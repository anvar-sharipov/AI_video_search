namespace VMS.Core.Domain;

/// <summary>
/// A named person enrolled via a reference photo. Plain face-embedding search (SearchByFace)
/// only finds detections that look similar to an arbitrary uploaded photo — it has no concept
/// of identity. Enrolling a KnownPerson gives a face embedding a stable Name, so DetectionWorker
/// can tag matching "person" detections with that Name (see KnownPersonMatcher), which is what
/// makes searching the archive/live feed by a person's name possible.
/// </summary>
public class KnownPerson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }

    /// <summary>Same 128-dim SFace embedding shape as ObjectMetadataEvent.FaceEmbedding — compared against it via cosine similarity, not stored in Elasticsearch.</summary>
    public required float[] FaceEmbedding { get; set; }

    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
}
