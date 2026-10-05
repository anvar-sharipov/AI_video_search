; Inno Setup script for the VMS.Frontend.WPF client — packages the self-contained
; `dotnet publish` output (publish\, built with -r win-x64 --self-contained true) into
; one Setup.exe so the app can be installed on a PC with no .NET runtime and no dev tools.
; Build: dotnet publish -c Release -r win-x64 --self-contained true -o publish
;        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\VMS.iss

#define MyAppName "Turkmentelekom VMS"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Turkmentelekom"
#define MyAppExeName "VMS.Frontend.WPF.exe"

[Setup]
AppId={{B6C1B6B0-2B0B-4E3B-9B7B-VMS-FRONTEND-01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=VMS-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
