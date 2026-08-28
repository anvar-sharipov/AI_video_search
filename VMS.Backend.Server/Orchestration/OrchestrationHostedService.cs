using Microsoft.Extensions.Options;
using VMS.Backend.Server.Startup;
using VMS.StorageEngine.Retention;
using VMS.StorageEngine.Immutable;

namespace VMS.Backend.Server.Orchestration;

/// <summary>
/// Starts every enabled camera's pipeline and the retention sweep on host startup,
/// and stops them cleanly on shutdown.
/// </summary>
public sealed class OrchestrationHostedService(
    CameraOrchestrator orchestrator,
    IRetentionPolicySource retentionPolicySource,
    ImmutableArchiveManager immutableManager,
    IOptions<VmsOptions> vmsOptions,
    ILoggerFactory loggerFactory) : IHostedService
{
    private RetentionEngine? _retentionEngine;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await orchestrator.StartAllEnabledAsync(cancellationToken);

        var options = vmsOptions.Value;
        _retentionEngine = new RetentionEngine(
            options.ArchiveRootPath, retentionPolicySource, immutableManager,
            loggerFactory.CreateLogger<RetentionEngine>(), scanInterval: TimeSpan.FromHours(1));
        _retentionEngine.Start();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_retentionEngine is not null)
        {
            await _retentionEngine.StopAsync();
        }

        await orchestrator.DisposeAsync();
    }
}
