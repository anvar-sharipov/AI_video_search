namespace VMS.MetadataIndexer.Detection;

public interface IPlateOcrReader
{
    /// <summary>Applies to vehicle classes only — a plate is meaningless for "person"/"bicycle".</summary>
    static readonly HashSet<string> VehicleTypes = new(StringComparer.OrdinalIgnoreCase) { "car", "truck", "bus", "motorcycle" };

    /// <summary>
    /// Best-effort read of the plate inside a vehicle detection's bounding box. Returns null
    /// when the object type isn't a vehicle, no plate-like region was found, or the OCR
    /// result was too short/noisy to trust — never throws.
    /// </summary>
    Task<string?> TryReadPlateAsync(string imagePath, DetectionBoundingBox vehicleBox, CancellationToken ct = default);
}
