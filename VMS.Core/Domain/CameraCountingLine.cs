using System.Text.Json;

namespace VMS.Core.Domain;

/// <summary>
/// A virtual tripwire configured for one camera's People Counting feature — a person's detection
/// centroid crossing any segment of this polyline (tracked snapshot-to-snapshot, see
/// PeopleCountingTracker) counts as an IN or OUT event depending on crossing direction.
/// Coordinates are fractions (0..1) of the snapshot frame, same convention as EMapPin, so the
/// line stays correctly placed regardless of the actual camera resolution.
/// </summary>
public class CameraCountingLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Camera.Code, not Camera.Id — DetectionWorker/MetadataIndexer only ever know a
    /// camera by its stable Code (see Camera.cs's own doc comment for why), so keying this the
    /// same way avoids a Postgres join purely to translate one id into the other at runtime.</summary>
    public required string CameraCode { get; set; }

    /// <summary>
    /// Ordered list of at least 2 points (fractional 0..1 frame coordinates), JSON-encoded as
    /// [[x,y],[x,y],...] — see GetPoints/SetPoints. Consecutive points form straight segments, so
    /// the tripwire can bend to follow an angled doorway/passage instead of being restricted to a
    /// single straight line; a person's centroid crossing ANY segment counts. Stored as JSON text
    /// rather than a child table since it's always read/written as one whole unit per camera, the
    /// same reasoning as User.AdditionalPermissionsCsv.
    /// </summary>
    public required string PointsJson { get; set; }

    /// <summary>
    /// Crossing from a segment's left side (per the 2D cross-product of that segment's direction)
    /// to the right counts as "In" when true, "Out" when false — lets an operator match the line's
    /// meaning to the camera's actual physical doorway orientation without needing to redraw it.
    /// Applies uniformly to every segment of the polyline.
    /// </summary>
    public bool LeftToRightIsIn { get; set; } = true;

    public List<(double X, double Y)> GetPoints() =>
        JsonSerializer.Deserialize<List<double[]>>(PointsJson)!
            .Select(p => (p[0], p[1]))
            .ToList();

    public void SetPoints(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("A counting line needs at least 2 points.", nameof(points));
        }

        PointsJson = JsonSerializer.Serialize(points.Select(p => new[] { p.X, p.Y }));
    }
}
