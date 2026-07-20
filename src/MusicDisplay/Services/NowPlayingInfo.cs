using System.Windows.Media.Imaging;

namespace MusicDisplay.Services;

public sealed record NowPlayingInfo(
    string Title,
    string Artist,
    BitmapImage? Thumbnail,
    bool IsPlaying,
    TimeSpan? Duration = null,
    PlaybackPosition? Position = null);
