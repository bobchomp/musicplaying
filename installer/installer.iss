; Inno Setup script for Music Display.
; Built by .github/workflows/release.yml against the output of `dotnet publish` (see that
; workflow for the exact publish command). Expects a self-contained single-file publish at
; ..\publish relative to this script, and the app icon at ..\src\MusicDisplay\Assets\icon.ico.

#define MyAppName "Music Display"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppExeName "MusicDisplay.exe"
#define MyAppPublisher "Music Display"

[Setup]
AppId={{9F2C7E9B-6C39-4C7A-9E7E-6E9B7D6E5A21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=MusicDisplaySetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\src\MusicDisplay\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
PrivilegesRequired=lowest
; Same GUID-style name as App.xaml.cs's SingleInstanceMutexName. Safety net for the in-app
; updater (which downloads this installer, launches it, and then closes the app itself before
; Setup gets to its file-copy step): if that self-close is ever slow, still running, or the
; installer is instead run by hand while the app happens to be open, Setup detects the held
; mutex and prompts to close it, rather than failing to overwrite the locked running exe.
AppMutex=MusicDisplay-SingleInstance-3F2A9E9E-9F5B-4E2A-8E3C-5B2E7C1A2B44

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
; The Network Feed (NDI) option needs the free NDI Runtime, a separate product from NDI/Vizrt
; (https://ndi.video) under its own license — we don't bundle it, but we do offer to fetch and run
; NDI's own official redistributable installer for the user (see CurStepChanged below), rather
; than making them do that by hand. Skipped entirely if a compatible NDI Runtime is already
; detected on this machine. If the download itself fails (no internet, blocked, etc.), the
; fallback entry below opens the same official link in a browser instead, so there's still a way
; to get it.
Filename: "{tmp}\NDIRedistV6-Setup.exe"; Description: "Install the free NDI Runtime (needed for the Network Feed / EasyWorship live-feed option)"; Flags: postinstall skipifsilent unchecked; Check: NdiRedistReadyToRun
Filename: "http://ndi.link/NDIRedistV6"; Description: "Download and install the free NDI Runtime (needed for the Network Feed / EasyWorship live-feed option)"; Flags: postinstall shellexec skipifsilent unchecked; Check: NdiRedistFallbackNeeded

[Code]
var
  NdiRedistDownloaded: Boolean;

const
  NdiRedistUrl = 'http://ndi.link/NDIRedistV6';
  NdiRedistLocalName = 'NDIRedistV6-Setup.exe';

// Plain WinINet/URLMON API, available on every Windows install — deliberately not a third-party
// Inno Setup plugin (e.g. Inno Download Plugin), so this script has no extra binary dependency to
// fetch/verify during CI. Follows redirects itself, which matters since ndi.link/NDIRedistV6 is
// a redirect to NDI's actual current download URL, not a direct file link.
function URLDownloadToFile(pCaller: Integer; szURL: string; szFileName: string; dwReserved: Integer; lpfnCB: Integer): Integer;
  external 'URLDownloadToFileW@urlmon.dll stdcall';

function IsNdiRuntimeInstalled(): Boolean;
begin
  Result :=
    (GetEnv('NDI_RUNTIME_DIR_V6') <> '') or
    (GetEnv('NDI_RUNTIME_DIR_V5') <> '') or
    (GetEnv('NDI_RUNTIME_DIR_V4') <> '');
end;

function NdiRuntimeNotInstalled(): Boolean;
begin
  Result := not IsNdiRuntimeInstalled();
end;

function NdiRedistReadyToRun(): Boolean;
begin
  Result := NdiRuntimeNotInstalled() and NdiRedistDownloaded;
end;

function NdiRedistFallbackNeeded(): Boolean;
begin
  Result := NdiRuntimeNotInstalled() and not NdiRedistDownloaded;
end;

// Fetches NDI's official redistributable installer into this run's temp folder (which Inno Setup
// cleans up on its own once setup exits, same as it does for every other {tmp} file) so the [Run]
// entry above can launch it directly — instead of just opening the URL in a browser and leaving
// the user to find and run the download themselves. Only attempted when NDI isn't already present,
// so nobody who already has it pays for an unnecessary download. Best-effort: NdiRedistDownloaded
// stays false on any failure, which is what routes to the plain-browser-link fallback [Run] entry
// instead of a broken/missing local file.
procedure CurStepChanged(CurStep: TSetupStep);
var
  LocalPath: string;
  DownloadResult: Integer;
begin
  if (CurStep = ssPostInstall) and NdiRuntimeNotInstalled() then
  begin
    WizardForm.StatusLabel.Caption := 'Downloading the NDI Runtime installer...';
    LocalPath := ExpandConstant('{tmp}\' + NdiRedistLocalName);
    try
      DownloadResult := URLDownloadToFile(0, NdiRedistUrl, LocalPath, 0, 0);
      NdiRedistDownloaded := (DownloadResult = 0) and FileExists(LocalPath);
    except
      NdiRedistDownloaded := False;
    end;
  end;
end;
