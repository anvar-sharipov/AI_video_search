using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VMS.MediaEngine.Capture;

/// <summary>
/// Periodically grabs a single decoded frame from a camera's stream for the AI
/// detection pipeline (wired up in M4). Each tick is a short-lived ffmpeg process
/// with "-hwaccel auto" so decode uses whatever GPU backend is available (NVDEC/
/// DXVA2/QuickSync) without hardcoding one vendor's API.
///
/// This is a pragmatic MVP choice: a fresh process per snapshot instead of one
/// persistent piped decode. At low sampling rates (seconds, not fps) the per-process
/// overhead is immaterial; if 500-camera scale needs it, this is the place to switch
/// to a continuous piped mjpeg stream or FFmpeg.AutoGen.
/// </summary>
public sealed class SnapshotSampler : IAsyncDisposable
{
    private readonly string _ffmpegPath;
    private readonly string _rtspUri;
    private readonly string _cameraId;
    private readonly string _snapshotDir;
    private readonly ILogger _logger;
    private readonly TimeSpan _interval;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<string>? SnapshotCaptured;

    public SnapshotSampler(
        string ffmpegPath,
        string rtspUri,
        string cameraId,
        string snapshotRoot,
        ILogger logger,
        TimeSpan? interval = null)
    {
        _ffmpegPath = ffmpegPath;
        _rtspUri = rtspUri;
        _cameraId = cameraId;
        _snapshotDir = Path.Combine(snapshotRoot, cameraId);
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(2);

        Directory.CreateDirectory(_snapshotDir);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    var path = await CaptureOneFrameAsync(ct);
                    if (path is not null)
                    {
                        SnapshotCaptured?.Invoke(path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SnapshotSampler[{CameraId}] failed to capture a frame", _cameraId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private async Task<string?> CaptureOneFrameAsync(CancellationToken ct)
    {
        var outputPath = Path.Combine(_snapshotDir, "latest.jpg");
        var arguments = string.Join(' ',
            "-hide_banner", "-loglevel", "error",
            "-y",
            "-hwaccel", "auto",
            "-rtsp_transport", "tcp",
            "-i", $"\"{_rtspUri}\"",
            "-frames:v", "1",
            "-q:v", "3",
            $"\"{outputPath}\"");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_ffmpegPath, arguments)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        FfmpegJobObject.Instance.AddProcess(process.Handle);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("SnapshotSampler[{CameraId}] frame capture timed out", _cameraId);
            TryKill(process);
            return null;
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            _logger.LogDebug("SnapshotSampler[{CameraId}] ffmpeg exited {Code}: {Stderr}", _cameraId, process.ExitCode, stderr);
            return null;
        }

        return outputPath;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
