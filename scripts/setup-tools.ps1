<#
.SYNOPSIS
    One-time provisioning of external tools/models needed by the VMS:
    - ffmpeg/ffprobe (static Windows build) -> tools/ffmpeg/
    - YOLOv8n ONNX detection model (exported via Ultralytics, opset 17) -> models/

    Requires internet access. Run once per machine. Everything produced here
    is used fully offline at runtime (AI inference and recording never call out).
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $root "tools\ffmpeg"
$modelsDir = Join-Path $root "models"

New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null

# --- ffmpeg / ffprobe ---
$ffmpegExe = Join-Path $toolsDir "ffmpeg.exe"
if (Test-Path $ffmpegExe) {
    Write-Host "ffmpeg already present at $ffmpegExe, skipping download."
} else {
    $zipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
    $zipPath = Join-Path $env:TEMP "ffmpeg-win64-gpl.zip"
    Write-Host "Downloading ffmpeg static build from $zipUrl ..."
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath

    $extractDir = Join-Path $env:TEMP "ffmpeg-extract"
    if (Test-Path $extractDir) { Remove-Item -Recurse -Force $extractDir }
    Expand-Archive -Path $zipPath -DestinationPath $extractDir

    $binDir = Get-ChildItem -Path $extractDir -Recurse -Directory -Filter "bin" | Select-Object -First 1
    Copy-Item (Join-Path $binDir.FullName "ffmpeg.exe") $toolsDir -Force
    Copy-Item (Join-Path $binDir.FullName "ffprobe.exe") $toolsDir -Force

    Remove-Item $zipPath -Force
    Remove-Item -Recurse -Force $extractDir
    Write-Host "ffmpeg/ffprobe installed to $toolsDir"
}

# --- YOLOv8n ONNX model (COCO pretrained) ---
$modelPath = Join-Path $modelsDir "yolov8n.onnx"
if (Test-Path $modelPath) {
    Write-Host "YOLO model already present at $modelPath, skipping export."
} else {
    Write-Host "Installing/upgrading ultralytics (Python) to export YOLOv8n to ONNX..."
    python -m pip install --quiet --upgrade ultralytics

    Push-Location $modelsDir
    try {
        # opset 17 required for YoloDotNet 2.x compatibility.
        # Note: "yolo" (not "python -m ultralytics" — that has no __main__ entry point).
        yolo export model=yolov8n.pt format=onnx opset=17 simplify=True
        $exported = Join-Path $modelsDir "yolov8n.onnx"
        if (-not (Test-Path $exported)) {
            throw "Export completed but yolov8n.onnx was not found in $modelsDir"
        }
        Write-Host "YOLO model exported to $exported"
    } finally {
        Pop-Location
    }
}

Write-Host "`nSetup complete."
Write-Host "  ffmpeg:  $toolsDir"
Write-Host "  model:   $modelPath"
