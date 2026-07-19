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
; (https://ndi.video) under its own license. We don't bundle or silently install it ourselves;
; this just offers to open NDI's own official redistributable download in the browser, and is
; skipped entirely if a compatible NDI Runtime is already detected on this machine.
Filename: "http://ndi.link/NDIRedistV6"; Description: "Download and install the free NDI Runtime (needed for the Network Feed / EasyWorship live-feed option)"; Flags: postinstall shellexec skipifsilent unchecked; Check: NdiRuntimeNotInstalled

[Code]
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
