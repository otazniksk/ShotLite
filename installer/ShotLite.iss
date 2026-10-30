; Inno Setup script for ShotLite. Built by .github/workflows/build.yml:
;   ISCC.exe /DVersion=1.2.0 /DNumericVersion=1.2.0 /DSourceDir=..\out\app installer\ShotLite.iss
; Output: out\ShotLite-install.exe (fixed name, so releases/latest/download/ShotLite-install.exe always works).

#ifndef Version
  #define Version "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\app"
#endif

#define AppName "ShotLite"
#define AppExe "ShotLite.exe"
#define RepoUrl "https://github.com/otazniksk/ShotLite"

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstaller.
AppId={{AF23D553-9105-488B-AC09-5588769C2D56}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher=Michal Škop
AppCopyright=Copyright © 2026 Michal Škop · MIT License
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#NumericVersion}
; Per-user install into %LOCALAPPDATA%\Programs\ShotLite: no admin rights, no UAC prompt.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\src\ShotLite\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; A running ShotLite holds this mutex (Program.cs); setup and uninstall ask to close it first.
AppMutex=ShotLite.SingleInstance
CloseApplications=yes
OutputDir=..\out
OutputBaseFilename=ShotLite-install
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
; Same languages as the app, where Inno Setup ships a translation; the others fall back to English.
#define Lang(Name, File) FileExists(CompilerPath + "Languages\" + File) ? \
  'Name: "' + Name + '"; MessagesFile: "compiler:Languages\' + File + '"' : ""
#emit Lang("sk", "Slovak.isl")
#emit Lang("cs", "Czech.isl")
#emit Lang("de", "German.isl")
#emit Lang("fr", "French.isl")
#emit Lang("es", "Spanish.isl")
#emit Lang("it", "Italian.isl")
#emit Lang("ptbr", "BrazilianPortuguese.isl")
#emit Lang("nl", "Dutch.isl")
#emit Lang("pl", "Polish.isl")
#emit Lang("hu", "Hungarian.isl")
#emit Lang("uk", "Ukrainian.isl")
#emit Lang("ja", "Japanese.isl")
#emit Lang("ko", "Korean.isl")
#emit Lang("zh", "ChineseSimplified.isl")

[Tasks]
Name: "autostart"; Description: "{cm:AutoStartProgram,{#AppName}}"; GroupDescription: "{cm:AutoStartProgramGroupDescription}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same value the app writes from its settings (Autostart.cs); the app reads it back on start.
; An update keeps it as it is: autostart may have been switched off in the app since the install.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Check: not IsAppUpdate; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; An update from inside the app (/relaunch=1) is silent, so the entry above is skipped.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsAppUpdate

[Code]
function IsAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

function InitializeSetup: Boolean;
var
  Waited: Integer;
begin
  { The app quits right after starting setup; without the wait the AppMutex check could still see it running. }
  Waited := 0;
  while IsAppUpdate and CheckForMutexes('ShotLite.SingleInstance') and (Waited < 15000) do
  begin
    Sleep(200);
    Waited := Waited + 200;
  end;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Autostart may have been switched on later in the app's settings, not by the installer task. }
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
end;
