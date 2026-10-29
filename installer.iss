; Скрипт создания установщика ShiftRoster для Windows 7, 8, 10, 11 (Inno Setup)
#define MyAppName "ShiftRoster"
#define MyAppUserTitle "График смен"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ShiftRoster Development"
#define MyAppExeName "ShiftRoster.exe"

[Setup]
AppId={{5F8A2D34-71B3-4A1C-9E45-8B23C9D38FA0}
AppName={#MyAppUserTitle}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppUserTitle}
AllowNoIcons=yes
OutputDir=publish\installer
OutputBaseFilename=ShiftRoster-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppUserTitle}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppUserTitle}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppUserTitle}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppUserTitle}}"; Flags: nowait postinstall skipifsilent
