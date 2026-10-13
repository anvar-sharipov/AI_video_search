param(
    [Parameter(Mandatory = $true)] [string] $AppDir
)

# Runs once, elevated, right after Inno Setup copies the payload into $AppDir. It brings up
# PostgreSQL and Elasticsearch as native Windows services, points the backend at them, runs the
# backend once so DbInitializer creates the schema and seeds the bootstrap SuperAdmin, captures
# that one-time password to a file on the Desktop, then registers the backend itself as an
# auto-start Windows Service.
#
# Everything here is offline: both third-party installers ship inside the setup.exe, so the
# target PC never needs internet.
#
# Re-runnable on purpose: each step checks whether it already happened, and the secrets it
# generates are kept in $DataRoot\config\secrets.json so a second run reuses the same PostgreSQL
# password instead of locking itself out of the database the first run created. That makes a
# half-failed install fixable by just running this script again.

$ErrorActionPreference = 'Stop'

# Deliberately not under "Program Files\...": a video archive doesn't belong in Program Files,
# and Elasticsearch's Windows service wrapper (procrun) is unreliable when ES_HOME contains
# spaces - which "C:\Program Files\Turkmentelekom VMS" does.
$DataRoot      = 'C:\ProgramData\VMS'
$ConfigDir     = Join-Path $DataRoot 'config'
$LogDir        = Join-Path $DataRoot 'logs'
$SecretsFile   = Join-Path $ConfigDir 'secrets.json'
$BackendDir    = Join-Path $AppDir 'backend'
$FrontendDir   = Join-Path $AppDir 'frontend'
$DownloadsDir  = Join-Path $AppDir 'downloads'
$PgInstaller   = Join-Path $DownloadsDir 'postgresql-16.4-1-windows-x64.exe'
$EsZip         = Join-Path $DownloadsDir 'elasticsearch-9.0.4-windows-x86_64.zip'
$EsHome        = Join-Path $DataRoot 'elasticsearch'
$PgInstallDir  = 'C:\Program Files\PostgreSQL\16'
$PgServiceName = 'VMS-Postgres'
$EsServiceName = 'elasticsearch-service-x64'
$BackendSvc    = 'VMSBackend'

New-Item -ItemType Directory -Force -Path $DataRoot, $ConfigDir, $LogDir | Out-Null
$LogFile = Join-Path $LogDir 'install.log'

function Log($msg) {
    $line = '[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $msg
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

function New-RandomSecret([int]$Length = 32) {
    # Alphanumeric only: this value travels through a PostgreSQL connection string, a psql -c
    # argument and a JSON file, and every quoting/escaping problem in those three disappears if
    # the secret simply can't contain a quote, backslash or semicolon. Asking for 3x the bytes
    # leaves enough characters after stripping base64's "+/=".
    $buf = New-Object byte[] ($Length * 3)
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buf)
    $s = ([Convert]::ToBase64String($buf) -replace '[^a-zA-Z0-9]', '')
    return $s.Substring(0, $Length)
}

function Test-PortFree([int]$Port) {
    $listening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    return -not $listening
}

function Get-FreePort([int[]]$Candidates) {
    foreach ($p in $Candidates) { if (Test-PortFree $p) { return $p } }
    throw "None of the ports $($Candidates -join ', ') are free on this PC."
}

function Test-TcpListening([int]$Port) {
    # Used as the backend's readiness probe. A plain TCP connect, not an HTTP GET: every
    # /api/... route needs a bearer token, so there is no endpoint that answers 200 here, and
    # Kestrel accepting a connection already proves the migrations ran and the host is serving.
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $client.Connect('127.0.0.1', $Port)
        $connected = $client.Connected
        $client.Close()
        return $connected
    } catch {
        return $false
    }
}

try {

# --- 0. Secrets and ports --------------------------------------------------------------------
# Generated on the first install and reused afterwards, so re-running this script (or a repair
# install over an existing database) keeps working.
if (Test-Path $SecretsFile) {
    Log "Reusing the secrets from a previous install ($SecretsFile)."
    $secrets = Get-Content -Path $SecretsFile -Raw | ConvertFrom-Json
} else {
    Log 'Generating per-install secrets (database password, JWT signing key).'
    $secrets = [pscustomobject]@{
        PgSuperPassword = New-RandomSecret 24
        VmsDbPassword   = New-RandomSecret 24
        JwtSigningKey   = New-RandomSecret 48
        PgPort          = Get-FreePort @(5432, 5434, 5435)
        EsPort          = Get-FreePort @(9200, 9201, 9202)
        BackendPort     = Get-FreePort @(5080, 5081, 5082)
    }
    $secrets | ConvertTo-Json | Set-Content -Path $SecretsFile -Encoding UTF8
    # Only SYSTEM and local admins: this file holds the database password and the JWT signing key.
    # By SID (*S-1-5-18 = SYSTEM, *S-1-5-32-544 = Administrators), not by name: the group names are
    # localised, so "Administrators" silently fails on a Russian or Turkmen Windows.
    & icacls.exe $SecretsFile /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
}
Log ("Ports: PostgreSQL={0}, Elasticsearch={1}, backend API={2}" -f $secrets.PgPort, $secrets.EsPort, $secrets.BackendPort)

$DataDirs = @('Archive', 'Clips', 'LiveHls', 'EMaps', 'Snapshots', 'dp-keys', 'es-data', 'es-logs') |
    ForEach-Object { Join-Path $DataRoot $_ }
New-Item -ItemType Directory -Force -Path $DataDirs | Out-Null

# --- 1. PostgreSQL ---------------------------------------------------------------------------
if (Get-Service -Name $PgServiceName -ErrorAction SilentlyContinue) {
    Log "PostgreSQL service '$PgServiceName' already present - skipping install."
} else {
    if (-not (Test-Path $PgInstaller)) {
        throw "PostgreSQL installer missing: $PgInstaller. This setup.exe was built without it bundled."
    }
    Log 'Installing PostgreSQL 16 (unattended, a few minutes)...'
    # --enable-components rather than --disable-components: naming only what we want keeps this
    # working even if EDB renames the pgAdmin/StackBuilder components in a later 16.x build.
    $pgArgs = @(
        '--mode', 'unattended',
        '--unattendedmodeui', 'minimal',
        '--prefix', $PgInstallDir,
        '--datadir', (Join-Path $PgInstallDir 'data'),
        '--superpassword', $secrets.PgSuperPassword,
        '--servicename', $PgServiceName,
        '--serverport', "$($secrets.PgPort)",
        '--enable-components', 'server,commandlinetools'
    )
    $p = Start-Process -FilePath $PgInstaller -ArgumentList $pgArgs -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        throw "The PostgreSQL installer exited with code $($p.ExitCode). See %TEMP%\install-postgresql.log."
    }
    Log 'PostgreSQL installed.'
}

Log 'Waiting for the PostgreSQL service to report Running...'
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    $svc = Get-Service -Name $PgServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -eq 'Stopped') { Start-Service -Name $PgServiceName -ErrorAction SilentlyContinue }
        if ($svc.Status -eq 'Running') { break }
    }
    Start-Sleep -Seconds 2
}
if ((Get-Service -Name $PgServiceName).Status -ne 'Running') {
    throw "The PostgreSQL service '$PgServiceName' did not start within 120s."
}
Set-Service -Name $PgServiceName -StartupType Automatic

$PsqlExe = Join-Path $PgInstallDir 'bin\psql.exe'
if (-not (Test-Path $PsqlExe)) { throw "psql.exe not found at $PsqlExe." }
$env:PGPASSWORD = $secrets.PgSuperPassword
$PsqlCommon = @('-U', 'postgres', '-h', '127.0.0.1', '-p', "$($secrets.PgPort)", '-w')

# The role and the database are created as two statements checked from here rather than in one
# SQL script: CREATE DATABASE can't run inside a DO block (it isn't transaction-safe), and psql's
# \gexec trick doesn't work through -c, which is what the previous version of this script got
# wrong - it silently created no database at all.
Log "Creating the 'vms' role and database..."
$roleExists = (& $PsqlExe @PsqlCommon -tAc "SELECT 1 FROM pg_roles WHERE rolname = 'vms'") -join ''
# psql is a native exe: a failed connection is a non-zero exit code, not a PowerShell error, so
# without this check the script would carry on and only fail much later, when the backend can't
# reach a database nobody ever created.
if ($LASTEXITCODE -ne 0) {
    throw "Could not connect to PostgreSQL on port $($secrets.PgPort) as 'postgres'. If this PC already had a PostgreSQL service named '$PgServiceName' with a different superuser password, this installer cannot reuse it - see $LogFile."
}
if ($roleExists.Trim() -eq '1') {
    & $PsqlExe @PsqlCommon -c "ALTER ROLE vms LOGIN PASSWORD '$($secrets.VmsDbPassword)'" | ForEach-Object { Log "psql: $_" }
} else {
    & $PsqlExe @PsqlCommon -c "CREATE ROLE vms LOGIN PASSWORD '$($secrets.VmsDbPassword)'" | ForEach-Object { Log "psql: $_" }
}
$dbExists = (& $PsqlExe @PsqlCommon -tAc "SELECT 1 FROM pg_database WHERE datname = 'vms'") -join ''
if ($dbExists.Trim() -ne '1') {
    & $PsqlExe @PsqlCommon -c 'CREATE DATABASE vms OWNER vms' | ForEach-Object { Log "psql: $_" }
    Log "Database 'vms' created."
} else {
    Log "Database 'vms' already exists - keeping it (an upgrade must not wipe the archive index)."
}
$env:PGPASSWORD = $null

# --- 2. Elasticsearch ------------------------------------------------------------------------
# Wrapped in its own try/catch, unlike every other step: Elasticsearch backs metadata search
# only - live view, recording and detection never touch it - so a PC where it won't start should
# still end up with a working VMS and a warning, not a failed install. Everything below sets
# $esUp, and the backend is configured to point at it either way (it retries on its own once the
# service does come up).
$esUp = $false
try {

if (Get-Service -Name $EsServiceName -ErrorAction SilentlyContinue) {
    Log 'Elasticsearch service already present - skipping install.'
} else {
    if (-not (Test-Path $EsZip)) {
        throw "Elasticsearch archive missing: $EsZip. This setup.exe was built without it bundled."
    }
    Log 'Extracting Elasticsearch (~1 GB on disk, a few minutes)...'
    # [IO.Compression.ZipFile] rather than Expand-Archive: on PowerShell 5.1 Expand-Archive takes
    # many minutes on a 500 MB archive because it pushes every entry through the object pipeline.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $staging = Join-Path $DataRoot 'es-staging'
    if (Test-Path $staging) { Remove-Item -Path $staging -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($EsZip, $staging)
    $extracted = Get-ChildItem -Path $staging -Directory | Where-Object { $_.Name -like 'elasticsearch-*' } | Select-Object -First 1
    if (-not $extracted) { throw "Unexpected Elasticsearch archive layout under $staging." }
    if (Test-Path $EsHome) { Remove-Item -Path $EsHome -Recurse -Force }
    Move-Item -Path $extracted.FullName -Destination $EsHome
    Remove-Item -Path $staging -Recurse -Force

    Log 'Configuring Elasticsearch (single node, bound to localhost, security off)...'
    # Single node with security disabled matches this deployment: one PC, the index reachable only
    # from localhost, so TLS and passwords between the backend and its own search index would buy
    # nothing. ingest.geoip.downloader is turned off because it otherwise retries a download from
    # elastic.co forever on a machine with no internet - exactly the machine this installs on.
    # Forward slashes in the paths: ES parses these as Java properties, where a backslash escapes.
    $esYml = Join-Path $EsHome 'config\elasticsearch.yml'
    Add-Content -Path $esYml -Encoding UTF8 -Value @"

# --- added by the Turkmentelekom VMS installer ---
cluster.name: vms-cluster
node.name: vms-node
discovery.type: single-node
network.host: 127.0.0.1
http.port: $($secrets.EsPort)
path.data: $($DataRoot.Replace('\','/'))/es-data
path.logs: $($DataRoot.Replace('\','/'))/es-logs
xpack.security.enabled: false
xpack.security.enrollment.enabled: false
xpack.security.http.ssl.enabled: false
ingest.geoip.downloader.enabled: false
"@
    # Written before the service is installed: the service wrapper bakes the JVM options into its
    # own registry config at install time, so a heap setting added afterwards would be ignored.
    New-Item -ItemType Directory -Force -Path (Join-Path $EsHome 'config\jvm.options.d') | Out-Null
    Set-Content -Path (Join-Path $EsHome 'config\jvm.options.d\vms-heap.options') -Encoding ASCII -Value "-Xms1g`r`n-Xmx1g"

    Log 'Registering Elasticsearch as a Windows service...'
    # Point at the JDK bundled inside the archive, so the target PC needs no Java installed.
    $env:ES_JAVA_HOME = Join-Path $EsHome 'jdk'
    & cmd.exe /c "`"$(Join-Path $EsHome 'bin\elasticsearch-service.bat')`" install" 2>&1 | ForEach-Object { Log "es-service: $_" }
}

# elasticsearch-service.bat installs with startup type Manual, which would leave search broken
# after the first reboot.
Set-Service -Name $EsServiceName -StartupType Automatic
if ((Get-Service -Name $EsServiceName).Status -ne 'Running') {
    Log 'Starting the Elasticsearch service...'
    Start-Service -Name $EsServiceName
}

Log "Waiting for Elasticsearch on http://127.0.0.1:$($secrets.EsPort) ..."
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$($secrets.EsPort)/_cluster/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $esUp = $true; break }
    } catch {
        Start-Sleep -Seconds 3
    }
}
if ($esUp) {
    Log 'Elasticsearch is up.'
} else {
    Log "WARNING: Elasticsearch did not answer within 180s. Live view and recording will work; metadata search won't until the '$EsServiceName' service starts. See $DataRoot\es-logs."
}

} catch {
    Log "WARNING: Elasticsearch setup failed - $($_.Exception.Message)"
    Log "Continuing without it: everything except metadata search will work. Re-run this script once the cause is fixed to finish setting it up."
}

# --- 3. Backend configuration ----------------------------------------------------------------
Log 'Writing backend\appsettings.local.json...'
$appsettings = [ordered]@{
    ConnectionStrings = [ordered]@{
        VmsDatabase = "Host=127.0.0.1;Port=$($secrets.PgPort);Database=vms;Username=vms;Password=$($secrets.VmsDbPassword)"
    }
    Kestrel = [ordered]@{
        Endpoints = [ordered]@{ Http = [ordered]@{ Url = "http://127.0.0.1:$($secrets.BackendPort)" } }
    }
    Vms = [ordered]@{
        ArchiveRootPath          = Join-Path $DataRoot 'Archive'
        ClipsRootPath            = Join-Path $DataRoot 'Clips'
        LiveHlsRootPath          = Join-Path $DataRoot 'LiveHls'
        EMapsRootPath            = Join-Path $DataRoot 'EMaps'
        SnapshotsRootPath        = Join-Path $DataRoot 'Snapshots'
        FfmpegPath               = Join-Path $AppDir 'tools\ffmpeg\ffmpeg.exe'
        FfprobePath              = Join-Path $AppDir 'tools\ffmpeg\ffprobe.exe'
        YoloModelPath            = Join-Path $AppDir 'models\yolov8n.onnx'
        FaceDetectorModelPath    = Join-Path $AppDir 'models\haarcascade_frontalface_default.xml'
        EyeDetectorModelPath     = Join-Path $AppDir 'models\haarcascade_eye.xml'
        FaceEmbedderModelPath    = Join-Path $AppDir 'models\face_recognition_sface_2021dec.onnx'
        TessDataPath             = Join-Path $AppDir 'tessdata'
        DataProtectionKeysPath   = Join-Path $DataRoot 'dp-keys'
        ElasticsearchUri         = "http://127.0.0.1:$($secrets.EsPort)"
        JwtSigningKey            = $secrets.JwtSigningKey
        JwtExpiryMinutes         = 30
    }
}
$appsettings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $BackendDir 'appsettings.local.json') -Encoding UTF8

# The client reads this instead of its compiled-in default, so it still finds the backend when
# port 5080 was already taken on this PC and the API ended up on 5081.
Log 'Writing frontend\vms-client.json...'
[ordered]@{ BackendBaseAddress = "http://127.0.0.1:$($secrets.BackendPort)" } |
    ConvertTo-Json | Set-Content -Path (Join-Path $FrontendDir 'vms-client.json') -Encoding UTF8

# --- 4. First run: create the schema and seed the bootstrap SuperAdmin -----------------------
$exe = Join-Path $BackendDir 'VMS.Backend.Server.exe'
if (-not (Test-Path $exe)) { throw "Backend executable missing: $exe." }

Log 'Starting the backend once to create the database schema and seed the SuperAdmin...'
$outLog = Join-Path $LogDir 'first-run.log'
$errLog = Join-Path $LogDir 'first-run-err.log'
$proc = Start-Process -FilePath $exe -WorkingDirectory $BackendDir `
    -RedirectStandardOutput $outLog -RedirectStandardError $errLog -PassThru -WindowStyle Hidden

# Poll the log instead of sleeping a fixed 20s: applying the migrations and loading the YOLO
# model takes longer than that on a slow disk, and the previous version of this script killed the
# process before the password had been printed.
$pw = $null
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $hit = Select-String -Path $outLog, $errLog -Pattern 'one-time password: (\S+)' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { $pw = $hit.Matches.Groups[1].Value.TrimEnd('.', ','); break }
    # The schema is in place as soon as the API is listening; on a repeat install there is no new
    # password coming, so stop waiting as soon as the port answers.
    if (Test-TcpListening $secrets.BackendPort) { break }
    if ($proc.HasExited) { break }
}
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Start-Sleep -Seconds 2

$credFile = Join-Path $DataRoot 'VMS-superadmin-parol.txt'
if ($pw) {
    @"
=== Turkmentelekom VMS ===

* RU * Первый вход в систему:

  Пользователь: superadmin
  Пароль:       $pw

  Пароль показан только один раз. Смените его сразу после первого входа.
  Адрес сервера: http://127.0.0.1:$($secrets.BackendPort)

* TK * Ulgama ilkinji giris:

  Ulanyjy: superadmin
  Parol:   $pw

  Bu parol dine bir gezek gorkezilyar. Ilkinji girenizden son ony uytgedin.
"@ | Set-Content -Path $credFile -Encoding UTF8
    foreach ($desk in @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:PUBLIC 'Desktop'))) {
        if ($desk -and (Test-Path $desk)) {
            Copy-Item -Path $credFile -Destination (Join-Path $desk 'VMS-superadmin-parol.txt') -Force -ErrorAction SilentlyContinue
        }
    }
    Log "Bootstrap SuperAdmin password written to $credFile (and copied to the Desktop)."
} else {
    Log "No new SuperAdmin password in the first-run log. That is expected when installing over an existing 'vms' database - the password from the original install still applies. Otherwise check $outLog / $errLog."
}

# --- 5. Register the backend as a Windows service --------------------------------------------
$existing = Get-Service -Name $BackendSvc -ErrorAction SilentlyContinue
if ($existing) {
    Log "Service '$BackendSvc' already exists - restarting it on the new build."
    Stop-Service -Name $BackendSvc -Force -ErrorAction SilentlyContinue
} else {
    Log "Registering '$BackendSvc' as an auto-start Windows service..."
    # DependsOn so a reboot starts the database and the search index before the thing that
    # connects to them, instead of leaning on the backend's retry loop.
    $deps = @($PgServiceName)
    if (Get-Service -Name $EsServiceName -ErrorAction SilentlyContinue) { $deps += $EsServiceName }
    New-Service -Name $BackendSvc -BinaryPathName "`"$exe`"" -DisplayName 'Turkmentelekom VMS Backend' `
        -Description 'Turkmentelekom VMS - REST API, camera orchestration, recording and detection.' `
        -StartupType Automatic -DependsOn $deps | Out-Null
    # Restart on crash: one bad camera stream taking the process down shouldn't stop recording
    # until somebody notices. (sc.exe, because New-Service has no equivalent.)
    & sc.exe failure $BackendSvc reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
}
Start-Service -Name $BackendSvc
Log "Service '$BackendSvc' started."

# --- 6. Clean up the bundled third-party installers ------------------------------------------
# ~860 MB that is pure dead weight now that both are installed. Deleted here, at the end of a
# successful run, rather than by Inno's deleteafterinstall flag, so that a run which failed
# halfway still has them on disk and can simply be re-run.
if (Test-Path $DownloadsDir) {
    Log 'Removing the bundled PostgreSQL/Elasticsearch installers (no longer needed, ~860 MB).'
    Remove-Item -Path $DownloadsDir -Recurse -Force -ErrorAction SilentlyContinue
}

Log 'Install finished successfully.'

} catch {
    $msg = "$($_.Exception.Message)"
    Log "ERROR: $msg"
    Log $_.ScriptStackTrace
    # Inno Setup's [Run] step can't surface a failure from here, so say it out loud: failing
    # silently would leave the operator with a Start-menu shortcut and no working server.
    try {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show(
            "Установка серверных компонентов не завершена.`n`n$msg`n`nПодробности: $LogFile`n`nПосле устранения причины запустите заново:`npowershell -ExecutionPolicy Bypass -File `"$AppDir\scripts\install.ps1`" -AppDir `"$AppDir`"",
            'Turkmentelekom VMS', 'OK', 'Error') | Out-Null
    } catch { }
    exit 1
}
