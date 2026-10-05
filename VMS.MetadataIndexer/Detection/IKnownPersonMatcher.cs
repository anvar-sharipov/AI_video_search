namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Matches a live detection's face embedding against enrolled KnownPerson rows, so
/// DetectionWorker can tag a "person" detection with a real name instead of leaving it
/// anonymous. See KnownPersonMatcher for the actual comparison.
/// </summary>
public interface IKnownPersonMatcher
{
    /// <summary>Returns the enrolled name of the closest KnownPerson above the similarity threshold, or null if nobody matches closely enough.</summary>
    Task<string?> TryMatchAsync(float[] embedding, CancellationToken ct = default);

    /// <summary>Drops the cached KnownPerson list so the next TryMatchAsync call re-reads the database — called after enrolling/removing a person.</summary>
    void Invalidate();
}
