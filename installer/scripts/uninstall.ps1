$ErrorActionPreference = "SilentlyContinue"

# Stops and removes the services install.ps1 created. Run from Inno's [UninstallRun] before the
# app's own files are removed. Postgres data (and thus the archive DB) is left in place under
# its install dir unless the operator removes it by hand — losing the video archive index on a
# plain uninstall would be a much worse surprise than a leftover data directory.

Stop-Service -Name "VMS Backend" -Force
& sc.exe delete "VMS Backend"

$EsHome = Get-ChildItem -Path (Join-Path $PSScriptRoot "..\elasticsearch") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "elasticsearch-*" } | Select-Object -First 1
if ($EsHome) {
    & (Join-Path $EsHome.FullName "bin\elasticsearch-service.bat") stop
    & (Join-Path $EsHome.FullName "bin\elasticsearch-service.bat") remove
}

Stop-Service -Name "VMS-Postgres" -Force
$pgUninstaller = "C:\Program Files\PostgreSQL\16\uninstall-postgresql.exe"
if (Test-Path $pgUninstaller) {
    Start-Process -FilePath $pgUninstaller -ArgumentList "--mode unattended" -Wait
}
