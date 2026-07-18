using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MusicDisplay.Services;
using WinForms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

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

    private static readonly Color IdleBackgroundColor = (Color)ColorConverter.ConvertFromString("#0B0B0D")!;

    /// <summary>Raised when the user dismisses the display themselves (Escape key).</summary>
    public event Action? DismissedByUser;

    public DisplayWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
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

    public void UpdateNowPlaying(NowPlayingInfo? info)
    {
        bool hasTrack = info != null && !string.IsNullOrWhiteSpace(info.Title);

        if (!hasTrack)
        {
            ContentPanel.Visibility = Visibility.Collapsed;
            AnimateBackgroundTo(IdleBackgroundColor);
            return;
        }

        TitleText.Text = info!.Title;
        ArtistText.Text = info.Artist;
        AlbumArtImage.Source = info.Thumbnail;
        ContentPanel.Visibility = Visibility.Visible;

        var accent = ColorExtractor.GetAccentColor(info.Thumbnail, IdleBackgroundColor);
        AnimateBackgroundTo(accent);
    }

    private void AnimateBackgroundTo(Color target)
    {
        var animation = new ColorAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(600)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        BackgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
}
