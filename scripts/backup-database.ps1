<#
.SYNOPSIS
    Dumps the PostgreSQL database (users, RBAC, audit log, camera configs, known persons,
    camera groups, video wall layouts, refresh tokens - everything except the Elasticsearch
    search index and the archive/clips/snapshots files on disk) to a timestamped, plain-SQL
    backup file, via `docker exec` into the running vms-postgres container.

    This is the one piece of this MVP's "known limitations" list that's actually just a missing
    script, not a missing feature - intended to run on a schedule (Windows Task Scheduler /
    `schtasks`) in any real deployment. See the README's Operations section for the restore
    command and for why Elasticsearch isn't included here.

.PARAMETER BackupDir
    Where to write the .sql file. Defaults to backups/ at the repo root.

.PARAMETER KeepLast
    How many most-recent backups to keep in BackupDir - older ones are deleted. Defaults to 14
    (two weeks of daily backups). Pass 0 to disable pruning.
#>
param(
    [string]$BackupDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "backups"),
    [int]$KeepLast = 14
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$outFile = Join-Path $BackupDir "vms_$timestamp.sql"

Write-Host "Dumping vms-postgres database 'vms' to $outFile ..."
docker exec vms-postgres pg_dump -U vms -d vms --format=plain | Out-File -FilePath $outFile -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    throw "pg_dump failed (exit code $LASTEXITCODE) - is the vms-postgres container running? (docker compose up -d)"
}

$sizeKb = [math]::Round((Get-Item $outFile).Length / 1KB, 1)
Write-Host "Backup written: $outFile ($sizeKb KB)"

if ($KeepLast -gt 0) {
    $old = Get-ChildItem -Path $BackupDir -Filter "vms_*.sql" | Sort-Object LastWriteTime -Descending | Select-Object -Skip $KeepLast
    foreach ($file in $old) {
        Write-Host "Pruning old backup: $($file.Name)"
        Remove-Item $file.FullName -Force
    }
}
