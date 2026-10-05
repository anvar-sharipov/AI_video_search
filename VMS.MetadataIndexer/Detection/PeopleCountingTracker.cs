using VMS.Core.Domain;

namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Best-effort line-crossing counter for one camera. Honest limitation up front: this project
/// samples snapshots every few seconds (SnapshotSampler), not continuous video frames, so this
/// is nearest-centroid matching between consecutive SNAPSHOTS rather than real multi-object
/// tracking — a person who fully crosses the line between two snapshots, or two people who pass
/// close together, can be missed or miscounted. There is no dedicated tracking model in this
/// project (that's normally a separate ML component in real ANPR/counting systems); this is the
/// simplest thing that can plausibly work with what YOLO already gives us per snapshot.
/// </summary>
public sealed class PeopleCountingTracker
{
    // Fraction of the frame diagonal — how far a centroid can move between snapshots and still be
    // considered "the same person" rather than a different, newly-appeared one.
    private const double MaxMatchDistance = 0.35;

    private List<(double X, double Y)> _previousCentroids = [];

    /// <summary>Feed this snapshot's person centroids (fractional 0..1 frame coordinates); returns one bool per detected crossing this frame (true = In, false = Out) per the line's LeftToRightIsIn convention.</summary>
    public List<bool> ProcessFrame(IReadOnlyList<(double X, double Y)> centroids, CameraCountingLine line)
    {
        var crossings = new List<bool>();
        var unmatchedCurrent = new List<(double X, double Y)>(centroids);

        foreach (var previous in _previousCentroids)
        {
            var closestIndex = -1;
            var closestDistance = double.MaxValue;
            for (var i = 0; i < unmatchedCurrent.Count; i++)
            {
                var distance = Distance(previous, unmatchedCurrent[i]);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            if (closestIndex < 0 || closestDistance > MaxMatchDistance)
            {
                continue;
            }

            var current = unmatchedCurrent[closestIndex];
            unmatchedCurrent.RemoveAt(closestIndex);

            if (TryGetCrossing(previous, current, line, out var isIn))
            {
                crossings.Add(isIn);
            }
        }

        _previousCentroids = centroids.ToList();
        return crossings;
    }

    private static bool TryGetCrossing((double X, double Y) from, (double X, double Y) to, CameraCountingLine line, out bool isIn)
    {
        isIn = false;
        var points = line.GetPoints();

        // The tripwire is a polyline (>=2 points) — a crossing of ANY one segment counts, checked
        // in order and stopping at the first hit (a single frame-to-frame move crossing more than
        // one segment of the same tripwire is not a meaningful distinct scenario here).
        for (var i = 0; i < points.Count - 1; i++)
        {
            var segmentStart = points[i];
            var segmentEnd = points[i + 1];
            if (!SegmentsIntersect(from, to, segmentStart, segmentEnd))
            {
                continue;
            }

            // Which side of the segment each point falls on, via the 2D cross product of (segment
            // direction) and (point - segment start) — sign flip between "from" and "to" is
            // exactly what SegmentsIntersect already confirmed, so this just reads off the
            // direction of travel.
            var lineDx = segmentEnd.X - segmentStart.X;
            var lineDy = segmentEnd.Y - segmentStart.Y;
            var sideFrom = (lineDx * (from.Y - segmentStart.Y)) - (lineDy * (from.X - segmentStart.X));

            var wentLeftToRight = sideFrom < 0;
            isIn = wentLeftToRight == line.LeftToRightIsIn;
            return true;
        }

        return false;
    }

    private static bool SegmentsIntersect((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3, (double X, double Y) p4)
    {
        var d1 = CrossProductSign(p3, p4, p1);
        var d2 = CrossProductSign(p3, p4, p2);
        var d3 = CrossProductSign(p1, p2, p3);
        var d4 = CrossProductSign(p1, p2, p4);

        return d1 != d2 && d3 != d4;
    }

    private static bool CrossProductSign((double X, double Y) a, (double X, double Y) b, (double X, double Y) p) =>
        ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X)) > 0;

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
