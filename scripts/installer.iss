#ifndef AppVersion
#define AppVersion "1.0.0"
#endif
[Setup]
AppId=VigiaLocal.Monitoramento
AppName=Vigia Local
AppVersion={#AppVersion}
AppPublisher=Vigia Local
DefaultDirName={localappdata}\Programs\Vigia Local
DefaultGroupName=Vigia Local
UninstallDisplayIcon={app}\VigiaLocal.exe
OutputDir=..\dist
OutputBaseFilename=VigiaLocal-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Vigia Local"; Filename: "{app}\VigiaLocal.exe"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "VigiaLocal"; Flags: uninsdeletevalue
[Run]
Filename: "{app}\VigiaLocal.exe"; Description: "Abrir Vigia Local"; Flags: nowait postinstall skipifsilent
Filename: "{app}\VigiaLocal.exe"; Flags: nowait skipifnotsilent
