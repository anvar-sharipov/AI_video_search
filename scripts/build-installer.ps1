<#
.SYNOPSIS
    Builds dist\VMS-Full-Setup.exe - the single offline installer for the whole VMS.

.DESCRIPTION
    Stages everything installer\VMS-Full.iss expects, then compiles it:

      1. publishes VMS.Backend.Server and VMS.Frontend.WPF self-contained for win-x64, so the
         target PC needs no .NET runtime installed;
      2. strips appsettings.local.json out of the published backend - `dotnet publish` copies it
         in because it matches the SDK's default appsettings*.json glob, and on a developer
         machine that file holds real camera credentials;
      3. copies ffmpeg, the ONNX/Haar models and tessdata into the payload;
      4. checks that the bundled PostgreSQL installer and Elasticsearch archive are in
         installer\downloads (they are too big to commit - see -ShowDownloadUrls);
      5. runs Inno Setup's ISCC.

    Takes roughly 10-25 minutes, almost all of it Inno Setup compressing ~1.9 GB of payload.

.PARAMETER SkipPublish
    Reuse the existing installer\payload instead of republishing. Only for iterating on the
    .iss or the install scripts - it will happily package a stale build.

.PARAMETER ShowDownloadUrls
    Print the two third-party download URLs and exit, without building anything.
#>
[CmdletBinding()]
param(
    [switch] $SkipPublish,
    [switch] $ShowDownloadUrls
)

$ErrorActionPreference = 'Stop'

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$InstallerDir = Join-Path $RepoRoot 'installer'
$PayloadDir  = Join-Path $InstallerDir 'payload'
$DownloadDir = Join-Path $InstallerDir 'downloads'
$DistDir     = Join-Path $RepoRoot 'dist'

$PgInstaller = 'postgresql-16.4-1-windows-x64.exe'
$EsArchive   = 'elasticsearch-9.0.4-windows-x86_64.zip'
$PgUrl       = "https://get.enterprisedb.com/postgresql/$PgInstaller"
$EsUrl       = "https://artifacts.elastic.co/downloads/elasticsearch/$EsArchive"
# Guards against packaging a half-finished download: a truncated PostgreSQL installer or ES zip
# would compile into a setup.exe that only fails once it is run on the target PC. Exact byte
# counts, not a rough floor - both URLs are pinned to one published version, so the size is
# fixed, and an exact check catches a truncation of any size. (Written as plain integers because
# PowerShell's MB suffix means mebibytes while the servers report decimal megabytes - mixing the
# two is what made the first version of this check reject a complete download.)
$PgBytes = 374221136
$EsBytes = 489515629

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

if ($ShowDownloadUrls) {
    Write-Host "Download these two into $DownloadDir before building:`n"
    Write-Host "  $PgUrl"
    Write-Host "  $EsUrl`n"
    Write-Host 'Both support resuming, which matters on a slow link:'
    Write-Host "  curl -L -C - --retry 30 --retry-all-errors -o `"$DownloadDir\$PgInstaller`" `"$PgUrl`""
    Write-Host "  curl -L -C - --retry 30 --retry-all-errors -o `"$DownloadDir\$EsArchive`" `"$EsUrl`""
    exit 0
}

# --- Prerequisites ---------------------------------------------------------------------------
Step 'Checking prerequisites'

$Iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) {
    throw 'Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php (ISCC.exe is its command-line compiler).'
}
Write-Host "  Inno Setup: $Iscc"

$FfmpegExe = Join-Path $RepoRoot 'tools\ffmpeg\ffmpeg.exe'
if (-not (Test-Path $FfmpegExe)) {
    throw "ffmpeg not found at $FfmpegExe. Run scripts\setup-tools.ps1 first."
}

$RequiredModels = @(
    'yolov8n.onnx',
    'haarcascade_frontalface_default.xml',
    'haarcascade_eye.xml',
    'face_recognition_sface_2021dec.onnx'
)
foreach ($m in $RequiredModels) {
    $p = Join-Path $RepoRoot "models\$m"
    if (-not (Test-Path $p)) { throw "Model missing: $p. Run scripts\setup-tools.ps1 first." }
}

$TessData = Join-Path $RepoRoot 'tessdata\eng.traineddata'
if (-not (Test-Path $TessData)) { throw "Tesseract data missing: $TessData. Run scripts\setup-tools.ps1 first." }

foreach ($d in @(@{ Name = $PgInstaller; Bytes = $PgBytes; Url = $PgUrl },
                 @{ Name = $EsArchive;   Bytes = $EsBytes; Url = $EsUrl })) {
    $p = Join-Path $DownloadDir $d.Name
    if (-not (Test-Path $p)) {
        throw "Missing bundled installer: $p`n  Download it from $($d.Url) (see -ShowDownloadUrls)."
    }
    $size = (Get-Item $p).Length
    if ($size -ne $d.Bytes) {
        throw ("$($d.Name) is {0:N0} bytes, expected {1:N0}. The download is incomplete or is a different build - resume it with curl -C - (or delete and refetch) before building." -f $size, $d.Bytes)
    }
    Write-Host ("  {0}: {1:N0} bytes - OK" -f $d.Name, $size)
}

# --- Publish ---------------------------------------------------------------------------------
if ($SkipPublish) {
    Step 'Skipping publish (-SkipPublish) - packaging whatever is already in installer\payload'
    if (-not (Test-Path (Join-Path $PayloadDir 'backend\VMS.Backend.Server.exe'))) {
        throw "installer\payload has no published backend, so there is nothing to package. Run without -SkipPublish."
    }
} else {
    Step 'Publishing the backend and the client (self-contained win-x64)'
    if (Test-Path $PayloadDir) { Remove-Item -Path $PayloadDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $PayloadDir | Out-Null

    foreach ($proj in @(
        @{ Path = 'VMS.Backend.Server\VMS.Backend.Server.csproj'; Out = 'backend' },
        @{ Path = 'VMS.Frontend.WPF\VMS.Frontend.WPF.csproj';     Out = 'frontend' }
    )) {
        Write-Host "  dotnet publish $($proj.Path) -> payload\$($proj.Out)"
        & dotnet publish (Join-Path $RepoRoot $proj.Path) `
            -c Release -r win-x64 --self-contained true `
            -o (Join-Path $PayloadDir $proj.Out) --nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($proj.Path)." }
    }

    # Belt and braces: the .iss also excludes these, but a developer's appsettings.local.json
    # holds a real camera's IP, username and password, and it must not leave this machine inside
    # a setup.exe. install.ps1 writes the installed machine's own copy from scratch.
    Step 'Removing developer-only config from the published backend'
    foreach ($leak in @('appsettings.local.json', 'appsettings.Development.json')) {
        $p = Join-Path $PayloadDir "backend\$leak"
        if (Test-Path $p) {
            Remove-Item -Path $p -Force
            Write-Host "  removed payload\backend\$leak"
        }
    }
}

# --- Stage ffmpeg, models, tessdata ----------------------------------------------------------
Step 'Staging ffmpeg, models and tessdata'
New-Item -ItemType Directory -Force -Path `
    (Join-Path $PayloadDir 'tools\ffmpeg'), `
    (Join-Path $PayloadDir 'models'), `
    (Join-Path $PayloadDir 'tessdata') | Out-Null

Copy-Item -Path (Join-Path $RepoRoot 'tools\ffmpeg\*.exe') -Destination (Join-Path $PayloadDir 'tools\ffmpeg') -Force
foreach ($m in $RequiredModels) {
    Copy-Item -Path (Join-Path $RepoRoot "models\$m") -Destination (Join-Path $PayloadDir 'models') -Force
}
# Only the four models the backend actually loads: models\ also holds yolov8n.pt, the PyTorch
# source the ONNX export came from, which nothing at runtime reads.
Copy-Item -Path (Join-Path $RepoRoot 'tessdata\*.traineddata') -Destination (Join-Path $PayloadDir 'tessdata') -Force

$payloadMb = ((Get-ChildItem -Path $PayloadDir -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1MB
Write-Host ("  payload: {0:N0} MB" -f $payloadMb)

# --- Compile ---------------------------------------------------------------------------------
Step 'Compiling the installer with Inno Setup (slow - it is compressing ~1.9 GB)'
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
$iss = Join-Path $InstallerDir 'VMS-Full.iss'
& $Iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

$setup = Join-Path $DistDir 'VMS-Full-Setup.exe'
if (-not (Test-Path $setup)) { throw "ISCC reported success but $setup is missing." }

Step 'Done'
Write-Host ("  {0}  ({1:N0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
Write-Host '  Copy that one file to the target PC and run it as administrator. It needs no internet.'
