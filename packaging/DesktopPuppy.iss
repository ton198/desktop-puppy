#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\installer\publish"
#endif

[Setup]
AppId={{2F732845-74FD-4DDE-9E28-9A5E3246D174}
AppName=Desktop Puppy
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\DesktopPuppy
DefaultGroupName=Desktop Puppy
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
OutputDir=..\artifacts\installer
OutputBaseFilename=DesktopPuppy-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\DesktopPuppy\Pet\Tray\Assets\DesktopPuppyTray.ico
UninstallDisplayIcon={app}\DesktopPuppy.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\DesktopPuppy\Pet\Tray\Assets\DesktopPuppyTray.ico"; DestDir: "{app}"; DestName: "DesktopPuppy.ico"; Flags: ignoreversion
Source: "..\docs\mvp-usage-and-privacy.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Desktop Puppy"; Filename: "{app}\DesktopPuppy.exe"; IconFilename: "{app}\DesktopPuppy.ico"
Name: "{userdesktop}\Desktop Puppy"; Filename: "{app}\DesktopPuppy.exe"; IconFilename: "{app}\DesktopPuppy.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\DesktopPuppy.exe"; Description: "Launch Desktop Puppy"; Flags: nowait postinstall skipifsilent
