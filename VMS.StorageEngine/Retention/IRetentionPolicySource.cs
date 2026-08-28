namespace VMS.StorageEngine.Retention;

/// <summary>
/// StorageEngine doesn't know about Postgres/EF Core — the host (Backend.Server)
/// implements this against the Cameras/RetentionPolicies tables.
/// </summary>
public interface IRetentionPolicySource
{
    Task<int> GetRetentionDaysAsync(string cameraId, CancellationToken ct = default);
}
