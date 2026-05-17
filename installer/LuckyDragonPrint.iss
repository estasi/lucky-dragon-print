; Inno Setup 6 script — Lucky Dragon Print installer
; Compile:  ISCC.exe installer\LuckyDragonPrint.iss
; Output:   installer\Output\LuckyDragonPrint-Setup-1.0.0.exe

#define MyAppName        "Lucky Dragon Print"
#define MyAppShortName   "LDPrint"
#define MyAppVersion     "1.0.0"
#define MyAppPublisher   "Lucky Dragon"
#define MyAppURL         "https://flow.lucky-dragon.ru/"
#define MyAppExeName     "LDPrint.exe"

[Setup]
; AppId is the GUID that identifies this product across versions for the
; Windows installer. Generated once — DO NOT regenerate for new versions
; or the new install will register as a separate product.
AppId={{6F4D8E22-3B71-4F35-9D55-2CFA0E80B1AA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} setup
VersionInfoProductName={#MyAppName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=Output
OutputBaseFilename=LuckyDragonPrint-Setup-{#MyAppVersion}
SetupIconFile=..\LdPrint\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associatefiles"; Description: "Сделать Lucky Dragon Print программой по умолчанию для .tspl"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "..\publish\LDPrint.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; File association: register .tspl with the installed exe. Only written if
; the user ticks the "associatefiles" task; per-user HKCU rule lives inside
; the app itself (the gear button toggles it). The installer writes
; system-wide HKLM so it survives across user accounts.
[Registry]
Root: HKLM; Subkey: "Software\Classes\.tspl"; ValueType: string; ValueName: ""; ValueData: "LuckyDragonPrint.TsplFile"; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKLM; Subkey: "Software\Classes\LuckyDragonPrint.TsplFile"; ValueType: string; ValueName: ""; ValueData: "TSPL Label File"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKLM; Subkey: "Software\Classes\LuckyDragonPrint.TsplFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: associatefiles
Root: HKLM; Subkey: "Software\Classes\LuckyDragonPrint.TsplFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associatefiles

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
