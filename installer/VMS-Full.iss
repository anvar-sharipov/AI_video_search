; Full-system installer for the Turkmentelekom VMS: backend (as a Windows Service) + WPF client
; + PostgreSQL + Elasticsearch, all installed natively (no Docker) on a single Windows machine.
;
; Build order:
;   1. dotnet publish ..\VMS.Backend.Server  -c Release -r win-x64 --self-contained true -o payload\backend
;   2. dotnet publish ..\VMS.Frontend.WPF    -c Release -r win-x64 --self-contained true -o payload\frontend
;   3. Copy tools/ffmpeg, models, tessdata into payload\ (see project CLAUDE.md / scripts/setup-tools.ps1)
;   4. Drop postgresql-16.4-1-windows-x64.exe and elasticsearch-9.0.4-windows-x86_64.zip into downloads\
;   5. "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" VMS-Full.iss
;
; What install.ps1 (run automatically at the end of setup, elevated) does: installs PostgreSQL
; and Elasticsearch as native Windows services, creates the app database, writes
; backend\appsettings.local.json, runs the backend once to seed the bootstrap SuperAdmin
; password (written to the Desktop), then registers the backend as a persistent service.
; See installer\scripts\install.ps1 for the full sequence and installer\README (Turkmen PDF)
; for the operator-facing walkthrough.

#define MyAppName "Turkmentelekom VMS"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Turkmentelekom"
#define MyClientExeName "VMS.Frontend.WPF.exe"

[Setup]
AppId={{4F1E3C2A-9B4D-4E1A-8B0C-VMSFULL0001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=VMS-Full-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Installing PostgreSQL/Elasticsearch as services and writing to Program Files requires admin.
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "payload\backend\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\frontend\*"; DestDir: "{app}\frontend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\tools\ffmpeg\*"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\models\*"; DestDir: "{app}\models"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\tessdata\*"; DestDir: "{app}\tessdata"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "downloads\postgresql-16.4-1-windows-x64.exe"; DestDir: "{app}\downloads"; Flags: ignoreversion
Source: "downloads\elasticsearch-9.0.4-windows-x86_64.zip"; DestDir: "{app}\downloads"; Flags: ignoreversion
Source: "scripts\install.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\uninstall.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\frontend\{#MyClientExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\frontend\{#MyClientExeName}"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\install.ps1"" -AppDir ""{app}"""; \
    StatusMsg: "PostgreSQL, Elasticsearch we backend gurnalýar (birnäçe minut alyp biler)..."; \
    Flags: waituntilterminated
Filename: "{app}\frontend\{#MyClientExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\uninstall.ps1"""; \
    Flags: waituntilterminated runhidden; RunOnceId: "RemoveVmsServices"
