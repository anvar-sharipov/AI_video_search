namespace VMS.Core.Domain;

/// <summary>A floor-plan/site image with camera icons pinned to it, for the E-map feature's spatial "click a pin to jump to that camera" navigation.</summary>
public class EMap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }

    /// <summary>Filename under the e-maps storage root (VmsOptions.EMapsRootPath) — same pattern as Camera snapshots, not a full path.</summary>
    public required string ImageFileName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A camera's pinned position on an EMap, as a fraction (0..1) of the image's width/height so the pin stays correctly placed regardless of what size the image is displayed at.</summary>
public class EMapPin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid EMapId { get; set; }
    public required Guid CameraId { get; set; }
    public required double X { get; set; }
    public required double Y { get; set; }
}
