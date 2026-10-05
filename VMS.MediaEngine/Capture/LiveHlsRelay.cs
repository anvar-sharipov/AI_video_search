using Microsoft.Extensions.Logging;

namespace VMS.MediaEngine.Capture;

/// <summary>
/// On-demand RTSP→HLS transcode for one camera's live view — the WPF client plays RTSP
/// directly via LibVLC (same LAN, no transport needed), but a phone client is typically
/// off-LAN and can't open an RTSP socket to the camera itself, and mobile OS media stacks
/// don't speak RTSP as reliably as they speak HTTP Live Streaming. `-c copy` (no
/// re-encode, matching RtspRecorder's zero re-encode philosophy) — the camera's own H.264
/// stream is simply repackaged into MPEG-TS segments, so this costs no more CPU than a
/// second `ffmpeg -i rtsp://... -c copy` process per camera. Video only (`-an`): camera
/// audio formats are inconsistent across brands (some are raw PCM, which HLS/TS can't
/// carry), and live view without audio is what most VMS mobile clients default to anyway.
/// </summary>
public sealed class LiveHlsRelay : IAsyncDisposable
{
    private readonly FfmpegProcessSupervisor _supervisor;

    public LiveHlsRelay(
        string ffmpegPath,
        string rtspUri,
        string cameraId,
        string outputDir,
        ILogger logger,
        int segmentSeconds = 2,
        int segmentListSize = 6)
    {
        Directory.CreateDirectory(outputDir);
        var playlistPath = Path.Combine(outputDir, "index.m3u8");
        var segmentPattern = Path.Combine(outputDir, "seg_%05d.ts");

        _supervisor = new FfmpegProcessSupervisor(
            ffmpegPath,
            buildArguments: () => BuildArguments(rtspUri, playlistPath, segmentPattern, segmentSeconds, segmentListSize),
            label: $"live-hls:{cameraId}",
            logger: logger);
    }

    public void Start() => _supervisor.Start();

    public Task StopAsync() => _supervisor.StopAsync();

    private static string BuildArguments(
        string rtspUri, string playlistPath, string segmentPattern, int segmentSeconds, int segmentListSize)
    {
        return string.Join(' ',
            "-hide_banner", "-loglevel", "warning",
            "-rtsp_transport", "tcp",
            "-i", Quote(rtspUri),
            "-an",
            "-c:v", "copy",
            "-f", "hls",
            "-hls_time", segmentSeconds.ToString(),
            "-hls_list_size", segmentListSize.ToString(),
            // delete_segments: keeps only the last hls_list_size .ts files on disk instead
            // of growing forever — this relay runs for as long as anyone's watching live,
            // not a bounded recording. append_list: rewrite the playlist in place rather
            // than replacing it, so a client mid-read never sees a truncated file.
            "-hls_flags", "delete_segments+append_list",
            "-hls_segment_filename", Quote(segmentPattern),
            Quote(playlistPath));
    }

    private static string Quote(string value) => $"\"{value}\"";

    public async ValueTask DisposeAsync() => await _supervisor.DisposeAsync();
}
