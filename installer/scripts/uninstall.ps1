param(
    [string] $AppDir = ''
)

# Runs from Inno Setup's [UninstallRun], before the app's own files are deleted.
#
# Deliberately non-destructive about data. The backend service is removed outright (its exe is
# about to be deleted anyway), but PostgreSQL and Elasticsearch are only stopped and switched to
# a manual start - their files, the 'vms' database and everything under C:\ProgramData\VMS are
# left alone. Losing the video archive and its index to a plain uninstall would be a far worse
# surprise than a leftover service, and keeping them means a reinstall picks the archive back up
# (install.ps1 reuses config\secrets.json, so the passwords still match the existing database).
#
# To remove the data too, delete C:\ProgramData\VMS and uninstall PostgreSQL 16 from
# "Apps & features" by hand - see the note this script leaves on the Desktop.

$ErrorActionPreference = 'SilentlyContinue'

$DataRoot = 'C:\ProgramData\VMS'
$LogFile  = Join-Path $DataRoot 'logs\uninstall.log'

function Log($msg) {
    $line = '[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $msg
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

# Both names: 'VMSBackend' is what the current installer registers, 'VMS Backend' is what an
# earlier build used, and an upgrade-then-uninstall could leave either one behind.
foreach ($name in @('VMSBackend', 'VMS Backend')) {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Log "Stopping and removing the service '$name'..."
        Stop-Service -Name $name -Force
        & sc.exe delete "$name" | Out-Null
    }
}

foreach ($name in @('elasticsearch-service-x64', 'VMS-Postgres')) {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Log "Stopping '$name' and setting it to a manual start (files and data kept)."
        Stop-Service -Name $name -Force
        Set-Service -Name $name -StartupType Manual
    }
}

$note = Join-Path $DataRoot 'UDALENIE-OSTALNOGO.txt'
@"
=== Turkmentelekom VMS - что осталось на этом ПК ===

Программа удалена, но данные СОХРАНЕНЫ намеренно - чтобы повторная установка
подхватила существующий архив, а не начала с нуля.

Осталось на диске:
  1. $DataRoot
     Видеоархив, клипы, снимки, индекс Elasticsearch, пароли установки (config\secrets.json).
  2. PostgreSQL 16 (C:\Program Files\PostgreSQL\16) и база данных 'vms'.
     Служба VMS-Postgres остановлена и переведена в ручной запуск.
  3. Elasticsearch ($DataRoot\elasticsearch).
     Служба elasticsearch-service-x64 остановлена и переведена в ручной запуск.

Если нужно удалить ВСЁ (данные восстановить будет нельзя):
  1. Удалите "PostgreSQL 16" через "Параметры -> Приложения".
  2. Удалите папку $DataRoot
     (сначала в services.msc удалите службу elasticsearch-service-x64 командой:
      "$DataRoot\elasticsearch\bin\elasticsearch-service.bat" remove).
"@ | Set-Content -Path $note -Encoding UTF8

foreach ($desk in @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:PUBLIC 'Desktop'))) {
    if ($desk -and (Test-Path $desk)) {
        Copy-Item -Path $note -Destination (Join-Path $desk 'VMS-UDALENIE-OSTALNOGO.txt') -Force
    }
}

Log 'Uninstall script finished.'
