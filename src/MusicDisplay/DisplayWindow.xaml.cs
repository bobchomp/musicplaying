using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MusicDisplay.Services;
using WinForms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace MusicDisplay;

/// <summary>
/// The borderless, always-on-top "now playing" screen. Positioning is done with a direct
/// SetWindowPos call using the target monitor's physical pixel bounds (rather than WPF's
/// Left/Top/Width/Height, which are DPI-scaled device-independent units) so the window lands
/// pixel-perfect on the chosen monitor even when monitors run at different DPI scales.
/// </summary>
public partial class DisplayWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>Raised when the user dismisses the display themselves (Ctrl+Q).</summary>
    public event Action? DismissedByUser;

    public DisplayWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DismissedByUser?.Invoke();
        }
    }

    public void ShowOnScreen(WinForms.Screen screen)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var bounds = screen.Bounds;
        SetWindowPos(hwnd, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpShowWindow | SwpNoActivate);
        Show();
    }

    public void MoveToScreen(WinForms.Screen screen)
    {
        if (!IsVisible)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var bounds = screen.Bounds;
        SetWindowPos(hwnd, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpShowWindow | SwpNoActivate);
    }

    public void HideDisplay() => Hide();

    public void UpdateNowPlaying(NowPlayingInfo? info) => View.UpdateNowPlaying(info);

    public void SetBlanked(bool blanked) => View.SetBlanked(blanked);

    public void SetLayout(DisplayLayout layout) => View.SetLayout(layout);

    public void SetShowClock(bool show) => View.SetShowClock(show);

    public void SetLyrics(IReadOnlyList<LyricsLine>? lines) => View.SetLyrics(lines);

    public void SetShowLyrics(bool show) => View.SetShowLyrics(show);

    public void UpdatePosition(PlaybackPosition? position) => View.UpdatePosition(position);

    /// <summary>Switches from the normal now-playing view to a full-bleed video, with sound.
    /// Returns false if the video couldn't be started (most likely WebView2 isn't available) —
    /// the normal view is left showing in that case.</summary>
    public async Task<bool> ShowMusicVideoAsync(string videoId)
    {
        if (await MusicVideo.PlayAsync(videoId))
        {
            View.Visibility = Visibility.Collapsed;
            MusicVideo.Opacity = 1;
            MusicVideo.IsHitTestVisible = true;
            return true;
        }

        return false;
    }

    public async Task HideMusicVideoAsync()
    {
        MusicVideo.Opacity = 0;
        MusicVideo.IsHitTestVisible = false;
        View.Visibility = Visibility.Visible;
        await MusicVideo.StopAsync();
    }

    /// <summary>Forces this window's underlying native window handle to exist — without making
    /// it visible — so its whole content tree (in particular MusicVideo) has already been through
    /// a real WPF layout pass, and WebView2 has something to attach to, well before Show Music
    /// Video might be clicked. Without this, if the fullscreen display has never actually been
    /// shown on a monitor (Show Display never clicked), this window's content has never been laid
    /// out at all — no amount of juggling MusicVideo's own Visibility/Opacity fixes that, since
    /// the problem in that case is the whole window, not just one control in it.</summary>
    public void EnsureRealized() => new WindowInteropHelper(this).EnsureHandle();
}
