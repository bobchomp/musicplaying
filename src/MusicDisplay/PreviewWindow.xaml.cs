using System.Windows;
using MusicDisplay.Services;

namespace MusicDisplay;

/// <summary>
/// A plain, resizable window showing exactly what the fullscreen display would show, so you
/// can check a layout or that art/text look right without kicking the real thing to a monitor.
/// </summary>
public partial class PreviewWindow : Window
{
    public PreviewWindow()
    {
        InitializeComponent();
    }

    public void UpdateNowPlaying(NowPlayingInfo? info) => View.UpdateNowPlaying(info);

    public void SetBlanked(bool blanked) => View.SetBlanked(blanked);

    public void SetLayout(DisplayLayout layout) => View.SetLayout(layout);

    public void SetShowClock(bool show) => View.SetShowClock(show);

    public void SetLyrics(IReadOnlyList<LyricsLine>? lines) => View.SetLyrics(lines);

    public void SetShowLyrics(bool show) => View.SetShowLyrics(show);

    public void UpdatePosition(PlaybackPosition? position) => View.UpdatePosition(position);
}
