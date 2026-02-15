#define MyAppName "YASN"
#define MyAppVersion "0.2.2"
#define MyAppPublisher "Yasniy"
#define MyAppURL "https://github.com/maxbaydi/yasniy"
#define MyAppExeName "yasn.exe"
#define BinDir "{app}\bin"
#define PackagesDir "{app}\packages"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Yasn
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=Yasn-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64 arm64
ArchitecturesInstallIn64BitMode=x64 arm64
CloseApplications=force
CloseApplicationsFilter=yasn.exe

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.AddToPath=Добавить в PATH (рекомендуется)
russian.AdditionalOptions=Дополнительные параметры
english.AddToPath=Add to PATH (recommended)
english.AdditionalOptions=Additional options

[Tasks]
Name: "addpath"; Description: "{cm:AddToPath}"; GroupDescription: "{cm:AdditionalOptions}"; Flags: checkedonce

[Files]
Source: "staging\bin\yasn.exe"; DestDir: "{#BinDir}"; Flags: ignoreversion
Source: "staging\packages\ui-sdk\*"; DestDir: "{#PackagesDir}\ui-sdk"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "staging\packages\ui-kit\*"; DestDir: "{#PackagesDir}\ui-kit"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\YASN CLI"; Filename: "{#BinDir}\{#MyAppExeName}"; Parameters: "--help"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UserEnvironmentKey = 'Environment';

function PathNeedsAdd(Param: string): Boolean;
var
  OrigPath: string;
  RootKey: Integer;
  SubKey: string;
  BinPath: string;
begin
  BinPath := ExpandConstant('{#BinDir}');
  if IsAdminInstallMode then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    SubKey := EnvironmentKey;
  end
  else
  begin
    RootKey := HKEY_CURRENT_USER;
    SubKey := UserEnvironmentKey;
  end;

  if not RegQueryStringValue(RootKey, SubKey, 'Path', OrigPath) then
    Result := True
  else
    Result := Pos(';' + BinPath + ';', ';' + OrigPath + ';') = 0;
end;

procedure EnvAddPath(Path: string);
var
  Paths: string;
  RootKey: Integer;
  SubKey: string;
begin
  if IsAdminInstallMode then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    SubKey := EnvironmentKey;
  end
  else
  begin
    RootKey := HKEY_CURRENT_USER;
    SubKey := UserEnvironmentKey;
  end;

  if not RegQueryStringValue(RootKey, SubKey, 'Path', Paths) then
    Paths := '';

  if Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    Exit;

  Paths := Paths + ';' + Path;
  RegWriteStringValue(RootKey, SubKey, 'Path', Paths);
end;

procedure EnvRemovePath(Path: string);
var
  Paths: string;
  RootKey: Integer;
  SubKey: string;
  P: Integer;
  Part: string;
  NewPaths: string;
begin
  if IsAdminInstallMode then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    SubKey := EnvironmentKey;
  end
  else
  begin
    RootKey := HKEY_CURRENT_USER;
    SubKey := UserEnvironmentKey;
  end;

  if not RegQueryStringValue(RootKey, SubKey, 'Path', Paths) then
    Exit;

  NewPaths := '';
  Paths := Paths + ';';
  P := Pos(';', Paths);
  while P > 0 do
  begin
    Part := Copy(Paths, 1, P - 1);
    Delete(Paths, 1, P);
    if (Part <> '') and (Uppercase(Part) <> Uppercase(Path)) then
    begin
      if NewPaths <> '' then
        NewPaths := NewPaths + ';';
      NewPaths := NewPaths + Part;
    end;
    P := Pos(';', Paths);
  end;

  RegWriteExpandStringValue(RootKey, SubKey, 'Path', NewPaths);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if WizardIsTaskSelected('addpath') and PathNeedsAdd('') then
      EnvAddPath(ExpandConstant('{#BinDir}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    EnvRemovePath(ExpandConstant('{#BinDir}'));
end;

[UninstallDelete]
Type: dirifempty; Name: "{#BinDir}"
Type: dirifempty; Name: "{#PackagesDir}\ui-sdk"
Type: dirifempty; Name: "{#PackagesDir}\ui-kit"
Type: dirifempty; Name: "{#PackagesDir}"
Type: dirifempty; Name: "{app}"
