using VMS.Core.Domain;

namespace VMS.MetadataIndexer.Detection;

/// <summary>Looks up a camera's configured People Counting line (see CameraCountingLine) — null when the camera has none configured, which just skips counting for it.</summary>
public interface ICountingLineProvider
{
    Task<CameraCountingLine?> GetLineAsync(string cameraCode, CancellationToken ct = default);

    /// <summary>Called after a line is added/changed/removed via the API so the next lookup re-reads the database.</summary>
    void Invalidate();
}
