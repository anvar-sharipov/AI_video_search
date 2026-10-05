param(
    [Parameter(Mandatory = $true)] [string] $AppDir
)

# Runs once, elevated, right after Inno Setup copies files into $AppDir. Brings up Postgres and
# Elasticsearch as native Windows services, points the backend at them, runs it once to let
# DbInitializer create the schema + seed the bootstrap SuperAdmin (see VMS.Backend.Server/
# Startup/DbInitializer.cs), captures that one-time password to a file the operator can find,
# then installs the backend itself as a persistent Windows Service.
#
# Idempotent-ish: safe to re-run (checks for already-installed services before reinstalling),
# so a failed/partial install can just be re-run after fixing whatever broke.

$ErrorActionPreference = "Stop"
$LogFile = Join-Path $AppDir "install-log.txt"
function Log($msg) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $msg
    Write-Host $line
    Add-Content -Path $LogFile -Value $line
}

function New-RandomSecret([int]$Bytes = 32) {
    $buf = New-Object byte[] $Bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buf)
    return ([Convert]::ToBase64String($buf) -replace '[^a-zA-Z0-9]', '').Substring(0, $Bytes)
}

$BackendDir = Join-Path $AppDir "backend"
$DataDir = Join-Path $AppDir "Data"
$DownloadsDir = Join-Path $AppDir "downloads"
$PgInstaller = Join-Path $DownloadsDir "postgresql-16.4-1-windows-x64.exe"
$EsZip = Join-Path $DownloadsDir "elasticsearch-9.0.4-windows-x86_64.zip"
$EsInstallRoot = Join-Path $AppDir "elasticsearch"
$PgInstallDir = "C:\Program Files\PostgreSQL\16"
$PgServiceName = "VMS-Postgres"
$PgPort = 5432
$PgPassword = New-RandomSecret -Bytes 24
$JwtKey = New-RandomSecret -Bytes 48

New-Item -ItemType Directory -Force -Path $DataDir, "$DataDir\Archive", "$DataDir\Clips", "$DataDir\LiveHls", "$DataDir\EMaps", "$DataDir\Snapshots" | Out-Null

# --- 1. PostgreSQL --------------------------------------------------------------------------
if (Get-Service -Name $PgServiceName -ErrorAction SilentlyContinue) {
    Log "PostgreSQL service '$PgServiceName' already present, skipping install."
} else {
    if (-not (Test-Path $PgInstaller)) {
        throw "PostgreSQL installer not found at $PgInstaller — the setup.exe was built without it bundled."
    }
    Log "Installing PostgreSQL 16 (unattended)..."
    $pgArgs = @(
        "--mode", "unattended",
        "--unattendedmodeui", "minimal",
        "--prefix", "`"$PgInstallDir`"",
        "--datadir", "`"$PgInstallDir\data`"",
        "--superpassword", $PgPassword,
        "--servicename", $PgServiceName,
        "--serverport", "$PgPort",
        "--disable-components", "stackbuilder,pgAdmin4"
    )
    $p = Start-Process -FilePath $PgInstaller -ArgumentList $pgArgs -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        throw "PostgreSQL installer exited with code $($p.ExitCode). Check %TEMP%\postgresql*\install*.log for details."
    }
    Log "PostgreSQL installed."
}

Log "Waiting for PostgreSQL service to be running..."
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    $svc = Get-Service -Name $PgServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq "Running") { break }
    Start-Sleep -Seconds 2
}
if ((Get-Service -Name $PgServiceName).Status -ne "Running") {
    throw "PostgreSQL service '$PgServiceName' did not reach Running state within 60s."
}

$PsqlExe = Join-Path $PgInstallDir "bin\psql.exe"
$env:PGPASSWORD = $PgPassword
Log "Creating 'vms' role and database..."
& $PsqlExe -U postgres -h localhost -p $PgPort -v ON_ERROR_STOP=0 -c "DO `$`$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'vms') THEN CREATE ROLE vms LOGIN PASSWORD '$PgPassword'; END IF; END `$`$;" 2>&1 | ForEach-Object { Log "psql: $_" }
& $PsqlExe -U postgres -h localhost -p $PgPort -v ON_ERROR_STOP=0 -c "SELECT 'CREATE DATABASE vms OWNER vms' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'vms')\gexec" 2>&1 | ForEach-Object { Log "psql: $_" }

# --- 2. Elasticsearch ------------------------------------------------------------------------
$EsServiceName = "elasticsearch-service-x64"
if (Get-Service -Name $EsServiceName -ErrorAction SilentlyContinue) {
    Log "Elasticsearch service already present, skipping install."
} else {
    if (-not (Test-Path $EsZip)) {
        throw "Elasticsearch archive not found at $EsZip — the setup.exe was built without it bundled."
    }
    Log "Extracting Elasticsearch..."
    Expand-Archive -Path $EsZip -DestinationPath $EsInstallRoot -Force
    $EsHome = Get-ChildItem -Path $EsInstallRoot -Directory | Where-Object { $_.Name -like "elasticsearch-*" } | Select-Object -First 1
    $EsHome = $EsHome.FullName

    Log "Configuring Elasticsearch (single-node, security disabled to match this deployment's trusted-LAN model)..."
    Add-Content -Path (Join-Path $EsHome "config\elasticsearch.yml") -Value @"

discovery.type: single-node
xpack.security.enabled: false
xpack.security.http.ssl.enabled: false
"@
    New-Item -ItemType Directory -Force -Path (Join-Path $EsHome "config\jvm.options.d") | Out-Null
    Set-Content -Path (Join-Path $EsHome "config\jvm.options.d\vms-heap.options") -Value "-Xms512m`n-Xmx512m"

    Log "Installing Elasticsearch as a Windows service..."
    & (Join-Path $EsHome "bin\elasticsearch-service.bat") install
    & (Join-Path $EsHome "bin\elasticsearch-service.bat") start
}

Log "Waiting for Elasticsearch to answer on http://localhost:9200 ..."
$deadline = (Get-Date).AddSeconds(120)
$esUp = $false
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:9200/_cluster/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $esUp = $true; break }
    } catch { Start-Sleep -Seconds 3 }
}
if (-not $esUp) {
    Log "WARNING: Elasticsearch did not respond within 120s — continuing anyway; check the 'elasticsearch-service-x64' service manually."
}

# --- 3. Backend configuration ------------------------------------------------------------------
Log "Writing backend appsettings.local.json..."
$appsettings = @{
    ConnectionStrings = @{
        VmsDatabase = "Host=localhost;Port=$PgPort;Database=vms;Username=vms;Password=$PgPassword"
    }
    Kestrel = @{
        Endpoints = @{ Http = @{ Url = "http://localhost:5080" } }
    }
    Vms = @{
        ArchiveRootPath        = "$DataDir\Archive"
        ClipsRootPath          = "$DataDir\Clips"
        LiveHlsRootPath        = "$DataDir\LiveHls"
        EMapsRootPath          = "$DataDir\EMaps"
        SnapshotsRootPath      = "$DataDir\Snapshots"
        FfmpegPath              = "$AppDir\tools\ffmpeg\ffmpeg.exe"
        FfprobePath             = "$AppDir\tools\ffmpeg\ffprobe.exe"
        YoloModelPath           = "$AppDir\models\yolov8n.onnx"
        FaceDetectorModelPath   = "$AppDir\models\haarcascade_frontalface_default.xml"
        EyeDetectorModelPath    = "$AppDir\models\haarcascade_eye.xml"
        FaceEmbedderModelPath   = "$AppDir\models\face_recognition_sface_2021dec.onnx"
        TessDataPath            = "$AppDir\tessdata"
        ElasticsearchUri        = "http://localhost:9200"
        JwtSigningKey           = $JwtKey
        JwtExpiryMinutes        = 480
    }
} | ConvertTo-Json -Depth 6
Set-Content -Path (Join-Path $BackendDir "appsettings.local.json") -Value $appsettings -Encoding UTF8

# --- 4. First run: let DbInitializer create the schema + seed SuperAdmin, capture the password ---
Log "Starting backend once to initialize the database and seed the bootstrap SuperAdmin..."
$exe = Join-Path $BackendDir "VMS.Backend.Server.exe"
$outLog = Join-Path $AppDir "first-run.log"
$errLog = Join-Path $AppDir "first-run-err.log"
$proc = Start-Process -FilePath $exe -WorkingDirectory $BackendDir `
    -RedirectStandardOutput $outLog -RedirectStandardError $errLog -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 20
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }

$passwordLine = Select-String -Path $outLog, $errLog -Pattern "one-time password: (\S+)" -ErrorAction SilentlyContinue | Select-Object -First 1
$credFile = Join-Path ([Environment]::GetFolderPath("Desktop")) "VMS-superadmin-parol.txt"
if ($passwordLine -and $passwordLine.Matches.Groups.Count -ge 2) {
    $pw = $passwordLine.Matches.Groups[1].Value
    @"
Ulgama ilkinji giri$([char]0x015f) u$([char]0x00e7)in:
  Ulanyjy ady (Username): superadmin
  Parol (Password):       $pw

Bu parol diňe bir gezek görkezilýär — ony häzir ýazyp alyň we ilkinji giren$([char]0x00f6)i$([char]0x00f1)izden so$([char]0x0148) üýtgedi$([char]0x0148).
"@ | Set-Content -Path $credFile -Encoding UTF8
    Log "Bootstrap SuperAdmin password captured -> $credFile"
} else {
    Log "WARNING: could not find the bootstrap SuperAdmin password in first-run logs — check $outLog / $errLog manually, or see the 'Application' Windows Event Log (source: VMS.Backend.Server) once the service is running."
}

# --- 5. Install backend as a persistent Windows Service ---------------------------------------
$ServiceName = "VMS Backend"
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Log "Service '$ServiceName' already exists, restarting it with the current build..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
} else {
    Log "Registering '$ServiceName' as a Windows Service (auto-start)..."
    & sc.exe create "$ServiceName" binPath= "`"$exe`"" start= auto DisplayName= "Turkmentelekom VMS Backend" | ForEach-Object { Log "sc: $_" }
    & sc.exe description "$ServiceName" "Turkmentelekom VMS — REST API + camera orchestration" | Out-Null
}
Start-Service -Name $ServiceName
Log "Backend service started."

Log "Install script finished."
