using System.IO;
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
/// and the embedded preview in <see cref="ControlPanelWindow"/> so both always render identically.
/// </summary>
public partial class NowPlayingView : UserControl
{
    private const double EdgeLayoutWidth = 900;
    private const double EdgeLayoutInset = 160;
    private const double CenteredTitleWidth = 1500;
    private const double TitleScrollEdgePadding = 40;
    private const double TitleScrollPixelsPerSecond = 110;
    private static readonly TimeSpan LyricsPollInterval = TimeSpan.FromMilliseconds(250);

    // A small hold past each line's own LRC timestamp before switching to it — enough to not
    // feel like it's anticipating the line before it's sung, but small enough that it doesn't
    // read as lagging behind the vocal. Cut down twice now on real-world feedback (350ms, then
    // 100ms), both times because it was still switching after the line had already started.
    private static readonly TimeSpan LyricsAdvanceDelay = TimeSpan.FromMilliseconds(30);
    private static readonly Duration LayoutFadeOutDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration LayoutFadeInDuration = new(TimeSpan.FromMilliseconds(220));
    private const double EdgeLyricLineHeight = 90;
    private static readonly Duration EdgeLyricsSlideDuration = new(TimeSpan.FromMilliseconds(380));

    private static readonly Color IdleBackgroundColor = (Color)ColorConverter.ConvertFromString("#0B0B0D")!;
    private static readonly Random EqualizerRandom = new();
    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicDisplay",
        "title-scroll-debug.log");
    private static readonly string LyricsTickerDebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicDisplay",
        "lyrics-ticker-debug.log");

    private NowPlayingInfo? _lastInfo;
    private bool _isBlanked;
    private bool _showClock;
    private DisplayLayout _layout = DisplayLayout.Centered;
    private bool _hasAppliedLayout;
    private double _titleClipWidth = CenteredTitleWidth;
    private TextAlignment _titleAlignment = TextAlignment.Center;
    private string? _titleScrollAppliedFor;
    private readonly List<AnimationClock> _equalizerClocks = new();
    private bool _isEqualizerPlaying = true;
    private IReadOnlyList<LyricsLine>? _lyrics;
    private bool _showLyrics;
    private int _currentLyricIndex = -1;
    private PlaybackPosition? _lastPosition;
    private DispatcherTimer? _lyricsTimer;
    private bool _edgeLyricsSliding;

    // Only used to tag debug log lines, since DisplayWindow, ControlPanelWindow's embedded
    // preview, and NdiOutputService each own a separate NowPlayingView instance and all three log
    // to the same shared file — without this, two different instances evaluating the same track
    // moments apart looks identical to one instance re-evaluating (resetting) itself.
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..6];

    public NowPlayingView()
    {
        InitializeComponent();
        StartEqualizerAnimation();
        StartClock();
    }

    public void UpdateNowPlaying(NowPlayingInfo? info)
    {
        _lastInfo = info;
        _lastPosition = info?.Position;
        Render();
        UpdateLyricsVisibility();
    }

    /// <summary>Temporarily hides the album art and text without changing anything else.</summary>
    public void SetBlanked(bool blanked)
    {
        _isBlanked = blanked;
        Render();
        UpdateLyricsVisibility();
    }

    /// <summary>Shows a live clock stacked below the equalizer bars (Album left/right layouts only).</summary>
    public void SetShowClock(bool show)
    {
        _showClock = show;
        Render();
    }

    /// <summary>Sets the synced lyrics for the current track (null if none are available), reset
    /// on every track change by the caller regardless of whether the fetch found anything.</summary>
    public void SetLyrics(IReadOnlyList<LyricsLine>? lines)
    {
        _lyrics = lines is { Count: > 0 } ? lines : null;
        _currentLyricIndex = -1;
        UpdateLyricsVisibility();
    }

    /// <summary>Overrides the cached position anchor with a freshly-queried one and, if the
    /// lyrics timer is already running, immediately re-syncs to it — used when lyrics are
    /// switched on, so the current line lands correctly right away instead of waiting for the
    /// next SMTC-driven update (which some sources delay or batch).</summary>
    public void UpdatePosition(PlaybackPosition? position)
    {
        _lastPosition = position;
        if (_lyricsTimer != null)
        {
            UpdateCurrentLyricLine();
        }
    }

    /// <summary>Shows the current synced lyric line, karaoke-style, when available.</summary>
    public void SetShowLyrics(bool show)
    {
        _showLyrics = show;
        UpdateLyricsVisibility();
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
            UpdateLyricsVisibility();
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
            UpdateLyricsVisibility();

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
                _titleAlignment = TextAlignment.Left;
                _titleClipWidth = EdgeLayoutWidth;
                ArtistText.TextAlignment = TextAlignment.Left;
                IdlePanel.HorizontalAlignment = HorizontalAlignment.Left;
                IdleText.TextAlignment = TextAlignment.Left;
                IdlePanel.Margin = new Thickness(EdgeLayoutInset, 0, 0, 0);
                SidePanel.HorizontalAlignment = HorizontalAlignment.Right;
                SidePanel.Margin = new Thickness(0, 0, EdgeLayoutInset, 0);
                break;

            case DisplayLayout.Right:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Right;
                ContentPanel.Width = EdgeLayoutWidth;
                ContentPanel.Margin = new Thickness(0, 0, EdgeLayoutInset, 0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Right;
                _titleAlignment = TextAlignment.Right;
                _titleClipWidth = EdgeLayoutWidth;
                ArtistText.TextAlignment = TextAlignment.Right;
                IdlePanel.HorizontalAlignment = HorizontalAlignment.Right;
                IdleText.TextAlignment = TextAlignment.Right;
                IdlePanel.Margin = new Thickness(0, 0, EdgeLayoutInset, 0);
                SidePanel.HorizontalAlignment = HorizontalAlignment.Left;
                SidePanel.Margin = new Thickness(EdgeLayoutInset, 0, 0, 0);
                break;

            default:
                ContentPanel.HorizontalAlignment = HorizontalAlignment.Center;
                ContentPanel.Width = double.NaN;
                ContentPanel.Margin = new Thickness(0);
                ArtBorder.HorizontalAlignment = HorizontalAlignment.Center;
                _titleAlignment = TextAlignment.Center;
                _titleClipWidth = CenteredTitleWidth;
                ArtistText.TextAlignment = TextAlignment.Center;
                IdlePanel.HorizontalAlignment = HorizontalAlignment.Center;
                IdleText.TextAlignment = TextAlignment.Center;
                IdlePanel.Margin = new Thickness(0);
                SidePanel.HorizontalAlignment = HorizontalAlignment.Center;
                SidePanel.Margin = new Thickness(0);
                break;
        }

        TitleClip.Width = _titleClipWidth;
        EvaluateTitleScroll();
        _titleScrollAppliedFor = TitleText.Text;
    }

    private void Render()
    {
        var info = _lastInfo;
        bool hasTrack = info != null && !string.IsNullOrWhiteSpace(info.Title);
        bool onEdgeLayout = _layout != DisplayLayout.Centered;

        // The side panel only makes sense filling the empty space beside an edge-aligned layout;
        // Centered has no single obvious empty side to put it in.
        bool equalizerVisible = !_isBlanked && hasTrack && onEdgeLayout;
        bool edgeClockVisible = _showClock && onEdgeLayout;
        SetEqualizerPlaying(info?.IsPlaying ?? false);
        EqualizerPanel.Visibility = equalizerVisible ? Visibility.Visible : Visibility.Collapsed;
        ClockPanel.Visibility = edgeClockVisible ? Visibility.Visible : Visibility.Collapsed;
        SidePanel.Visibility = equalizerVisible || edgeClockVisible ? Visibility.Visible : Visibility.Collapsed;

        // Centered has no side panel to show a clock in, so it gets this bottom-anchored one
        // instead, in the same spot whether it's showing because nothing's playing or because the
        // screen is blanked — computed here, above the blanked/idle branches below, so it stays in
        // that exact same place either way rather than only appearing for one of the two.
        IdleClockPanel.Visibility = _showClock && !onEdgeLayout && (_isBlanked || !hasTrack)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_isBlanked)
        {
            // Deliberately not touching the title scroll here: ContentPanel is hidden either way,
            // so whatever's mid-flight simply isn't visible — no need to stop or reset it, and
            // leaving it running means it doesn't lose its place while blanked.
            ContentPanel.Visibility = Visibility.Collapsed;
            IdlePanel.Visibility = Visibility.Collapsed;
            AnimateBackgroundTo(IdleBackgroundColor);
            return;
        }

        if (!hasTrack)
        {
            ContentPanel.Visibility = Visibility.Collapsed;
            IdlePanel.Visibility = Visibility.Visible;
            AnimateBackgroundTo(IdleBackgroundColor);
            return;
        }

        IdlePanel.Visibility = Visibility.Collapsed;
        TitleText.Text = info!.Title;
        ArtistText.Text = info.Artist;
        AlbumArtImage.Source = info.Thumbnail;
        ContentPanel.Visibility = Visibility.Visible;

        // NowPlayingChanged (and so Render) can fire repeatedly for the same track — many media
        // sources raise SMTC's PlaybackInfoChanged well beyond actual play/pause toggles, and
        // NowPlayingService can also report a brief null/no-session blip for a track that's still
        // really playing. Only re-evaluating when the title text actually changes (and never
        // resetting that above, on the merely-hidden paths) stops any of that from restarting the
        // scroll animation before it ever completes a lap.
        if (_titleScrollAppliedFor != info.Title)
        {
            _titleScrollAppliedFor = info.Title;
            EvaluateTitleScroll();
        }

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

    private void AnimateEqualizerBar(FrameworkElement bar)
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

        // Applied via a controllable clock (rather than BeginAnimation) so SetEqualizerPlaying
        // can pause/resume it in place when playback pauses, instead of only being able to stop
        // it outright and lose the bar's current height.
        var clock = animation.CreateClock();
        scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, clock);
        _equalizerClocks.Add(clock);
    }

    private void SetEqualizerPlaying(bool playing)
    {
        if (_isEqualizerPlaying == playing)
        {
            return;
        }

        _isEqualizerPlaying = playing;
        foreach (var clock in _equalizerClocks)
        {
            if (playing)
            {
                clock.Controller?.Resume();
            }
            else
            {
                clock.Controller?.Pause();
            }
        }
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
        var time = now.ToString("t");
        var date = now.ToString("D");
        ClockTimeText.Text = time;
        ClockDateText.Text = date;
        IdleClockTimeText.Text = time;
        IdleClockDateText.Text = date;
    }

    private void StopTitleScroll()
    {
        TitleText.BeginAnimation(Canvas.LeftProperty, null);
        Canvas.SetLeft(TitleText, 0);
    }

    /// <summary>Measures the title text against the current per-layout clip width (set in
    /// <see cref="ApplyLayout"/>) and, if it's too wide for one line, scrolls it back and forth
    /// instead of wrapping — wrapping would push the artist text (and everything below it)
    /// further down or off screen.</summary>
    private void EvaluateTitleScroll()
    {
        StopTitleScroll();

        if (string.IsNullOrEmpty(TitleText.Text))
        {
            return;
        }

        // Measuring TitleText itself (rather than reconstructing a Typeface for FormattedText)
        // guarantees this exactly matches how the TextBlock will actually render — rebuilding a
        // Typeface from FontFamily/FontWeight risked not matching "Poppins SemiBold", which is a
        // distinct embedded font family rather than a font-weight variant, and undershot the
        // real width, cutting the scroll short before it ever reached the end of the title.
        //
        // Width is explicitly reset to NaN (unset) first: FrameworkElement.Measure clamps its
        // result to any already-set explicit Width regardless of the availableSize passed in, so
        // a leftover Width from a *previous*, differently-sized title would otherwise cap this
        // measurement — which is exactly what was capping the scroll distance far too short.
        // With a Canvas as the parent (which arranges children at their own natural DesiredSize,
        // not stretched to anything), an explicit Width isn't needed here at all any more.
        TitleText.Width = double.NaN;
        TitleText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double naturalWidth = TitleText.DesiredSize.Width;

        double overflow = naturalWidth - _titleClipWidth;
        LogTitleScrollDebug(
            $"[{_instanceId}] text=\"{TitleText.Text}\" clipWidth={_titleClipWidth:0.#} naturalWidth={naturalWidth:0.#} overflow={overflow:0.#} " +
            $"TitleClip.ActualWidth={TitleClip.ActualWidth:0.#} TitleText.ActualWidth={TitleText.ActualWidth:0.#}");

        if (overflow <= 0)
        {
            Canvas.SetLeft(TitleText, _titleAlignment switch
            {
                TextAlignment.Center => (_titleClipWidth - naturalWidth) / 2,
                TextAlignment.Right => _titleClipWidth - naturalWidth,
                _ => 0,
            });
            return;
        }

        double distance = overflow + TitleScrollEdgePadding;
        var holdTime = TimeSpan.FromSeconds(1.2);
        var scrollTime = TimeSpan.FromSeconds(Math.Max(2, distance / TitleScrollPixelsPerSecond));
        var scrollEndTime = holdTime + scrollTime;
        var holdEndTime = scrollEndTime + holdTime;
        var cycleEndTime = holdEndTime + scrollTime;
        LogTitleScrollDebug($"[{_instanceId}] scrolling distance={distance:0.#} scrollTime={scrollTime.TotalSeconds:0.##}s");

        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(holdTime)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(scrollEndTime))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(holdEndTime)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(cycleEndTime))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });

        TitleText.BeginAnimation(Canvas.LeftProperty, animation);
    }

    private static void LogLyricsTickerDebug(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LyricsTickerDebugLogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(LyricsTickerDebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best-effort diagnostic logging; nothing actionable if this fails.
        }
    }

    private static void LogTitleScrollDebug(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(DebugLogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best-effort diagnostic logging; nothing actionable if this fails.
        }
    }

    /// <summary>Starts/stops the lyrics poll timer and shows/hides the current-line text based on
    /// whether lyrics are actually available and relevant right now — mirrors how
    /// <see cref="SetEqualizerPlaying"/> pauses/resumes in place rather than running unconditionally.
    /// Centered layout shows the single bottom-anchored line; Album left/right show the 3-line edge
    /// ticker instead, since only they have the side space for it.</summary>
    private void UpdateLyricsVisibility()
    {
        bool hasTrack = _lastInfo != null && !string.IsNullOrWhiteSpace(_lastInfo.Title);
        bool lyricsActive = _showLyrics && _lyrics != null && hasTrack && !_isBlanked && _lastPosition != null;
        bool onEdgeLayout = _layout != DisplayLayout.Centered;

        CurrentLyricText.Visibility = lyricsActive && !onEdgeLayout ? Visibility.Visible : Visibility.Collapsed;
        EdgeLyricsPanel.Visibility = lyricsActive && onEdgeLayout ? Visibility.Visible : Visibility.Collapsed;

        if (lyricsActive)
        {
            StartLyricsTimer();
        }
        else
        {
            StopLyricsTimer();
            CurrentLyricText.Text = string.Empty;
            SetEdgeLyricsWindow(-1);
        }
    }

    private void StartLyricsTimer()
    {
        if (_lyricsTimer != null)
        {
            return;
        }

        _lyricsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = LyricsPollInterval };
        _lyricsTimer.Tick += (_, _) => UpdateCurrentLyricLine();
        _lyricsTimer.Start();
        UpdateCurrentLyricLine();
    }

    private void StopLyricsTimer()
    {
        if (_lyricsTimer == null)
        {
            return;
        }

        _lyricsTimer.Stop();
        _lyricsTimer = null;
        _currentLyricIndex = -1;
    }

    private void UpdateCurrentLyricLine()
    {
        if (_lyrics == null || _lastPosition is not { } anchor)
        {
            return;
        }

        // Interpolate off the last SMTC-reported anchor rather than polling the session directly
        // here — position only needs to be "close enough" for line-granular sync, and this keeps
        // the session/session-lifetime bookkeeping entirely in NowPlayingService.
        var elapsed = _lastInfo?.IsPlaying == true
            ? (DateTime.UtcNow - anchor.LastUpdatedTime) * anchor.PlaybackRate
            : TimeSpan.Zero;
        var position = anchor.Position + elapsed;
        var displayPosition = position - LyricsAdvanceDelay;

        int index = -1;
        for (int i = 0; i < _lyrics.Count; i++)
        {
            if (_lyrics[i].Time > displayPosition)
            {
                break;
            }

            index = i;
        }

        if (index == _currentLyricIndex)
        {
            return;
        }

        int previousIndex = _currentLyricIndex;
        _currentLyricIndex = index;
        CurrentLyricText.Text = index >= 0 ? _lyrics[index].Text : string.Empty;
        LogLyricsTickerDebug(
            $"[{_instanceId}] index {previousIndex} -> {index} of {_lyrics.Count} " +
            $"nextText=\"{EdgeLyricLineOrEmpty(index + 1)}\" edgeVisible={EdgeLyricsPanel.Visibility} sliding={_edgeLyricsSliding}");
        AdvanceEdgeLyrics(previousIndex, index);
    }

    private string EdgeLyricLineOrEmpty(int index) =>
        _lyrics != null && index >= 0 && index < _lyrics.Count ? _lyrics[index].Text : string.Empty;

    /// <summary>Fills all 5 ticker slots — the 3 visible ones plus the extra line just outside the
    /// clip on each side — directly from <paramref name="index"/>, with no animation. Used for the
    /// initial line and for any jump that isn't a plain one-line step (seeking, rewinding, or
    /// lyrics just having turned on), where an animated slide wouldn't make sense anyway since the
    /// slots weren't pre-loaded with the right neighbouring text for it.</summary>
    private void SetEdgeLyricsWindow(int index)
    {
        _edgeLyricsSliding = false;
        EdgeLyricSlot0.Text = EdgeLyricLineOrEmpty(index - 2);
        EdgeLyricSlot1.Text = EdgeLyricLineOrEmpty(index - 1);
        EdgeLyricSlot2.Text = EdgeLyricLineOrEmpty(index);
        EdgeLyricSlot3.Text = EdgeLyricLineOrEmpty(index + 1);
        EdgeLyricSlot4.Text = EdgeLyricLineOrEmpty(index + 2);
        EdgeLyricsTransform.BeginAnimation(TranslateTransform.YProperty, null);
        EdgeLyricsTransform.Y = -EdgeLyricLineHeight;
        LogLyricsTickerDebug(
            $"[{_instanceId}] SetEdgeLyricsWindow({index}) slot0=\"{EdgeLyricSlot0.Text}\" slot1=\"{EdgeLyricSlot1.Text}\" " +
            $"slot2=\"{EdgeLyricSlot2.Text}\" slot3=\"{EdgeLyricSlot3.Text}\" slot4=\"{EdgeLyricSlot4.Text}\" " +
            $"transformY={EdgeLyricsTransform.Y} panelWidth={EdgeLyricsPanel.ActualWidth:0.#} panelHeight={EdgeLyricsPanel.ActualHeight:0.#} " +
            $"stackWidth={EdgeLyricsStack.ActualWidth:0.#} stackHeight={EdgeLyricsStack.ActualHeight:0.#}");
    }

    /// <summary>Slides the edge lyrics ticker up by one line when the line advances normally, so
    /// the slot that already held the upcoming line's text (loaded ahead of time by the previous
    /// call) scrolls into view instead of just popping to new text. Anything other than a plain
    /// one-line forward step just snaps straight to the new window instead — including while a
    /// previous slide is still in flight: BeginAnimation would otherwise silently replace it, and
    /// since its Completed handler is the only place the slots' text gets refreshed for the new
    /// index, that step's line would never appear at all — normal lyric pacing can easily advance
    /// a line again before the previous slide's ~380ms finishes.</summary>
    private void AdvanceEdgeLyrics(int previousIndex, int newIndex)
    {
        bool simpleForwardStep = previousIndex >= 0 && newIndex == previousIndex + 1;
        if (!simpleForwardStep || _edgeLyricsSliding)
        {
            LogLyricsTickerDebug(
                $"[{_instanceId}] AdvanceEdgeLyrics jump (simpleForwardStep={simpleForwardStep} alreadySliding={_edgeLyricsSliding})");
            SetEdgeLyricsWindow(newIndex);
            return;
        }

        LogLyricsTickerDebug($"[{_instanceId}] AdvanceEdgeLyrics slide starting");
        _edgeLyricsSliding = true;
        var animation = new DoubleAnimation
        {
            From = -EdgeLyricLineHeight,
            To = -2 * EdgeLyricLineHeight,
            Duration = EdgeLyricsSlideDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        animation.Completed += (_, _) => SetEdgeLyricsWindow(newIndex);
        EdgeLyricsTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }
}
