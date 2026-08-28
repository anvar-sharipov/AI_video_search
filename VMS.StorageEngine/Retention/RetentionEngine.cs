using Microsoft.Extensions.Logging;
using VMS.StorageEngine.Immutable;

namespace VMS.StorageEngine.Retention;

/// <summary>
/// Periodically scans each camera's archive directory and deletes segment files
/// older than that camera's retention policy — skipping anything write-protected.
/// This is an automated system policy, not a user action, so it deletes standard
/// expired files directly rather than going through AccessControlManager (which
/// exists to authorize a *person's* delete request, e.g. via ImmutableArchiveManager.Delete).
/// Immutable files are never touched here, regardless of age, by design.
/// </summary>
public sealed class RetentionEngine(
    string archiveRoot,
    IRetentionPolicySource policySource,
    ImmutableArchiveManager immutableManager,
    ILogger logger,
    TimeSpan? scanInterval = null) : IAsyncDisposable
{
    private readonly TimeSpan _scanInterval = scanInterval ?? TimeSpan.FromHours(1);
    private CancellationTokenSource? _cts;
    private Task? _loop;

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
        using var timer = new PeriodicTimer(_scanInterval);

        try
        {
            do
            {
                await RunOneScanAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    /// <summary>Public so a caller can trigger an out-of-band scan (e.g. an admin "purge now" action or a test).</summary>
    public async Task RunOneScanAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(archiveRoot))
        {
            return;
        }

        foreach (var cameraDir in Directory.GetDirectories(archiveRoot))
        {
            ct.ThrowIfCancellationRequested();

            var cameraId = Path.GetFileName(cameraDir);
            int retentionDays;
            try
            {
                retentionDays = await policySource.GetRetentionDaysAsync(cameraId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not resolve retention policy for camera {CameraId}, skipping this scan cycle", cameraId);
                continue;
            }

            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(retentionDays);

            foreach (var file in Directory.GetFiles(cameraDir, "*.mp4"))
            {
                if (immutableManager.IsProtected(file))
                {
                    continue;
                }

                if (File.GetLastWriteTimeUtc(file) >= cutoff)
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    logger.LogInformation("Retention: deleted expired segment {Path} (older than {Days}d)", file, retentionDays);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Retention: could not delete {Path} (in use?)", file);
                }
            }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
