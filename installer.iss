; Скрипт создания универсального установщика ShiftRoster для Windows x86 и x64 (Inno Setup)
#define MyAppName "ShiftRoster"
#define MyAppUserTitle "График смен"
#ifndef MyAppVersion
#define MyAppVersion "1.0.2"
#endif
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
; Для 64-битных систем (x64)
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.pdb,*.log,Backups,Exports"; Check: Is64BitInstallMode
; Для 32-битных систем (x86)
Source: "publish\win-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.pdb,*.log,Backups,Exports"; Check: not Is64BitInstallMode

[Icons]
Name: "{group}\{#MyAppUserTitle}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppUserTitle}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppUserTitle}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppUserTitle}}"; Flags: nowait postinstall skipifsilent
