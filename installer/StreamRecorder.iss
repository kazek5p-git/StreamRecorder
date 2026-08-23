; StreamRecorder installer for Inno Setup.

#ifndef Version
  #define Version "1.0.2"
#endif

#ifndef StageDir
  #define StageDir "..\dotnet\target\release-package\StreamRecorder-v" + Version + "-winforms-net48"
#endif

#ifndef OutputDir
  #define OutputDir "..\dotnet\target\release-package"
#endif

#define AppName "StreamRecorder"
#define AppPublisher "StreamRecorder contributors"
#define AppUrl "https://github.com/kazek5p-git/StreamRecorder"

[Setup]
AppId={{8B5C9B34-2B43-4F5D-9F57-2A7F0F16A1C4}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=StreamRecorder-{#Version}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
UninstallDisplayName={#AppName}
Uninstallable=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=StreamRecorder - radio stream recording
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#Version}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\StreamRecorder.exe"; Parameters: "--installed"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\StreamRecorder.exe"; Parameters: "--installed"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\StreamRecorder.exe"; Parameters: "--installed"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\installed.marker"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SaveStringToFile(ExpandConstant('{app}\installed.marker'), 'installed', False);
  end;
end;
