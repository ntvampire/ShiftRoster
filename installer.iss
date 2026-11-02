; Скрипт создания универсального установщика ShiftRoster для Windows x86 и x64 (Inno Setup)
#define MyAppName "ShiftRoster"
#define MyAppUserTitle "График смен"
#ifndef MyAppVersion
#define MyAppVersion "1.0.3"
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
DisableDirPage=no
DisableProgramGroupPage=yes
AlwaysShowDirOnReadyPage=yes
OutputDir=publish\installer
OutputBaseFilename=ShiftRoster-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=Assets\app.ico
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallDisplayName={#MyAppUserTitle} ({#MyAppName})
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
russian.CreateStartMenuIcon=Создать ярлык в меню «Пуск»
russian.DeleteDataPrompt=Удалить также базу данных и сохраненные данные программы?

[Tasks]
Name: "startmenuicon"; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Для 64-битных систем (x64)
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.pdb,*.log,Backups,Exports"; Check: Is64BitInstallMode
; Для 32-битных систем (x86)
Source: "publish\win-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.pdb,*.log,Backups,Exports"; Check: not Is64BitInstallMode

[Icons]
Name: "{group}\{#MyAppUserTitle}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppUserTitle}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppUserTitle}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppUserTitle}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteUserData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteUserData := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    if not UninstallSilent then
    begin
      if MsgBox(CustomMessage('DeleteDataPrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DeleteUserData := True;
    end;
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    if DeleteUserData then
    begin
      DelTree(ExpandConstant('{app}\Backups'), True, True, True);
      DelTree(ExpandConstant('{app}\Exports'), True, True, True);
      DeleteFile(ExpandConstant('{app}\ShiftRoster.db'));
      DeleteFile(ExpandConstant('{app}\ShiftRoster.db-wal'));
      DeleteFile(ExpandConstant('{app}\ShiftRoster.db-shm'));
      DelTree(ExpandConstant('{localappdata}\ShiftRoster'), True, True, True);
      RemoveDir(ExpandConstant('{app}'));
    end;
  end;
end;
