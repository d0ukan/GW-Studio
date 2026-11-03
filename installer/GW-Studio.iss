; Inno Setup 6 script for GW Studio.
; Build:  iscc /DMyAppVersion=1.1.1 /DPublishDir=..\publish installer\GW-Studio.iss
; Output: installer\Output\GW-Studio-Setup-v<version>.exe

#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define MyAppName "GW Studio"
#define MyAppExe "GW Studio.exe"

[Setup]
AppId={{99C9B44E-A11A-4C06-B034-19042E024375}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Dou
AppPublisherURL=https://github.com/d0ukan/GW-Studio
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExe}
SetupIconFile=..\Assets\AppIcon.ico
OutputDir=Output
OutputBaseFilename=GW-Studio-Setup-v{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; The Portable marker file must never end up in an installed copy.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "portable.txt,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
