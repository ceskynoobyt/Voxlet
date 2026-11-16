#define MyAppName "Voxlet"
#define MyAppVersion "0.4.12"
#define MyAppPublisher "CeskynoobProductions"

[Setup]
AppId={{6A8C60AE-20B9-4A3F-AC1D-FF5F93E6F95E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Voxlet
DefaultGroupName=Voxlet
UninstallDisplayName=Voxlet
UninstallDisplayIcon={app}\Voxlet.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
WizardResizable=no
SetupIconFile=..\Resources\Voxlet.ico
OutputDir=..\dist
OutputBaseFilename=Voxlet-Setup-0.4.12
Compression=lzma2/fast
SolidCompression=no
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Voxlet"; Filename: "{app}\Voxlet.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Voxlet"; Filename: "{app}\Voxlet.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Voxlet.exe"; Description: "Launch Voxlet"; Flags: postinstall nowait skipifsilent

[Code]
var
  DeleteVoxletData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteVoxletData := MsgBox('Delete all Voxlet data, including transcript history, dictionaries, and shortcuts? Choose No to keep it on this device.', mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES;
end;

procedure DeinitializeUninstall();
begin
  if DeleteVoxletData then
    DelTree(ExpandConstant('{localappdata}\Voxlet'), True, True, True);
end;
