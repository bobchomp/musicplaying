# Music Display

A small Windows app that shows a fullscreen "now playing" screen on whichever monitor you pick:
album cover, song title, and artist — nothing else. Background color shifts to match the album
art. Opening the app shows a control panel where you choose the monitor and show/hide the
display; it can also live in the system tray and start with Windows.

## How it detects what's playing

It reads Windows' built-in **System Media Transport Controls** (the same source as the media
flyout on the taskbar), so it works automatically with Spotify, browsers (YouTube, etc.),
Windows Media Player, VLC, iTunes, and anything else that reports "now playing" info to Windows —
no per-app setup. Requires Windows 10 version 1809 (build 17763) or later.

## Getting the app

Grab `MusicDisplaySetup.exe` from the repo's [Releases](../../releases) page and run it. No admin
rights needed.

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
5. Click **Preview** to open what the display would show in a plain, resizable window — handy for
   checking a layout without putting the real fullscreen display up. It stays live, updating as
   the track, layout, or blank state changes, until you close it.
6. Click **Show Display** to put up the fullscreen now-playing screen. Press **Ctrl+Q** or click
   **Hide Display** to take it down.
7. Once the display is up, **Blank Screen** temporarily hides the album art and text (just the
   background color stays) without closing the display window — click **Unblank** to bring them
   back.
8. Check **Start with Windows** to have it launch automatically (minimized to the tray) at login,
   restoring whatever show/hide state it was last in.
9. Closing the control panel window (the X button) asks whether to minimize it to the tray or
   exit the program completely. **File > Exit** in the menu bar exits immediately without asking.
10. **Previous / Play / Next** send those commands to whichever app is currently playing (via the
    same Windows media session used to read the track info), and the volume slider/mute button
    control the **system's master output volume** — the same one as the Windows volume flyout,
    not a per-app volume.

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

The installer is written to `installer\output\MusicDisplaySetup.exe`.

## CI/CD

**`.github/workflows/release.yml`** — on pushing a tag like `v1.0.0`, or via the Actions tab's
"Run workflow" button with a version number, this publishes the app, compiles the Inno Setup
installer, and attaches `MusicDisplaySetup.exe` to a new GitHub Release.

## Project layout

```
src/MusicDisplay/
  App.xaml(.cs)              application entry point, single-instance guard, shared styles
  ControlPanelWindow.xaml(.cs) monitor picker, show/hide, tray icon, autostart toggle, menu
  CloseConfirmationWindow.xaml(.cs) minimize-to-tray vs exit prompt shown on window close
  DisplayWindow.xaml(.cs)      the fullscreen now-playing screen (hosts NowPlayingView)
  PreviewWindow.xaml(.cs)      windowed preview of the same content (hosts NowPlayingView)
  NowPlayingView.xaml(.cs)     shared album art / title / artist visual, equalizer bars, clock
  Services/
    NowPlayingService.cs      reads title/artist/artwork via Windows SMTC
    ColorExtractor.cs         picks a background accent color from the album art
    AutostartService.cs       HKCU Run key registration
    AppSettings.cs            settings persisted to %AppData%\MusicDisplay\settings.json
    DisplayLayout.cs          Centered / Left / Right layout enum
    SystemVolumeService.cs    master volume/mute via NAudio's Core Audio API wrapper
  Resources/Fonts/            embedded Poppins font files (OFL licensed, see OFL.txt)
installer/installer.iss       Inno Setup installer script
```
