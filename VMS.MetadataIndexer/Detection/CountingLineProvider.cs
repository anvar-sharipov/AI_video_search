using Microsoft.EntityFrameworkCore;
using VMS.Core.Data;
using VMS.Core.Domain;

namespace VMS.MetadataIndexer.Detection;

/// <summary>Caches every camera's counting line in memory (same rationale as KnownPersonMatcher: small config table, read once per DetectionWorker snapshot cycle across every camera, refreshed on explicit Invalidate rather than a poll).</summary>
public sealed class CountingLineProvider(IDbContextFactory<VmsDbContext> dbContextFactory) : ICountingLineProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, CameraCountingLine>? _cache;

    public void Invalidate() => _cache = null;

    public async Task<CameraCountingLine?> GetLineAsync(string cameraCode, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(ct);
        return cache.GetValueOrDefault(cameraCode);
    }

    private async Task<Dictionary<string, CameraCountingLine>> GetCacheAsync(CancellationToken ct)
    {
        if (_cache is { } existing)
        {
            return existing;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is { } cachedWhileWaiting)
            {
                return cachedWhileWaiting;
            }

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var lines = await db.CameraCountingLines.AsNoTracking().ToListAsync(ct);
            _cache = lines.ToDictionary(l => l.CameraCode, StringComparer.OrdinalIgnoreCase);
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
}
