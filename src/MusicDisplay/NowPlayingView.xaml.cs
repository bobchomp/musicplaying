using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MusicDisplay.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextAlignment = System.Windows.TextAlignment;
using UserControl = System.Windows.Controls.UserControl;

namespace MusicDisplay;

/// <summary>
/// The album art / title / artist visual, shared by the fullscreen <see cref="DisplayWindow"/>
/// and the windowed <see cref="PreviewWindow"/> so both always render identically.
/// </summary>
public partial class NowPlayingView : UserControl
{
    private const double EdgeLayoutWidth = 900;
    private const double EdgeLayoutInset = 160;
    private static readonly Duration LayoutFadeOutDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration LayoutFadeInDuration = new(TimeSpan.FromMilliseconds(220));

    private static readonly Color IdleBackgroundColor = (Color)ColorConverter.ConvertFromString("#0B0B0D")!;
    private static readonly Random EqualizerRandom = new();

    private NowPlayingInfo? _lastInfo;
    private bool _isBlanked;
    private bool _showClock;
    private DisplayLayout _layout = DisplayLayout.Centered;
    private bool _hasAppliedLayout;

    public NowPlayingView()
    {
        InitializeComponent();
        StartEqualizerAnimation();
        StartClock();
    }

    public void UpdateNowPlaying(NowPlayingInfo? info)
    {
        _lastInfo = info;
        Render();
    }

    /// <summary>Temporarily hides the album art and text without changing anything else.</summary>
    public void SetBlanked(bool blanked)
    {
        _isBlanked = blanked;
        Render();
    }

    /// <summary>Shows a live clock stacked below the equalizer bars (Album left/right layouts only).</summary>
    public void SetShowClock(bool show)
    {
        _showClock = show;
        Render();
    }

    public void SetLayout(DisplayLayout layout)
    {
        // The very first layout application (app/preview startup) has nothing on screen yet to
        // transition from, so apply it immediately rather than fading in from nothing.
        if (!_hasAppliedLayout)
        {
            _hasAppliedLayout = true;
            _layout = layout;
            ApplyLayout(layout);
            Render();
            return;
        }

        if (layout == _layout)
        {
            return;
        }

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = LayoutFadeOutDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fadeOut.Completed += (_, _) =>
        {
            _layout = layout;
            ApplyLayout(layout);
            Render();

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = LayoutFadeInDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            LayoutGrid.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        LayoutGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ApplyLayout(DisplayLayout layout)
    {
        switch (layout)
        {
            case DisplayLayout.Left:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
                ContentPanel.Width = EdgeLayoutWidth;
                ContentPanel.Margin = new Thickness(EdgeLayoutInset, 0, 0, 0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Left;
                TitleText.TextAlignment = TextAlignment.Left;
                ArtistText.TextAlignment = TextAlignment.Left;
                SidePanel.HorizontalAlignment = HorizontalAlignment.Right;
                SidePanel.Margin = new Thickness(0, 0, EdgeLayoutInset, 0);
                break;

            case DisplayLayout.Right:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Right;
                ContentPanel.Width = EdgeLayoutWidth;
                ContentPanel.Margin = new Thickness(0, 0, EdgeLayoutInset, 0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Right;
                TitleText.TextAlignment = TextAlignment.Right;
                ArtistText.TextAlignment = TextAlignment.Right;
                SidePanel.HorizontalAlignment = HorizontalAlignment.Left;
                SidePanel.Margin = new Thickness(EdgeLayoutInset, 0, 0, 0);
                break;

            default:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Center;
                ContentPanel.Width = double.NaN;
                ContentPanel.Margin = new Thickness(0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Center;
                TitleText.TextAlignment = TextAlignment.Center;
                ArtistText.TextAlignment = TextAlignment.Center;
                SidePanel.HorizontalAlignment = HorizontalAlignment.Center;
                SidePanel.Margin = new Thickness(0);
                break;
        }
    }

    private void Render()
    {
        var info = _lastInfo;
        bool hasTrack = !_isBlanked && info != null && !string.IsNullOrWhiteSpace(info.Title);
        bool onEdgeLayout = _layout != DisplayLayout.Centered;

        // The side panel only makes sense filling the empty space beside an edge-aligned layout;
        // Centered has no single obvious empty side to put it in.
        bool equalizerVisible = hasTrack && onEdgeLayout;
        bool clockVisible = _showClock && onEdgeLayout;
        EqualizerPanel.Visibility = equalizerVisible ? Visibility.Visible : Visibility.Collapsed;
        ClockPanel.Visibility = clockVisible ? Visibility.Visible : Visibility.Collapsed;
        SidePanel.Visibility = equalizerVisible || clockVisible ? Visibility.Visible : Visibility.Collapsed;

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

    private void StartEqualizerAnimation()
    {
        foreach (var bar in new FrameworkElement[] { EqBar1, EqBar2, EqBar3, EqBar4, EqBar5, EqBar6 })
        {
            AnimateEqualizerBar(bar);
        }
    }

    private static void AnimateEqualizerBar(FrameworkElement bar)
    {
        // Animate the bar's own ScaleTransform (render thread) rather than its Height (a layout
        // property, which would force a UI-thread layout pass every animation frame and looks
        // jittery under any UI-thread load, e.g. while the NDI capture loop is running).
        if (bar.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 0.15 + EqualizerRandom.NextDouble() * 0.2,
            To = 0.6 + EqualizerRandom.NextDouble() * 0.4,
            Duration = new Duration(TimeSpan.FromMilliseconds(500 + EqualizerRandom.Next(500))),
            BeginTime = TimeSpan.FromMilliseconds(EqualizerRandom.Next(400)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void StartClock()
    {
        UpdateClockText();

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        timer.Tick += (_, _) => UpdateClockText();
        timer.Start();
    }

    private void UpdateClockText()
    {
        var now = DateTime.Now;
        ClockTimeText.Text = now.ToString("t");
        ClockDateText.Text = now.ToString("D");
    }
}
