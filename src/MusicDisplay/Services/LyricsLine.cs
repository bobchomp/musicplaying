namespace MusicDisplay.Services;

/// <summary>One timestamped line of synced (LRC-format) lyrics.</summary>
public readonly record struct LyricsLine(TimeSpan Time, string Text);
