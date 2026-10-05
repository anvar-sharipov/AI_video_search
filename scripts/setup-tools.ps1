<#
.SYNOPSIS
    One-time provisioning of external tools/models needed by the VMS:
    - ffmpeg/ffprobe (static Windows build) -> tools/ffmpeg/
    - YOLOv8n ONNX detection model (exported via Ultralytics, opset 17) -> models/
    - Haar cascade face + eye detectors + SFace face embedder ONNX model -> models/
    - Tesseract eng.traineddata (best-effort plate OCR) -> tessdata/

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

# --- Haar cascade (face detection) + SFace (face embedding, OpenCV Zoo) ---
# OpenCvSharp4's Face module only wraps the classic (pre-2021) face APIs — no
# FaceDetectorYN/FaceRecognizerSF binding for the modern YuNet detector — so
# detection uses OpenCV's bundled Haar cascade via CascadeClassifier instead
# (a plain XML, not ONNX; simpler, no anchor-decoding to implement by hand).
$faceDetectorPath = Join-Path $modelsDir "haarcascade_frontalface_default.xml"
if (Test-Path $faceDetectorPath) {
    Write-Host "Haar cascade face detector already present at $faceDetectorPath, skipping download."
} else {
    $cascadeUrl = "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_frontalface_default.xml"
    Write-Host "Downloading Haar cascade face detector from $cascadeUrl ..."
    Invoke-WebRequest -Uri $cascadeUrl -OutFile $faceDetectorPath
    Write-Host "Haar cascade face detector installed to $faceDetectorPath"
}

# Eye cascade (2-point level alignment before embedding — see OnnxFaceEmbedder.AlignByEyes).
$eyeDetectorPath = Join-Path $modelsDir "haarcascade_eye.xml"
if (Test-Path $eyeDetectorPath) {
    Write-Host "Haar cascade eye detector already present at $eyeDetectorPath, skipping download."
} else {
    $eyeCascadeUrl = "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_eye.xml"
    Write-Host "Downloading Haar cascade eye detector from $eyeCascadeUrl ..."
    Invoke-WebRequest -Uri $eyeCascadeUrl -OutFile $eyeDetectorPath
    Write-Host "Haar cascade eye detector installed to $eyeDetectorPath"
}

# SFace embedding model still comes from opencv_zoo, which tracks .onnx files via Git
# LFS — raw.githubusercontent.com only serves the LFS pointer text (~130 bytes),
# media.githubusercontent.com resolves the real blob. Fed through a plain ONNX Runtime
# session directly (Microsoft.ML.OnnxRuntime), not OpenCvSharp, for the same reason.
$faceEmbedderPath = Join-Path $modelsDir "face_recognition_sface_2021dec.onnx"
if (Test-Path $faceEmbedderPath) {
    Write-Host "SFace face embedder already present at $faceEmbedderPath, skipping download."
} else {
    $sfaceUrl = "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx"
    Write-Host "Downloading SFace face embedder from $sfaceUrl ..."
    Invoke-WebRequest -Uri $sfaceUrl -OutFile $faceEmbedderPath
    Write-Host "SFace face embedder installed to $faceEmbedderPath"
}

# --- Tesseract OCR language data (best-effort plate reading — see TesseractPlateOcrReader) ---
$tessDataDir = Join-Path $root "tessdata"
New-Item -ItemType Directory -Force -Path $tessDataDir | Out-Null
$tessDataPath = Join-Path $tessDataDir "eng.traineddata"
if (Test-Path $tessDataPath) {
    Write-Host "Tesseract eng.traineddata already present at $tessDataPath, skipping download."
} else {
    $tessDataUrl = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata"
    Write-Host "Downloading Tesseract eng.traineddata from $tessDataUrl ..."
    Invoke-WebRequest -Uri $tessDataUrl -OutFile $tessDataPath
    Write-Host "Tesseract eng.traineddata installed to $tessDataPath"
}

Write-Host "`nSetup complete."
Write-Host "  ffmpeg:        $toolsDir"
Write-Host "  YOLO model:    $modelPath"
Write-Host "  face detector: $faceDetectorPath"
Write-Host "  eye detector:  $eyeDetectorPath"
Write-Host "  face embedder: $faceEmbedderPath"
Write-Host "  tessdata:      $tessDataPath"
