using Microsoft.Extensions.Options;
using VMS.Backend.Server.Startup;
using VMS.MetadataIndexer.Indexing;
using VMS.StorageEngine.Retention;
using VMS.StorageEngine.Immutable;

namespace VMS.Backend.Server.Orchestration;

/// <summary>
/// Starts every enabled camera's pipeline and the retention sweep on host startup,
/// and stops them cleanly on shutdown.
/// </summary>
public sealed class OrchestrationHostedService(
    CameraOrchestrator orchestrator,
    IMetadataIndexer indexer,
    IRetentionPolicySource retentionPolicySource,
    ImmutableArchiveManager immutableManager,
    IOptions<VmsOptions> vmsOptions,
    ILoggerFactory loggerFactory) : IHostedService
{
    private RetentionEngine? _retentionEngine;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only place this is called: creates the ES index with the faceEmbedding
        // dense_vector mapping on a fresh cluster, or adds that field to an existing
        // index (safe/idempotent — see ElasticsearchIndexer.EnsureIndexAsync). Without
        // this, ES would dynamically map faceEmbedding as a plain float on first write
        // instead of a dense_vector, and kNN search-by-photo would fail outright.
        await indexer.EnsureIndexAsync(cancellationToken);

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
