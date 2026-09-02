namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Same rationale as SerializedObjectDetector: one shared FaceDetectorYN/FaceRecognizerSF
/// pair serves every camera's DetectionWorker plus the search-by-photo endpoint, and
/// OpenCV's detector/recognizer objects aren't safe for concurrent calls from multiple
/// threads. Serializes access instead of loading a model instance per caller.
/// </summary>
public sealed class SerializedFaceEmbedder(IFaceEmbedder inner) : IFaceEmbedder, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<float[]?> TryGetEmbeddingAsync(string imagePath, DetectionBoundingBox? cropBox, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await inner.TryGetEmbeddingAsync(imagePath, cropBox, ct);
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
