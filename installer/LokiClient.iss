#define MyAppName "Loki Proxy VPN"
#define MyAppVersion "0.1.63"
#define MyAppPublisher "Loki"
#define MyAppExeName "Client.App.Win.exe"

[Setup]
AppId={{7B0F5C0F-7A29-4BDF-B1AD-1F2B1C9D6E40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Loki Proxy VPN
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\.docs\LICENSE.txt
InfoBeforeFile=..\.docs\PRIVACY.md
OutputDir=..\artifacts\installer
OutputBaseFilename=LokiClientSetup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
WizardStyle=modern
SetupIconFile=..\src\Client.App.Win\Assets\Icons\app.ico
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\Assets\Icons\app.ico
UsePreviousAppDir=no
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Запускать Loki Proxy VPN вместе с Windows"; GroupDescription: "Параметры запуска:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\.docs\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\.docs\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\.docs\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{autodesktop}\Loki*.lnk"
Type: files; Name: "{userprograms}\Loki*.lnk"
Type: files; Name: "{group}\*.lnk"
Type: files; Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Loki*.lnk"

[UninstallDelete]
Type: files; Name: "{autodesktop}\Loki*.lnk"
Type: files; Name: "{userprograms}\Loki*.lnk"
Type: files; Name: "{group}\*.lnk"
Type: files; Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Loki*.lnk"
Type: filesandordirs; Name: "{localappdata}\LokiClient"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{group}\Loki"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{group}\Loki VPN"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{group}\Loki Proxy"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{userprograms}\Loki"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{userprograms}\Loki VPN"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{userprograms}\Loki Proxy"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Comment: "Loki VPN proxy client"; AppUserModelID: "Loki.Proxy.VPN"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"; Tasks: desktopicon; AppUserModelID: "Loki.Proxy.VPN"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\loki.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\loki.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\loki-proxy.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\loki-proxy.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\loki-vpn.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\loki-vpn.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LokiClient"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{sys}\ie4uinit.exe"; Parameters: "-show"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent

[Code]
procedure ForceCloseRunningClient();
var
  ResultCode: Integer;
begin
  if WizardSilent then
  begin
    Exec(ExpandConstant('{cmd}'), '/c taskkill /IM "{#MyAppExeName}" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

function InitializeSetup(): Boolean;
begin
  ForceCloseRunningClient();
  Result := True;
end;
