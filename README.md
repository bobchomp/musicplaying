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

- **Installer**: grab `MusicDisplaySetup.exe` from the repo's [Releases](../../releases) page and
  run it. No admin rights needed.
- **Portable build**: every push builds a self-contained `MusicDisplay.exe` — download it from
  the `MusicDisplay-win-x64` artifact on the [Build workflow](../../actions/workflows/build.yml)
  runs. It's a single file with no installer and no separate .NET install required.

## Using it

1. Launch `MusicDisplay.exe`. The control panel opens.
2. Pick which monitor the display should appear on.
3. Click **Show Display** to put up the fullscreen now-playing screen. Press **Esc** or click
   **Hide Display** to take it down.
4. Check **Start with Windows** to have it launch automatically (minimized to the tray) at login,
   restoring whatever show/hide state it was last in.
5. Closing the control panel window minimizes it to the tray rather than quitting — right-click
   the tray icon to fully exit.

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

- **`.github/workflows/build.yml`** — builds and publishes the app on every push/PR, uploading
  the portable exe as a workflow artifact. This is also how a Linux/CI environment validates the
  code compiles, since this project can only be built and run on Windows.
- **`.github/workflows/release.yml`** — on pushing a tag like `v1.0.0` (or via the "Run workflow"
  button with a version number), publishes the app, compiles the Inno Setup installer, and
  attaches `MusicDisplaySetup.exe` to a new GitHub Release.

## Project layout

```
src/MusicDisplay/
  App.xaml(.cs)              application entry point, single-instance guard
  ControlPanelWindow.xaml(.cs) monitor picker, show/hide, tray icon, autostart toggle
  DisplayWindow.xaml(.cs)      the fullscreen now-playing screen
  Services/
    NowPlayingService.cs      reads title/artist/artwork via Windows SMTC
    ColorExtractor.cs         picks a background accent color from the album art
    AutostartService.cs       HKCU Run key registration
    AppSettings.cs            settings persisted to %AppData%\MusicDisplay\settings.json
  Resources/Fonts/            embedded Poppins font files (OFL licensed, see OFL.txt)
installer/installer.iss       Inno Setup installer script
```
