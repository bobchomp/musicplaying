using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicDisplay.Services;

/// <summary>
/// Checks the GitHub releases API for a newer published version than the one currently running,
/// and downloads that release's installer .exe. Best-effort throughout, same as the other
/// optional network features in this app: any failure (offline, GitHub unreachable, no .exe
/// asset on the release) just means no update is reported, never a crash or a blocking error.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/bobchomp/musicplaying/releases/latest";

    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicDisplay",
        "update-debug.log");

    // A long timeout because it's shared with the installer download further below, not just the
    // small releases/latest JSON request — self-contained single-file publishes of this app run
    // well into the tens of megabytes.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    static UpdateService()
    {
        // GitHub's API rejects requests with no User-Agent at all.
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicDisplay-UpdateCheck");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Returns the newest published release if it's newer than the running app's own
    /// version and has a downloadable .exe asset, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        Log("checking for updates...");

        try
        {
            using var response = await Client.GetAsync(LatestReleaseUrl);
            Log($"GET /releases/latest -> {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);

            var tag = release?.TagName;
            if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var parsed))
            {
                Log($"couldn't parse a version from tag \"{tag}\"");
                return null;
            }

            // Normalized to a fixed 4-part form (release tags are always vMAJOR.MINOR.PATCH, so
            // Build is always present) — Version.TryParse leaves a missing Revision as -1 rather
            // than 0, which would otherwise make an installed version compare as *older* than the
            // exact same version freshly parsed from a tag, purely because of that -1 vs. 0.
            var latestVersion = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), 0);
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

            if (latestVersion <= currentVersion)
            {
                Log($"up to date (installed {currentVersion}, latest {latestVersion})");
                return null;
            }

            var asset = release?.Assets?.Find(a =>
                a.Name != null &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.BrowserDownloadUrl != null);

            if (asset == null)
            {
                Log($"newer release {latestVersion} found but it has no .exe asset");
                return null;
            }

            Log($"update available: {latestVersion} ({asset.BrowserDownloadUrl})");
            return new UpdateInfo(latestVersion, asset.BrowserDownloadUrl!);
        }
        catch (Exception ex)
        {
            Log($"update check failed: {ex}");
            return null;
        }
    }

    /// <summary>Downloads the given update's installer to a temp file and returns its local path,
    /// or null if the download fails. Reports fractional progress (0-1) as it goes, when the
    /// server provides a content length.</summary>
    public async Task<string?> DownloadInstallerAsync(UpdateInfo update, IProgress<double>? progress = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"MusicDisplaySetup-{update.Version}.exe");
        Log($"downloading installer to {path}...");

        try
        {
            using var response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(path);

            var buffer = new byte[81920];
            long readSoFar = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read));
                readSoFar += read;
                if (totalBytes is > 0)
                {
                    progress?.Report((double)readSoFar / totalBytes.Value);
                }
            }

            Log("download complete");
            return path;
        }
        catch (Exception ex)
        {
            Log($"installer download failed: {ex}");
            return null;
        }
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

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
