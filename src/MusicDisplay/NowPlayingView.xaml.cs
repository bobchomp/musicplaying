using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private static readonly Color IdleBackgroundColor = (Color)ColorConverter.ConvertFromString("#0B0B0D")!;
    private static readonly Random EqualizerRandom = new();

    private NowPlayingInfo? _lastInfo;
    private bool _isBlanked;
    private DisplayLayout _layout = DisplayLayout.Centered;

    public NowPlayingView()
    {
        InitializeComponent();
        StartEqualizerAnimation();
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

    public void SetLayout(DisplayLayout layout)
    {
        _layout = layout;

        switch (layout)
        {
            case DisplayLayout.Left:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
                ContentPanel.Width = EdgeLayoutWidth;
                ContentPanel.Margin = new Thickness(EdgeLayoutInset, 0, 0, 0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Left;
                TitleText.TextAlignment = TextAlignment.Left;
                ArtistText.TextAlignment = TextAlignment.Left;
                EqualizerPanel.HorizontalAlignment = HorizontalAlignment.Right;
                EqualizerPanel.Margin = new Thickness(0, 0, EdgeLayoutInset, 0);
                break;

            case DisplayLayout.Right:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Right;
                ContentPanel.Width = EdgeLayoutWidth;
                ContentPanel.Margin = new Thickness(0, 0, EdgeLayoutInset, 0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Right;
                TitleText.TextAlignment = TextAlignment.Right;
                ArtistText.TextAlignment = TextAlignment.Right;
                EqualizerPanel.HorizontalAlignment = HorizontalAlignment.Left;
                EqualizerPanel.Margin = new Thickness(EdgeLayoutInset, 0, 0, 0);
                break;

            default:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Center;
                ContentPanel.Width = double.NaN;
                ContentPanel.Margin = new Thickness(0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Center;
                TitleText.TextAlignment = TextAlignment.Center;
                ArtistText.TextAlignment = TextAlignment.Center;
                EqualizerPanel.HorizontalAlignment = HorizontalAlignment.Center;
                EqualizerPanel.Margin = new Thickness(0);
                break;
        }

        Render();
    }

    private void Render()
    {
        var info = _lastInfo;
        bool hasTrack = !_isBlanked && info != null && !string.IsNullOrWhiteSpace(info.Title);

        // The equalizer only makes sense filling the empty space beside an edge-aligned layout;
        // Centered has no single obvious empty side to put it in.
        EqualizerPanel.Visibility = hasTrack && _layout != DisplayLayout.Centered
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        var animation = new DoubleAnimation
        {
            From = 40 + EqualizerRandom.NextDouble() * 50,
            To = 150 + EqualizerRandom.NextDouble() * 110,
            Duration = new Duration(TimeSpan.FromMilliseconds(500 + EqualizerRandom.Next(500))),
            BeginTime = TimeSpan.FromMilliseconds(EqualizerRandom.Next(400)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        bar.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }
}
