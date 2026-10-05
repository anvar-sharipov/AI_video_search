namespace VMS.Backend.Server.Startup;

public class VmsOptions
{
    public const string SectionName = "Vms";

    public string ArchiveRootPath { get; set; } = "Archive";
    public string ClipsRootPath { get; set; } = "Clips";

    /// <summary>Per-camera HLS segment/playlist output for the on-demand live-view relay — see LiveHlsRelayManager.</summary>
    public string LiveHlsRootPath { get; set; } = "LiveHls";
    public string EMapsRootPath { get; set; } = "EMaps";
    public string SnapshotsRootPath { get; set; } = "Snapshots";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public string YoloModelPath { get; set; } = "models/yolov8n.onnx";
    // "../models/..." (not "models/..."): OpenCvSharp/OnnxRuntime validate the file at load
    // time (unlike YoloDotNet, which only touches disk lazily on first inference), and
    // "dotnet run"'s working directory is this project's own folder — same reasoning as
    // FfmpegPath above, one level up to the repo root where models/ actually lives.
    public string FaceDetectorModelPath { get; set; } = "../models/haarcascade_frontalface_default.xml";

    /// <summary>Eye detector used to level-align a face (rotate so the eye line is horizontal) before embedding — see OnnxFaceEmbedder.</summary>
    public string EyeDetectorModelPath { get; set; } = "../models/haarcascade_eye.xml";
    public string FaceEmbedderModelPath { get; set; } = "../models/face_recognition_sface_2021dec.onnx";

    /// <summary>Directory containing eng.traineddata for Tesseract's best-effort plate OCR — see scripts/setup-tools.ps1 and TesseractPlateOcrReader.</summary>
    public string TessDataPath { get; set; } = "../tessdata";
    public string ElasticsearchUri { get; set; } = "http://localhost:9200";

    /// <summary>Dev-only symmetric signing key for issued login JWTs — must move to a real secret store before production.</summary>
    public string JwtSigningKey { get; set; } = "dev-only-signing-key-change-me-before-production-0123456789";
    public int JwtExpiryMinutes { get; set; } = 480;

    /// <summary>
    /// Bootstrap-only: seeds a single Camera row from this on first run (empty
    /// Cameras table), supplied via the gitignored appsettings.local.json so
    /// credentials never land in source control. After that, cameras are managed
    /// entirely through the Cameras table / the camera management API.
    /// </summary>
    public TestCameraOptions? TestCamera { get; set; }
}

public class TestCameraOptions
{
    public required string CameraId { get; set; }
    public required string IpAddress { get; set; }
    public int OnvifPort { get; set; } = 80;
    public required string Username { get; set; }
    public required string Password { get; set; }
}
