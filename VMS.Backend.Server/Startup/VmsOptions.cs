namespace VMS.Backend.Server.Startup;

public class VmsOptions
{
    public const string SectionName = "Vms";

    public string ArchiveRootPath { get; set; } = "Archive";
    public string ClipsRootPath { get; set; } = "Clips";
    public string SnapshotsRootPath { get; set; } = "Snapshots";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public string YoloModelPath { get; set; } = "models/yolov8n.onnx";
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
