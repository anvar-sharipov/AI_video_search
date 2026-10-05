namespace VMS.MetadataIndexer.Detection;

/// <summary>Same rationale as SerializedFaceEmbedder/SerializedObjectDetector: one shared Tesseract engine serves every camera's DetectionWorker, and TesseractEngine isn't safe for concurrent Process() calls.</summary>
public sealed class SerializedPlateOcrReader(IPlateOcrReader inner) : IPlateOcrReader, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string?> TryReadPlateAsync(string imagePath, DetectionBoundingBox vehicleBox, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await inner.TryReadPlateAsync(imagePath, vehicleBox, ct);
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
