using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MusicDisplay.Services;

/// <summary>
/// Reads the current system "now playing" media (title, artist, artwork, play state) via the
/// Windows System Media Transport Controls (SMTC) API. This is the same mechanism behind the
/// Windows volume flyout's media controls, so it works automatically with Spotify, browsers,
/// Windows Media Player, VLC, iTunes, etc. without any per-app integration. Requires Windows 10
/// 1809 (build 17763) or later.
/// </summary>
public sealed class NowPlayingService : IDisposable
{
    public event Action<NowPlayingInfo?>? NowPlayingChanged;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        AttachToSession(_manager.GetCurrentSession());
        await RefreshAsync();
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        AttachToSession(sender.GetCurrentSession());
        _ = RefreshAsync();
    }

    private void AttachToSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _currentSession = session;

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();

    /// <summary>Sends a play/pause/skip command to whichever app owns the current session. Best-effort:
    /// silently does nothing if there's no session or the source app doesn't support the command.</summary>
    public Task PlayPauseAsync() => SendCommandAsync(session => session.TryTogglePlayPauseAsync());

    public Task NextAsync() => SendCommandAsync(session => session.TrySkipNextAsync());

    public Task PreviousAsync() => SendCommandAsync(session => session.TrySkipPreviousAsync());

    private async Task SendCommandAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        var session = _currentSession;
        if (session == null)
        {
            return;
        }

        try
        {
            await command(session);
        }
        catch (Exception)
        {
            // The source app may not support this command, or the session may have just ended.
        }
    }

    private async Task RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var session = _currentSession;
            if (session == null)
            {
                NowPlayingChanged?.Invoke(null);
                return;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties? props;
            try
            {
                props = await session.TryGetMediaPropertiesAsync();
            }
            catch (Exception)
            {
                // The session can vanish between GetCurrentSession() and the property fetch.
                NowPlayingChanged?.Invoke(null);
                return;
            }

            var playback = session.GetPlaybackInfo();
            var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            BitmapImage? thumbnail = null;
            if (props?.Thumbnail != null)
            {
                thumbnail = await LoadThumbnailAsync(props.Thumbnail);
            }

            // Not every source app supports timeline properties (position/duration), so this is
            // best-effort — lyrics sync is simply unavailable if it throws or comes back empty.
            TimeSpan? duration = null;
            PlaybackPosition? position = null;
            try
            {
                var timeline = session.GetTimelineProperties();
                if (timeline.EndTime > timeline.StartTime)
                {
                    duration = timeline.EndTime - timeline.StartTime;
                }

                position = new PlaybackPosition(timeline.Position, timeline.LastUpdatedTime.UtcDateTime, playback?.PlaybackRate ?? 1.0);
            }
            catch (Exception)
            {
                // Best-effort; lyrics sync just won't be available for this session.
            }

            var info = new NowPlayingInfo(
                props?.Title ?? string.Empty,
                props?.Artist ?? string.Empty,
                thumbnail,
                isPlaying,
                duration,
                position);

            NowPlayingChanged?.Invoke(info);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
            var bytes = new byte[stream.Size];

            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            using var memory = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        AttachToSession(null);
        _refreshLock.Dispose();
    }
}
