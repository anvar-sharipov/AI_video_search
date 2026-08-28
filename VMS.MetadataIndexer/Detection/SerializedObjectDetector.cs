namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// YoloDotNet's execution providers keep mutable per-call state (the last inference
/// result, kept around for disposal) — concurrent Run calls on the same instance
/// are not safe. This serializes access so one Yolo instance can be shared across
/// every camera's DetectionWorker instead of loading the ONNX model once per camera.
///
/// At true 500-camera scale this becomes the throughput bottleneck and would need
/// a small pool of detector instances (or batched inference) instead of one queue —
/// noted here as the next thing to revisit, not solved now.
/// </summary>
public sealed class SerializedObjectDetector(IObjectDetector inner) : IObjectDetector, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<DetectedObject>> DetectAsync(string imagePath, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await inner.DetectAsync(imagePath, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        (inner as IDisposable)?.Dispose();
    }
}
