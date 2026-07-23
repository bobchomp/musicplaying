using System.IO;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using UserControl = System.Windows.Controls.UserControl;

namespace MusicDisplay;

/// <summary>
/// A thin wrapper around a WebView2 control that plays a YouTube video full-bleed, for the Show
/// Music Video feature. One instance is hosted in DisplayWindow and another in ControlPanelWindow's
/// embedded preview, mirroring how NowPlayingView itself is shared across both — each owns its own
/// WebView2 environment/browser process.
/// </summary>
public partial class MusicVideoPlayerView : UserControl
{
    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicDisplay",
        "music-video-debug.log");

    private CoreWebView2Environment? _environment;

    public MusicVideoPlayerView()
    {
        InitializeComponent();
    }

    /// <summary>Navigates to the given YouTube video's embed player and starts it playing with
    /// sound. Returns false if WebView2 couldn't be initialized (most likely the WebView2 Runtime
    /// isn't installed on this machine) — the caller is expected to report that rather than the
    /// video just silently not appearing.</summary>
    public async Task<bool> PlayAsync(string videoId)
    {
        if (!await EnsureInitializedAsync())
        {
            return false;
        }

        // playsinline avoids the mobile-style fullscreen takeover Chromium sometimes applies;
        // autoplay is otherwise blocked for unmuted video by Chromium's default autoplay policy,
        // which EnsureInitializedAsync below works around via a browser-launch argument, since a
        // programmatic Navigate() here doesn't itself count as the "user gesture" Chromium's
        // heuristic looks for.
        Browser.CoreWebView2!.Navigate($"https://www.youtube.com/embed/{videoId}?autoplay=1&playsinline=1");
        return true;
    }

    public async Task StopAsync()
    {
        if (!await EnsureInitializedAsync())
        {
            return;
        }

        Browser.CoreWebView2!.Navigate("about:blank");
    }

    private async Task<bool> EnsureInitializedAsync()
    {
        if (Browser.CoreWebView2 != null)
        {
            return true;
        }

        try
        {
            // Without this, YouTube's embedded player loads but Chromium blocks it from playing
            // with sound until something inside the page itself receives a real user click/tap —
            // there's no such interaction here, so autoplay would otherwise silently do nothing.
            _environment ??= await CoreWebView2Environment.CreateAsync(
                null,
                null,
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));

            await Browser.EnsureCoreWebView2Async(_environment);
            return true;
        }
        catch (Exception ex)
        {
            Log($"WebView2 initialization failed: {ex.Message}");
            return false;
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
}
