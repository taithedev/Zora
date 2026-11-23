#ifndef MyAppVersion
  #define MyAppVersion "0.7.0"
#endif

[Setup]
AppId=Zora.taithedev.Desktop
AppName=Zora
AppVerName=Zora {#MyAppVersion}
AppVersion={#MyAppVersion}
AppPublisher=Zuzify
AppPublisherURL=https://github.com/taithedev/Zora
AppSupportURL=https://github.com/taithedev/Zora/issues
AppUpdatesURL=https://github.com/taithedev/Zora/releases/latest
DefaultDirName={localappdata}\Programs\Zora
DefaultGroupName=Zora
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\installer-out
OutputBaseFilename=Zora-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
UninstallDisplayIcon={app}\Zora.exe
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installed.marker"; DestDir: "{app}"; DestName: ".zora-installed"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Icons]
Name: "{userprograms}\Zora"; Filename: "{app}\Zora.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Zora"; Filename: "{app}\Zora.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Zora.exe"; Description: "Launch Zora now"; Flags: nowait postinstall skipifsilent
