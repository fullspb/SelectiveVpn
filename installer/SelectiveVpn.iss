#define MyAppName "SelectiveVpn"
#define MyAppVersion "0.1.5"
#define MyAppExeName "SelectiveVpn.exe"
#define MyAppId "{{8F4C2A91-6B37-4E5D-9C18-2A7D5E0B4F63}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}
AppComments=Выборочный VPN: выбранные приложения через Mihomo и AmneziaWG
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=SelectiveVpn-Setup-{#MyAppVersion}
SetupIconFile=..\SelectiveVpn\Assets\SelectiveVpn.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
DisableWelcomePage=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SelectiveVpn"; ValueType: none; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\SelectiveVpn"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить SelectiveVpn"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--exit"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "StopSelectiveVpn"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
