using Microsoft.EntityFrameworkCore;
using VMS.Core.Data;
using VMS.StorageEngine.Retention;

namespace VMS.Backend.Server.Orchestration;

/// <summary>
/// StorageEngine's RetentionEngine ticks on its own timer, so it needs a fresh
/// DbContext scope per lookup rather than a long-lived injected one.
/// </summary>
public class DbRetentionPolicySource(IServiceScopeFactory scopeFactory) : IRetentionPolicySource
{
    private const int FallbackDays = 30;

    public async Task<int> GetRetentionDaysAsync(string cameraId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VmsDbContext>();

        var camera = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Code == cameraId, ct);
        if (camera?.RetentionPolicyId is null)
        {
            return FallbackDays;
        }

        var policy = await db.RetentionPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == camera.RetentionPolicyId, ct);

        return policy?.RetentionDays ?? FallbackDays;
    }
}
