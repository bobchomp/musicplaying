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

    // YouTube's embed player expects to be running inside an <iframe> on a page with a real
    // HTTPS origin — its internal checks fail (YouTube's own "video player configuration error",
    // error 153) if it's loaded as a top-level navigation instead, which is what a plain
    // Navigate() straight to the embed URL does. SetVirtualHostNameToFolderMapping below serves a
    // small local HTML wrapper (with the video in an iframe, same as any normal embedding site)
    // from what WebView2 treats as a genuine https:// origin, even though it's actually reading
    // from a folder on disk.
    private const string VirtualHost = "musicdisplay.local";
    private static readonly string WrapperFolder = Path.Combine(Path.GetTempPath(), "MusicDisplayVideoPlayer");

    private CoreWebView2Environment? _environment;

    // DisplayWindow and ControlPanelWindow's embedded preview each own a separate instance of
    // this control. Used both to tag debug log lines (both instances log to the same
    // music-video-debug.log) and to give each instance its own wrapper HTML filename, so one
    // instance writing/navigating can never race the other's file out from under it.
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..6];

    private string WrapperFileName => $"player-{_instanceId}.html";

    /// <summary>Raised when the video finishes playing on its own (not when Stop is clicked) —
    /// the JS side posts a "ended" web message on the YouTube IFrame Player API's onStateChange,
    /// which WebMessageReceived below turns into this.</summary>
    public event Action? VideoEnded;

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
        Log($"PlayAsync({videoId}) start");
        if (!await EnsureInitializedAsync())
        {
            Log("PlayAsync: EnsureInitializedAsync failed, not navigating");
            return false;
        }

        // Built via the YouTube IFrame Player API (a JS API that creates its own iframe
        // internally), not a plain <iframe src="..."> — that's the only way to get an
        // onStateChange callback, which is how VideoEnded below knows when the video actually
        // finishes rather than the app having no idea at all. playsinline avoids the mobile-style
        // fullscreen takeover Chromium sometimes applies; autoplay is otherwise blocked for
        // unmuted video by Chromium's default autoplay policy, which EnsureInitializedAsync below
        // works around via a browser-launch argument, since nothing here counts as the "user
        // gesture" Chromium's heuristic looks for. $$ (not a single $): the CSS/JS below has
        // literal braces of their own, and raw interpolated strings don't use brace-doubling to
        // escape those the way regular interpolated strings do — with two $ signs, single braces
        // are always literal and an interpolation hole needs double braces instead, which is what
        // {{videoId}} is below.
        var html = $$"""
            <!DOCTYPE html>
            <html><head><style>
              html, body { margin: 0; background: #000; overflow: hidden; }
              #player { position: fixed; inset: 0; width: 100%; height: 100%; }
            </style></head>
            <body>
              <div id="player"></div>
              <script src="https://www.youtube.com/iframe_api"></script>
              <script>
                function onYouTubeIframeAPIReady() {
                  new YT.Player('player', {
                    videoId: '{{videoId}}',
                    playerVars: { autoplay: 1, playsinline: 1 },
                    events: {
                      onStateChange: function (event) {
                        if (event.data === YT.PlayerState.ENDED) {
                          window.chrome.webview.postMessage('ended');
                        }
                      }
                    }
                  });
                }
              </script>
            </body></html>
            """;
        await File.WriteAllTextAsync(Path.Combine(WrapperFolder, WrapperFileName), html);

        var url = $"https://{VirtualHost}/{WrapperFileName}";
        Log($"navigating to {url}");
        Browser.CoreWebView2!.Navigate(url);
        return true;
    }

    public async Task StopAsync()
    {
        Log("StopAsync");
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
            Log("EnsureInitializedAsync: already initialized");
            return true;
        }

        try
        {
            // Without this, YouTube's embedded player loads but Chromium blocks it from playing
            // with sound until something inside the page itself receives a real user click/tap —
            // there's no such interaction here, so autoplay would otherwise silently do nothing.
            if (_environment == null)
            {
                Log("creating CoreWebView2Environment...");
                _environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    null,
                    new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
                Log("CoreWebView2Environment created");
            }

            Log("EnsureCoreWebView2Async starting...");
            await Browser.EnsureCoreWebView2Async(_environment);
            Log("EnsureCoreWebView2Async completed");

            Directory.CreateDirectory(WrapperFolder);
            Browser.CoreWebView2!.SetVirtualHostNameToFolderMapping(
                VirtualHost, WrapperFolder, CoreWebView2HostResourceAccessKind.Allow);
            Log($"virtual host mapping set: {VirtualHost} -> {WrapperFolder}");

            // Only ever reached once per instance, since Browser.CoreWebView2 is non-null on
            // every call after this one — the "ended" message the wrapper HTML's onStateChange
            // posts (see PlayAsync) is how VideoEnded gets raised.
            Browser.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                var message = args.TryGetWebMessageAsString();
                Log($"WebMessageReceived: {message}");
                if (message == "ended")
                {
                    VideoEnded?.Invoke();
                }
            };

            return true;
        }
        catch (Exception ex)
        {
            Log($"WebView2 initialization failed: {ex}");
            return false;
        }
    }

    private void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(DebugLogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} [{_instanceId}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Best-effort diagnostic logging; nothing actionable if this fails.
        }
    }
}
