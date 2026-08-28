using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VMS.Backend.Server.Startup;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.MediaEngine.Capture;
using VMS.MediaEngine.Onvif;
using VMS.MetadataIndexer;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;

namespace VMS.Backend.Server.Orchestration;

public record CameraPipelineStatus(string Code, bool Running, DateTimeOffset OnboardedAt, string MainStreamUri, string SubStreamUri);

/// <summary>
/// Owns the live per-camera pipelines (recorder + snapshot sampler + detection
/// worker) for every enabled camera in the Cameras table. One camera failing to
/// onboard (bad credentials, unreachable IP) never stops the others — each is
/// onboarded independently and errors are logged, not thrown, from StartAllEnabledAsync.
/// </summary>
public sealed class CameraOrchestrator(
    IServiceScopeFactory scopeFactory,
    OnvifCameraDiscoveryService discovery,
    IMetadataIndexer indexer,
    IObjectDetector detector,
    IOptions<VmsOptions> vmsOptions,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private sealed record Pipeline(
        Guid CameraId, string Code, RtspRecorder Recorder, SnapshotSampler Sampler,
        CameraConnectionInfo Connection, DateTimeOffset OnboardedAt);

    private readonly VmsOptions _options = vmsOptions.Value;
    private readonly ILogger _logger = loggerFactory.CreateLogger<CameraOrchestrator>();
    private readonly ConcurrentDictionary<string, Pipeline> _pipelines = new();

    public IReadOnlyCollection<string> RunningCameraCodes => _pipelines.Keys.ToList();

    public async Task StartAllEnabledAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VmsDbContext>();
        var cameras = await db.Cameras.AsNoTracking().Where(c => c.IsEnabled).ToListAsync(ct);

        foreach (var camera in cameras)
        {
            try
            {
                await OnboardCameraAsync(camera, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to onboard camera {Code} ({IpAddress}) at startup", camera.Code, camera.IpAddress);
            }
        }
    }

    public async Task OnboardCameraAsync(Camera camera, CancellationToken ct = default)
    {
        if (_pipelines.ContainsKey(camera.Code))
        {
            return;
        }

        var connection = await discovery.ConnectAsync(
            camera.IpAddress, camera.OnvifUsername!, camera.OnvifPasswordPlaintext!, camera.OnvifPort, ct);

        var recorder = new RtspRecorder(
            _options.FfmpegPath, connection.MainStreamRtspUri, camera.Code, _options.ArchiveRootPath,
            loggerFactory.CreateLogger($"Recorder:{camera.Code}"), segmentSeconds: 300);
        recorder.Start();

        var sampler = new SnapshotSampler(
            _options.FfmpegPath, connection.SubStreamRtspUri, camera.Code, _options.SnapshotsRootPath,
            loggerFactory.CreateLogger($"Sampler:{camera.Code}"), interval: TimeSpan.FromSeconds(5));

        var detectionLogger = loggerFactory.CreateLogger($"Detection:{camera.Code}");
        var detectionWorker = new DetectionWorker(camera.Code, detector, indexer, _options.ArchiveRootPath, detectionLogger);
        sampler.SnapshotCaptured += path => _ = RunDetectionSafelyAsync(detectionWorker, path, detectionLogger);
        sampler.Start();

        _pipelines[camera.Code] = new Pipeline(camera.Id, camera.Code, recorder, sampler, connection, DateTimeOffset.Now);
        _logger.LogInformation("Camera {Code} onboarded, pipelines running.", camera.Code);
    }

    public async Task StopCameraAsync(string code)
    {
        if (_pipelines.TryRemove(code, out var pipeline))
        {
            await pipeline.Recorder.StopAsync();
            await pipeline.Sampler.StopAsync();
            _logger.LogInformation("Camera {Code} pipelines stopped.", code);
        }
    }

    public CameraPipelineStatus? GetStatus(string code) =>
        _pipelines.TryGetValue(code, out var p)
            ? new CameraPipelineStatus(p.Code, true, p.OnboardedAt, p.Connection.MainStreamRtspUri, p.Connection.SubStreamRtspUri)
            : null;

    private static async Task RunDetectionSafelyAsync(DetectionWorker worker, string snapshotPath, ILogger logger)
    {
        try
        {
            await worker.OnSnapshotCapturedAsync(snapshotPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Detection pipeline error for snapshot {Path}", snapshotPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var code in _pipelines.Keys.ToList())
        {
            await StopCameraAsync(code);
        }
    }
}
