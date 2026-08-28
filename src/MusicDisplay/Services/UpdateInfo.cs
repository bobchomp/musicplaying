namespace MusicDisplay.Services;

/// <summary>A newer release found on GitHub, and where to download its installer from.</summary>
public sealed record UpdateInfo(Version Version, string DownloadUrl);
