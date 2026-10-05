namespace VMS.Core.Domain;

/// <summary>
/// One person newly appearing in a camera's frame (see PersonPresenceTracker) — distinct from
/// PeopleCountEvent, which only fires on a configured line crossing. Indexed into Elasticsearch
/// like ObjectMetadataEvent/PeopleCountEvent, not Postgres, for the same "time-range aggregation
/// query" reason. ScreenshotPath points at a cropped JPEG saved to disk under the archive root
/// at the moment of the sighting (see DetectionWorker), so the person can be reviewed later even
/// though raw detections/snapshots themselves aren't retained.
/// </summary>
public class PersonSightingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CameraId { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public required BoundingBox BoundingBox { get; set; }

    /// <summary>Set when the face matched an enrolled KnownPerson (see KnownPersonMatcher) — null means unknown/unrecognized.</summary>
    public string? PersonName { get; set; }

    public required string ScreenshotPath { get; set; }
}
