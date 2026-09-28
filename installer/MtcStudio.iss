; Inno Setup script for MTC Studio (Windows MIDI Time Code generator / reader) + the mtc CLI.
; Built by publish-studio.ps1, which passes:
;   /DAppVersion=1.0.0 /DRid=win-x64 /DSourceDir=<repo>\artifacts\studio\win-x64 /DOutDir=<repo>\artifacts
; Manual build (after publish-studio.ps1 -NoInno):
;   ISCC.exe installer\MtcStudio.iss /DAppVersion=1.0.0 /DRid=win-x64
;
; Installs per user by default (no admin, %LOCALAPPDATA%\Programs\MTC Studio); the first page
; offers "install for all users" (Program Files, needs admin).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Rid
  #define Rid "win-x64"
#endif
#ifndef SourceDir
  #define SourceDir AddBackslash(SourcePath) + "..\artifacts\studio\" + Rid
#endif
#ifndef OutDir
  #define OutDir AddBackslash(SourcePath) + "..\artifacts"
#endif

#define AppName "MTC Studio"
#define AppExe  "MtcStudio.exe"

[Setup]
AppId={{B7E2C94A-5F31-4D6E-A8C0-3E9D1F42B6A7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Evan Minton
AppPublisherURL=https://github.com/evanminton/MIDITimecode-dotnet
AppSupportURL=https://github.com/evanminton/MIDITimecode-dotnet
AppComments=MIDI Time Code generator and reader for Windows MIDI ports, and the mtc command-line utility
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesEnvironment=yes
CloseApplications=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\app\{#AppExe}
OutputDir={#OutDir}
OutputBaseFilename=MtcStudio-{#AppVersion}-{#Rid}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.17763
#if Rid == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Rid == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add the mtc command-line utility to PATH"; GroupDescription: "Command line:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\cli\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\app\{#AppExe}"; WorkingDir: "{app}\app"; Comment: "MIDI Time Code generator and reader"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\app\{#AppExe}"; WorkingDir: "{app}\app"; Tasks: desktopicon

[Run]
Filename: "{app}\app\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
const
  UserEnvKey   = 'Environment';
  SystemEnvKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

function EnvRoot: Integer;
begin
  if IsAdminInstallMode then Result := HKEY_LOCAL_MACHINE else Result := HKEY_CURRENT_USER;
end;

function EnvKey: string;
begin
  if IsAdminInstallMode then Result := SystemEnvKey else Result := UserEnvKey;
end;

function CliDir: string;
begin
  Result := ExpandConstant('{app}\cli');
end;

function PathHas(const Path, Dir: string): Boolean;
begin
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') > 0;
end;

procedure AddCliToPath;
var
  Path: string;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', Path) then Path := '';
  if PathHas(Path, CliDir) then Exit;
  if (Path <> '') and (Copy(Path, Length(Path), 1) <> ';') then Path := Path + ';';
  RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', Path + CliDir);
end;

procedure RemoveCliFromPath;
var
  Path: string;
  P: Integer;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', Path) then Exit;
  Path := ';' + Path + ';';
  P := Pos(';' + Uppercase(CliDir) + ';', Uppercase(Path));
  if P = 0 then Exit;
  Delete(Path, P, Length(CliDir) + 1);
  Path := Copy(Path, 2, Length(Path) - 2);
  RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', Path);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then AddCliToPath;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemoveCliFromPath;
end;
