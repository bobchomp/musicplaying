# Music Display

A small Windows app that shows a fullscreen "now playing" screen on whichever monitor you pick:
album cover, song title, and artist — nothing else. Background color shifts to match the album
art. Opening the app shows a control panel — with a live preview of the display right next to
it — where you choose the monitor and show/hide the display; it can also live in the system tray
and start with Windows.

## How it detects what's playing

It reads Windows' built-in **System Media Transport Controls** (the same source as the media
flyout on the taskbar), so it works automatically with Spotify, browsers (YouTube, etc.),
Windows Media Player, VLC, iTunes, and anything else that reports "now playing" info to Windows —
no per-app setup. Requires Windows 10 version 1809 (build 17763) or later.

## Getting the app

Grab `MusicDisplaySetup-<version>.exe` from the repo's [Releases](../../releases) page and run it.
No admin rights needed.

## Using it

1. Launch `MusicDisplay.exe`. The control panel opens.
2. Pick which monitor the display should appear on.
3. Pick a **layout**: Centered (album art above the title/artist, all centered), or Album left /
   Album right (the art hugs that edge of the screen with the title and artist text stacked next
   to it, aligned to the same edge). Album left/right also fills the empty space on the opposite
   side with an animated equalizer-bar visualization while something's playing. Switching layouts
   while the display is up fades smoothly to the new arrangement instead of snapping.
4. Check **Show time and date** to add a live clock stacked below the equalizer bars — only
   available with the Album left/right layouts, since Centered has no dedicated empty side for it.
5. Check **Show synced lyrics** to display the lyrics, karaoke-style, synced to playback position
   — looked up automatically from [LRCLIB](https://lrclib.net) by title/artist. On the Album
   left/right layouts it's a 3-line ticker in the side space above the equalizer bars (the line
   just sung on top, the current line in the middle in white, the next line below), scrolling up
   one line at a time as the song progresses; Centered layout, which has no side space for that,
   shows just the current line near the bottom of the screen instead. Requires an internet
   connection; only shows
   anything when LRCLIB has *synced* (time-stamped) lyrics for that specific track — there's no
   fallback to a static wall of text. The control panel's status line under the checkbox says
   whether lyrics were found for the current track. **You're responsible for making sure you have
   the right to publicly display lyrics for whatever you're playing** (e.g. a CCLI license) —
   this app only fetches and shows them, it doesn't handle licensing for you.
6. The panel on the right shows a live preview of exactly what the display would show — no
   separate window to open, it's always there, updating as the track, layout, or blank state
   changes, so you can check a layout before putting the real fullscreen display up.
7. Click **Show Display** to put up the fullscreen now-playing screen. Press **Ctrl+Q** or click
   **Hide Display** to take it down.
8. Once the display is up, **Blank Screen** temporarily hides the album art and text (just the
   background color stays) without closing the display window — click **Unblank** to bring them
   back.
9. Check **Start with Windows** to have it launch automatically (minimized to the tray) at login,
   restoring whatever show/hide state it was last in.
10. Closing the control panel window (the X button) asks whether to minimize it to the tray or
    exit the program completely. **File > Exit** in the menu bar exits immediately without asking.
11. **Previous / Play / Next** send those commands to whichever app is currently playing (via the
    same Windows media session used to read the track info), and the volume slider/mute button
    control the **system's master output volume** — the same one as the Windows volume flyout,
    not a per-app volume.
12. Set a **Stream name** and check **Broadcast over the network (NDI)** to publish the
    now-playing screen as an NDI source on your LAN — independent of Show Display, so it works
    whether or not the fullscreen display is up on this machine. Changing the name while
    broadcasting restarts the feed under the new name. See below for what's needed on the
    receiving computer.

The menu bar also has **View** (Show/Hide Display, Blank Screen — the same actions as the buttons
below), **Settings > Open Settings Folder** (jumps straight to where `settings.json` lives), and
**Help > About Music Display** (version number, a GitHub link, and the third-party license
disclosures also listed below).

### Sending the feed to EasyWorship (or other NDI-aware software)

The Network Feed broadcasts an [NDI](https://ndi.video) source named
`<this computer's name> (<your stream name>)` — "Music Display" by default. To pick it up
elsewhere on the network:

- **EasyWorship with native NDI input**: add it directly as an NDI source if your version
  supports that.
- **Otherwise**, install [NDI Tools](https://ndi.video/tools/) on the *receiving* computer and
  use its **NDI Virtual Input** utility to assign your stream's source to one of its virtual
  webcam slots — it'll then show up in EasyWorship's Feed Editor as an
  "NDI Webcam Video N (DirectShow)" input device, just like a real webcam.

This requires the free **NDI Runtime** on the *sending* computer (this one) for the toggle to
work at all. The installer offers to open NDI's official download page for it (skipped
automatically if a compatible NDI Runtime is already detected) — see
[Third-party software](#third-party-software) below. If the Network Feed checkbox is greyed out,
the NDI Runtime isn't installed.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build src\MusicDisplay\MusicDisplay.csproj -c Release
```

To produce a single portable `.exe` (self-contained, no .NET install required to run it):

```powershell
dotnet publish src\MusicDisplay\MusicDisplay.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The published exe is at `publish\MusicDisplay.exe`.

### Building the installer locally

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). After publishing (above):

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\installer.iss
```

The installer is written to `installer\output\MusicDisplaySetup-<version>.exe` (version `1.0.0`
by default when built without `/DMyAppVersion=...`; see below).

## CI/CD

**`.github/workflows/release.yml`** — on pushing a tag like `v1.0.0`, or via the Actions tab's
"Run workflow" button with a version number, this publishes the app, compiles the Inno Setup
installer, and attaches `MusicDisplaySetup-<version>.exe` to a new GitHub Release.

## Third-party software

The Network Feed feature talks to the **NDI Runtime**, a separate free product from
[NDI/Vizrt](https://ndi.video) under its own license — this repo doesn't bundle or redistribute
it. The installer only offers a link to NDI's own official download
([ndi.link/NDIRedistV6](http://ndi.link/NDIRedistV6)); Music Display never installs it silently
or without your say-so. See NDI's [SDK licensing terms](https://docs.ndi.video/all/developing-with-ndi/sdk/licensing)
for details.

The Show synced lyrics feature looks up lyrics from **[LRCLIB](https://lrclib.net)**, a free
community lyrics database, over the internet at runtime — no lyrics are bundled with this app or
this repo. As noted above, displaying lyrics publicly is your responsibility to license (e.g. via
CCLI), not something this app manages.

## License

MIT — see [LICENSE](LICENSE). This covers this repo's own source code; the third-party
components listed above (NDI Runtime, LRCLIB, the bundled Poppins font) remain under their own
licenses.

## Project layout

```
src/MusicDisplay/
  App.xaml(.cs)              application entry point, single-instance guard, shared styles
  ControlPanelWindow.xaml(.cs) monitor picker, show/hide, tray icon, autostart toggle, menu,
                               and an embedded live preview (hosts a NowPlayingView)
  CloseConfirmationWindow.xaml(.cs) minimize-to-tray vs exit prompt shown on window close
  DisplayWindow.xaml(.cs)      the fullscreen now-playing screen (hosts NowPlayingView)
  NowPlayingView.xaml(.cs)     shared album art / title / artist visual, equalizer bars, clock
  Services/
    NowPlayingService.cs      reads title/artist/artwork via Windows SMTC
    ColorExtractor.cs         picks a background accent color from the album art
    AutostartService.cs       HKCU Run key registration
    AppSettings.cs            settings persisted to %AppData%\MusicDisplay\settings.json
    DisplayLayout.cs          Centered / Left / Right layout enum
    SystemVolumeService.cs    master volume/mute via NAudio's Core Audio API wrapper
    NdiInterop.cs             P/Invoke surface for the NDI SDK
    NdiOutputService.cs       captures an off-screen NowPlayingView and sends it as NDI
    LyricsService.cs          fetches + parses synced lyrics from LRCLIB
    LyricsLine.cs             one timestamped line of synced lyrics
    PlaybackPosition.cs       playback-position snapshot used to sync lyrics to position
  Resources/Fonts/            embedded Poppins font files (OFL licensed, see OFL.txt)
installer/installer.iss       Inno Setup installer script
```
