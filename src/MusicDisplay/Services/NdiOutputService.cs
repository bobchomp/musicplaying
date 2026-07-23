using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Size = System.Windows.Size;

namespace MusicDisplay.Services;

/// <summary>
/// Broadcasts the current now-playing screen as an NDI source on the local network, so it can
/// be picked up by NDI-aware software on another computer — including via NDI Tools' "NDI
/// Virtual Input", which re-publishes an NDI source as a local virtual webcam that apps like
/// EasyWorship can add as a live feed.
///
/// Captures a dedicated, off-screen <see cref="NowPlayingView"/> instance (measured/arranged
/// but never hosted in a shown Window) rather than whatever's on the physical monitor, so the
/// network feed works independently of whether the fullscreen <see cref="DisplayWindow"/> is
/// currently shown — it mirrors the same layout/blank/clock/track state via the same methods
/// DisplayWindow and ControlPanelWindow's embedded preview use.
/// </summary>
public sealed class NdiOutputService : IDisposable
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int FrameRate = 15;
    private const int Stride = Width * 4;
    private const int BufferSize = Stride * Height;

    private readonly NowPlayingView _captureView = new();
    private readonly DispatcherTimer _timer;
    private readonly RenderTargetBitmap _renderTarget = new(Width, Height, 96, 96, PixelFormats.Pbgra32);

    // Two buffers, alternated each frame, because frames are sent with the async NDI API below —
    // it returns immediately rather than blocking until the frame is actually transmitted, so the
    // buffer just sent can still be in use by the SDK when the next tick comes around. Writing
    // into the buffer NOT currently in flight avoids ever touching one out from under it.
    private readonly IntPtr _pixelBufferA = Marshal.AllocHGlobal(BufferSize);
    private readonly IntPtr _pixelBufferB = Marshal.AllocHGlobal(BufferSize);
    private bool _useBufferA = true;

    private IntPtr _sendInstance = IntPtr.Zero;
    private IntPtr _sourceNamePtr = IntPtr.Zero;
    private bool _isRunning;
    private bool _isDisposed;
    private bool _hasInitialized;

    /// <summary>False once <see cref="Start"/> has failed (NDI Runtime missing or init failed),
    /// so the UI can disable the toggle instead of letting the user retry forever.</summary>
    public bool IsAvailable { get; private set; } = true;

    public bool IsRunning => _isRunning;

    public NdiOutputService()
    {
        _captureView.Measure(new Size(Width, Height));
        _captureView.Arrange(new Rect(0, 0, Width, Height));

        // Render rather than Background: this timer competes for the same UI thread as the
        // equalizer/title-scroll animations and the control panel's own event handlers, and
        // Background priority meant it could keep getting pushed behind a backlog of that other
        // work, arriving at uneven intervals and reading as jitter in the output — Render is the
        // same priority WPF's own layout/render passes use, which keeps this ticking close to
        // every frame instead of only whenever the queue happens to run dry.
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1.0 / FrameRate),
        };
        _timer.Tick += (_, _) => CaptureAndSendFrame();
    }

    public void UpdateNowPlaying(NowPlayingInfo? info) => _captureView.UpdateNowPlaying(info);

    public void SetLayout(DisplayLayout layout) => _captureView.SetLayout(layout);

    public void SetBlanked(bool blanked) => _captureView.SetBlanked(blanked);

    public void SetShowClock(bool show) => _captureView.SetShowClock(show);

    public void SetLyrics(IReadOnlyList<LyricsLine>? lines) => _captureView.SetLyrics(lines);

    public void SetShowLyrics(bool show) => _captureView.SetShowLyrics(show);

    public void UpdatePosition(PlaybackPosition? position) => _captureView.UpdatePosition(position);

    /// <summary>Starts broadcasting under the given NDI source name. Returns false (without
    /// throwing) if the NDI Runtime isn't installed or initialization otherwise fails.</summary>
    public bool Start(string sourceName)
    {
        if (_isRunning)
        {
            return true;
        }

        if (!IsAvailable)
        {
            return false;
        }

        if (!NdiInterop.TryLocateAndPrepareLibrary())
        {
            IsAvailable = false;
            return false;
        }

        try
        {
            if (!NdiInterop.NDIlib_initialize())
            {
                IsAvailable = false;
                return false;
            }

            _hasInitialized = true;

            _sourceNamePtr = NdiInterop.AllocUtf8(sourceName);
            var createSettings = new NdiInterop.SendCreate
            {
                NdiName = _sourceNamePtr,
                Groups = IntPtr.Zero,
                ClockVideo = false,
                ClockAudio = false,
            };

            _sendInstance = NdiInterop.NDIlib_send_create(ref createSettings);
            if (_sendInstance == IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sourceNamePtr);
                _sourceNamePtr = IntPtr.Zero;
                IsAvailable = false;
                return false;
            }

            _isRunning = true;
            _timer.Start();
            return true;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _timer.Stop();
        _isRunning = false;

        if (_sendInstance != IntPtr.Zero)
        {
            // Waits for any in-flight async frame to actually finish sending, so neither buffer
            // is still in use by the SDK by the time NDIlib_send_destroy (or eventually Dispose's
            // Marshal.FreeHGlobal) runs.
            NdiInterop.NDIlib_send_send_video_async_v2_Flush(_sendInstance, IntPtr.Zero);
            NdiInterop.NDIlib_send_destroy(_sendInstance);
            _sendInstance = IntPtr.Zero;
        }

        if (_sourceNamePtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_sourceNamePtr);
            _sourceNamePtr = IntPtr.Zero;
        }
    }

    private void CaptureAndSendFrame()
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            _captureView.UpdateLayout();

            // NowPlayingView's background is always fully opaque, so re-rendering onto the same
            // target every tick fully overwrites the previous frame with no need to clear first.
            _renderTarget.Render(_captureView);

            var buffer = _useBufferA ? _pixelBufferA : _pixelBufferB;
            _useBufferA = !_useBufferA;
            _renderTarget.CopyPixels(new Int32Rect(0, 0, Width, Height), buffer, BufferSize, Stride);

            var frame = new NdiInterop.VideoFrame
            {
                Xres = Width,
                Yres = Height,
                FourCc = NdiInterop.FourCcBgra,
                FrameRateN = FrameRate,
                FrameRateD = 1,
                PictureAspectRatio = (float)Width / Height,
                FrameFormatType = NdiInterop.FrameFormatProgressive,
                Timecode = NdiInterop.SynthesizeTimecode,
                PData = buffer,
                LineStrideInBytes = Stride,
                PMetadata = IntPtr.Zero,
                Timestamp = NdiInterop.SynthesizeTimecode,
            };

            // Async rather than the plain v2 call: that one blocks this (UI) thread until the
            // frame is actually transmitted, which can take an unpredictable amount of time
            // depending on the receiver/network — exactly the kind of stall that reads as jitter,
            // and on the same thread the rest of the app's animations depend on. This queues the
            // frame and returns immediately; the buffer alternation above keeps that safe.
            NdiInterop.NDIlib_send_send_video_async_v2(_sendInstance, ref frame);
        }
        catch (Exception)
        {
            // Best-effort; skip this frame rather than tearing down the whole feed over it.
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        Marshal.FreeHGlobal(_pixelBufferA);
        Marshal.FreeHGlobal(_pixelBufferB);

        if (_hasInitialized)
        {
            NdiInterop.NDIlib_destroy();
        }
    }
}
