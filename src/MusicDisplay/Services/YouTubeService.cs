using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MusicDisplay.Services;

/// <summary>
/// Looks up a music video for the current track via the YouTube Data API v3 search endpoint,
/// using a user-supplied API key entered in Settings — there's no responsible way to find one
/// without a real API (scraping YouTube's own search results isn't something this app does).
/// Best-effort throughout: any failure (no key configured, no results, network error) just means
/// no video is found, same as the rest of this app's optional features.
/// </summary>
public sealed class YouTubeService
{
    private const string SearchUrl = "https://www.googleapis.com/youtube/v3/search";

    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicDisplay",
        "youtube-debug.log");

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Searches for a music video matching the given title/artist. Returns null if no
    /// API key is configured, no results are found, or the request fails for any reason.</summary>
    public async Task<string?> FindMusicVideoIdAsync(string title, string artist, string apiKey)
    {
        Log($"FindMusicVideoIdAsync start: title=\"{title}\" artist=\"{artist}\" apiKeyLength={apiKey?.Length ?? 0}");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log("no API key configured — returning null without calling the API");
            return null;
        }

        try
        {
            var query = Uri.EscapeDataString($"{artist} {title} official music video");
            var url = $"{SearchUrl}?part=snippet&type=video&videoEmbeddable=true&maxResults=1&q={query}&key={Uri.EscapeDataString(apiKey)}";

            Log("sending GET /search...");
            using var response = await Client.GetAsync(url);
            Log($"GET /search -> {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // This is the important part for diagnosing a "not working" report: the status
                // code alone doesn't say WHY (bad key, API not enabled for the project, quota
                // exceeded, key restricted to the wrong API, etc.) — Google's response body does,
                // in a human-readable message, so log it rather than just the code.
                Log($"error response body: {Truncate(json, 500)}");
                return null;
            }

            var result = JsonSerializer.Deserialize<SearchResponse>(json, JsonOptions);
            var videoId = result?.Items?.Count > 0 ? result.Items[0].Id?.VideoId : null;

            if (string.IsNullOrEmpty(videoId))
            {
                Log($"no results for \"{title}\" — \"{artist}\"");
                return null;
            }

            Log($"found video {videoId} for \"{title}\" — \"{artist}\"");
            return videoId;
        }
        catch (Exception ex)
        {
            Log($"search failed for \"{title}\" — \"{artist}\": {ex}");
            return null;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

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

    private sealed class SearchResponse
    {
        public List<SearchItem>? Items { get; set; }
    }

    private sealed class SearchItem
    {
        public SearchItemId? Id { get; set; }
    }

    private sealed class SearchItemId
    {
        public string? VideoId { get; set; }
    }
}
