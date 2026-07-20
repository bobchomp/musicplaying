namespace MusicDisplay.Services;

/// <summary>A playback-position snapshot from SMTC, used to interpolate a live position between
/// updates: actual position is <c>Position + (elapsed since LastUpdatedTime) * PlaybackRate</c>
/// while playing.</summary>
public readonly record struct PlaybackPosition(TimeSpan Position, DateTime LastUpdatedTime, double PlaybackRate);
