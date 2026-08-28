using Microsoft.Extensions.Logging;

namespace VMS.MediaEngine.Capture;

/// <summary>
/// Continuous archive recording for one camera's main stream: stream-copies RTSP
/// straight to rolling MP4 segments, no re-encoding (matches the "-c copy" retention
/// spec and keeps CPU/GPU load at zero for this path — only detection sampling decodes).
///
/// Layout: {archiveRoot}/{cameraId}/{yyyy-MM-dd_HH-mm-ss}.mp4 (flat per camera, timestamp
/// in the filename). A date subdirectory per ffmpeg's own strftime would need to exist
/// before midnight rollover — ffmpeg's segment muxer does not create directories — so a
/// flat layout with a timestamped filename sidesteps that instead of pre-creating dirs.
/// </summary>
public sealed class RtspRecorder : IAsyncDisposable
{
    private readonly FfmpegProcessSupervisor _supervisor;

    public RtspRecorder(
        string ffmpegPath,
        string rtspUri,
        string cameraId,
        string archiveRoot,
        ILogger logger,
        int segmentSeconds = 300)
    {
        var cameraArchiveDir = Path.Combine(archiveRoot, cameraId);
        Directory.CreateDirectory(cameraArchiveDir);

        var segmentPattern = Path.Combine(cameraArchiveDir, "%Y-%m-%d_%H-%M-%S.mp4");

        _supervisor = new FfmpegProcessSupervisor(
            ffmpegPath,
            buildArguments: () => BuildArguments(rtspUri, segmentPattern, segmentSeconds),
            label: $"record:{cameraId}",
            logger: logger);
    }

    public void Start() => _supervisor.Start();

    public Task StopAsync() => _supervisor.StopAsync();

    private static string BuildArguments(string rtspUri, string segmentPattern, int segmentSeconds)
    {
        return string.Join(' ',
            "-hide_banner", "-loglevel", "warning",
            "-rtsp_transport", "tcp",
            "-i", Quote(rtspUri),
            "-c", "copy",
            "-f", "segment",
            "-segment_time", segmentSeconds.ToString(),
            "-reset_timestamps", "1",
            "-strftime", "1",
            // Fragmented MP4: without this, a segment's moov atom (the index ffmpeg
            // needs to open the file at all) is only written when the segment closes,
            // so nothing — VideoClipExtractor included — can read the currently-active
            // segment. Fragmenting writes a valid, readable structure incrementally.
            // Must go through -segment_format_options, not a bare -movflags: the
            // "segment" muxer only forwards options passed this way to the per-segment
            // mp4 muxer it creates internally.
            "-segment_format", "mp4",
            "-segment_format_options", "movflags=+frag_keyframe+empty_moov+default_base_moof",
            Quote(segmentPattern));
    }

    private static string Quote(string value) => $"\"{value}\"";

    public async ValueTask DisposeAsync() => await _supervisor.DisposeAsync();
}
