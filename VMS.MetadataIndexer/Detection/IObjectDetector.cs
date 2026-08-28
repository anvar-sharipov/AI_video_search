namespace VMS.MetadataIndexer.Detection;

public interface IObjectDetector
{
    Task<IReadOnlyList<DetectedObject>> DetectAsync(string imagePath, CancellationToken ct = default);
}
