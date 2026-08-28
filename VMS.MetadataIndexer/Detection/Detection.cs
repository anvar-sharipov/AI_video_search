namespace VMS.MetadataIndexer.Detection;

// Named DetectedObject, not "Detection", to avoid clashing with the
// VMS.MetadataIndexer.Detection namespace itself.
public record DetectedObject(string ObjectType, double Confidence, DetectionBoundingBox BoundingBox, string? ColorAttribute = null);

public record DetectionBoundingBox(int X, int Y, int Width, int Height);
