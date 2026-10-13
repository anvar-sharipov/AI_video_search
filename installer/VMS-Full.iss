; Full-system offline installer for the Turkmentelekom VMS: the backend (as a Windows Service)
; + the WPF client + PostgreSQL + Elasticsearch, all installed natively (no Docker) on a single
; Windows PC. Everything it needs is inside the setup.exe - the target PC never needs internet,
; .NET, Java or any dev tooling.
;
; Build it with scripts\build-installer.ps1 from the repo root (it publishes both apps
; self-contained, stages the payload, checks that the two bundled third-party installers are
; present, then calls ISCC) - don't run ISCC by hand, the payload has to be staged first.
;
; What runs at the end of setup, elevated: scripts\install.ps1 installs PostgreSQL and
; Elasticsearch as native Windows services, creates the app database, writes
; backend\appsettings.local.json and frontend\vms-client.json, runs the backend once to seed the
; bootstrap SuperAdmin (whose one-time password it saves to the Desktop), then registers the
; backend as an auto-start service. See that script for the full sequence.

#define MyAppName "Turkmentelekom VMS"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Turkmentelekom"
#define MyClientExeName "VMS.Frontend.WPF.exe"
#define PgInstaller "postgresql-16.4-1-windows-x64.exe"
#define EsArchive "elasticsearch-9.0.4-windows-x86_64.zip"

[Setup]
AppId={{4F1E3C2A-9B4D-4E1A-8B0C-7D5A1F0E9C21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=VMS-Full-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 or newer: the client needs it, and PostgreSQL 16's installer refuses older builds.
MinVersion=10.0
; Installing PostgreSQL/Elasticsearch as services and writing to Program Files both need admin.
PrivilegesRequired=admin
UninstallDisplayIcon={app}\frontend\{#MyClientExeName}
; The payload is well over a gigabyte, so tell the user up front rather than at the copy step.
DiskSpanning=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Excludes: appsettings.local.json is the developer's own machine config - it holds real camera
; credentials, and `dotnet publish` copies it into the output because it matches the SDK's
; default appsettings*.json content glob. install.ps1 writes the installed machine's own copy.
Source: "payload\backend\*"; DestDir: "{app}\backend"; \
    Excludes: "appsettings.local.json,appsettings.Development.json"; \
    Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\frontend\*"; DestDir: "{app}\frontend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\tools\ffmpeg\*"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\models\*"; DestDir: "{app}\models"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\tessdata\*"; DestDir: "{app}\tessdata"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "scripts\install.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "scripts\uninstall.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
; The two third-party installers. Not flagged deleteafterinstall even though they're ~860 MB of
; dead weight afterwards: install.ps1 deletes them itself once it has finished with them, which
; keeps them on disk when it fails halfway and so lets the operator just re-run the script.
Source: "downloads\{#PgInstaller}"; DestDir: "{app}\downloads"; Flags: ignoreversion
Source: "downloads\{#EsArchive}"; DestDir: "{app}\downloads"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\frontend\{#MyClientExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\frontend\{#MyClientExeName}"; Tasks: desktopicon

[Run]
; Not runhidden: this takes several minutes (a PostgreSQL install plus a 1 GB Elasticsearch
; extraction), and a visible console is the only progress the operator gets while it works.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\install.ps1"" -AppDir ""{app}"""; \
    StatusMsg: "{cm:InstallingComponents}"; \
    Flags: waituntilterminated
Filename: "{app}\frontend\{#MyClientExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\uninstall.ps1"" -AppDir ""{app}"""; \
    Flags: waituntilterminated runhidden; RunOnceId: "RemoveVmsServices"

[CustomMessages]
russian.InstallingComponents=Устанавливаются PostgreSQL, Elasticsearch и серверная служба (это займёт несколько минут)...
english.InstallingComponents=Installing PostgreSQL, Elasticsearch and the backend service (this takes several minutes)...
russian.NeedDiskSpace=На диске %1 свободно только %2 ГБ. Для установки нужно не менее %3 ГБ (PostgreSQL, Elasticsearch и видеоархив). Освободите место и запустите установку заново.
english.NeedDiskSpace=Drive %1 has only %2 GB free. The install needs at least %3 GB (PostgreSQL, Elasticsearch and the video archive). Free up space and run setup again.

[Code]
const
  RequiredGb = 12;

function InitializeSetup(): Boolean;
var
  FreeMb, TotalMb: Cardinal;
  SystemDrive: String;
begin
  Result := True;
  // Inno checks space for the files it copies itself, but install.ps1 then installs PostgreSQL
  // (~400 MB) and unpacks Elasticsearch (~1 GB) onto the system drive, and the archive grows
  // there afterwards. Checking up front beats failing halfway through a multi-minute install.
  SystemDrive := ExpandConstant('{sd}\');
  if GetSpaceOnDisk(SystemDrive, True, FreeMb, TotalMb) then
  begin
    if (FreeMb div 1024) < RequiredGb then
    begin
      // The argument array stays on the same line as the call: Inno's parser reads any line whose
      // first non-blank character is '[' as a section header, so wrapping before it fails to compile.
      MsgBox(FmtMessage(CustomMessage('NeedDiskSpace'), [SystemDrive,
        IntToStr(FreeMb div 1024), IntToStr(RequiredGb)]), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // An upgrade over a running install can't overwrite backend\*.dll while the service holds them
  // open, so stop it first. Failures are ignored on purpose - on a first install there is no
  // service yet, and `net stop` returning non-zero for that is expected, not a problem.
  // Both names: 'VMSBackend' is current, 'VMS Backend' is what an earlier build registered.
  Exec(ExpandConstant('{sys}\net.exe'), 'stop VMSBackend', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\net.exe'), 'stop "VMS Backend"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
