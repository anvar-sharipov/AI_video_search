namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Best-effort per-camera "who's new in frame" tracker, feeding the "count every person seen +
/// remember with a screenshot" feature. Same nearest-centroid matching between consecutive
/// SNAPSHOTS as PeopleCountingTracker (see its own accuracy caveat — this has the same
/// limitations), but with no line: it just keeps a track alive across a few missed snapshots
/// (MaxMissedFrames) so someone standing still or briefly occluded isn't re-counted as a new
/// person every cycle, and reports which centroids are brand-new tracks this frame.
/// </summary>
public sealed class PersonPresenceTracker
{
    // Same threshold as PeopleCountingTracker — a centroid within this fraction of the frame
    // diagonal of an existing track is "the same person", not a new one.
    private const double MaxMatchDistance = 0.35;

    // How many consecutive snapshots a track may go unmatched (person briefly turned/occluded,
    // or simply missed one sampling interval) before it's dropped and a later reappearance
    // counts as a new sighting.
    private const int MaxMissedFrames = 2;

    private sealed class Track
    {
        public (double X, double Y) Centroid;
        public int MissedFrames;
    }

    private readonly List<Track> _tracks = [];

    /// <summary>Feed this snapshot's person centroids (fractional 0..1 frame coordinates); returns the indices into `centroids` that are brand-new sightings this frame (i.e. matched no existing track).</summary>
    public List<int> ProcessFrame(IReadOnlyList<(double X, double Y)> centroids)
    {
        var unmatchedCurrent = new List<int>(Enumerable.Range(0, centroids.Count));

        foreach (var track in _tracks)
        {
            var closestIndex = -1;
            var closestDistance = double.MaxValue;
            foreach (var ci in unmatchedCurrent)
            {
                var distance = Distance(track.Centroid, centroids[ci]);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = ci;
                }
            }

            if (closestIndex < 0 || closestDistance > MaxMatchDistance)
            {
                track.MissedFrames++;
                continue;
            }

            track.Centroid = centroids[closestIndex];
            track.MissedFrames = 0;
            unmatchedCurrent.Remove(closestIndex);
        }

        foreach (var ci in unmatchedCurrent)
        {
            _tracks.Add(new Track { Centroid = centroids[ci], MissedFrames = 0 });
        }

        _tracks.RemoveAll(t => t.MissedFrames > MaxMissedFrames);
        return unmatchedCurrent;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
