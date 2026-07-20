using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicDisplay.Services;

/// <summary>
/// Fetches synced (LRC-format) lyrics from LRCLIB (https://lrclib.net) — a free, keyless lyrics
/// database — by track title and artist, for the optional karaoke-style lyrics overlay.
/// Best-effort throughout: any failure (no match, instrumental, no synced lyrics, network error)
/// just means no lyrics are shown, same as the rest of this app's optional features.
/// </summary>
public sealed class LyricsService
{
    private const string BaseUrl = "https://lrclib.net/api";

    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicDisplay",
        "lyrics-debug.log");

    private static readonly HttpClient Client = CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // LRC lines look like "[mm:ss.xx]lyric text"; non-matching lines (blank, or metadata tags
    // like "[ar:Artist]") are skipped.
    private static readonly Regex LrcLineRegex = new(@"^\[(\d+):(\d+)(?:\.(\d+))?\](.*)$", RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        client.DefaultRequestHeaders.Add("User-Agent", "MusicDisplay/1.0 (+https://github.com/bobchomp/musicplaying)");
        return client;
    }

    /// <summary>Looks up synced lyrics for a track. Returns null if none are found or available
    /// (including if only plain/unsynced lyrics exist — this feature only does synced display).</summary>
    public async Task<IReadOnlyList<LyricsLine>?> FetchAsync(string title, string artist, TimeSpan? duration)
    {
        try
        {
            var track = await GetExactMatchAsync(title, artist, duration) ?? await SearchAsync(title, artist);
            if (track == null || track.Instrumental || string.IsNullOrWhiteSpace(track.SyncedLyrics))
            {
                Log($"no synced lyrics for \"{title}\" — \"{artist}\"");
                return null;
            }

            var lines = ParseLrc(track.SyncedLyrics);
            Log($"found {lines.Count} synced line(s) for \"{title}\" — \"{artist}\"");
            return lines.Count > 0 ? lines : null;
        }
        catch (Exception ex)
        {
            Log($"fetch failed for \"{title}\" — \"{artist}\": {ex.Message}");
            return null;
        }
    }

    private async Task<LrcLibTrack?> GetExactMatchAsync(string title, string artist, TimeSpan? duration)
    {
        var url = $"{BaseUrl}/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (duration.HasValue)
        {
            url += $"&duration={(int)duration.Value.TotalSeconds}";
        }

        using var response = await Client.GetAsync(url);
        Log($"GET /get -> {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LrcLibTrack>(json, JsonOptions);
    }

    private async Task<LrcLibTrack?> SearchAsync(string title, string artist)
    {
        var url = $"{BaseUrl}/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";

        using var response = await Client.GetAsync(url);
        Log($"GET /search -> {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var results = JsonSerializer.Deserialize<List<LrcLibTrack>>(json, JsonOptions);
        return results is { Count: > 0 } ? results[0] : null;
    }

    private static List<LyricsLine> ParseLrc(string syncedLyrics)
    {
        var lines = new List<LyricsLine>();
        foreach (var rawLine in syncedLyrics.Split('\n'))
        {
            var match = LrcLineRegex.Match(rawLine.TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }

            int minutes = int.Parse(match.Groups[1].Value);
            int seconds = int.Parse(match.Groups[2].Value);
            int milliseconds = match.Groups[3].Success
                ? int.Parse(match.Groups[3].Value.PadRight(3, '0')[..3])
                : 0;
            var text = match.Groups[4].Value.Trim();

            if (!string.IsNullOrEmpty(text))
            {
                lines.Add(new LyricsLine(new TimeSpan(0, 0, minutes, seconds, milliseconds), text));
            }
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static void Log(string message)
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

    private sealed class LrcLibTrack
    {
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public double? Duration { get; set; }
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
    }
}
