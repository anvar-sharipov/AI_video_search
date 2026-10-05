namespace VMS.Core.Domain;

/// <summary>
/// Shared AI-detection payload contract. Lives in VMS.Core because both
/// VMS.MetadataIndexer (writer, indexes it into Elasticsearch) and
/// VMS.Backend.Server (reader, serves search results) depend on it.
/// This is NOT an EF Core entity — structured detection events are indexed
/// into Elasticsearch, not Postgres, per the "index metadata, not raw video" design.
/// </summary>
public class ObjectMetadataEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The camera's stable string identifier (matches the archive directory name,
    /// e.g. "cam-01") — not the Postgres Cameras.Id Guid. MediaEngine/StorageEngine
    /// already key everything (recording folders, retention, clip lookup) off this
    /// string, so metadata events use the same key rather than a second identity.
    /// </summary>
    public required string CameraId { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public required string ObjectType { get; set; }
    public string? ColorAttribute { get; set; }
    public required double Confidence { get; set; }
    public required BoundingBox BoundingBox { get; set; }

    /// <summary>Path to the recorded segment this detection falls within, used to drive clip extraction.</summary>
    public required string VideoChunkLocation { get; set; }

    /// <summary>
    /// 128-dim SFace embedding of the face found inside this detection's bounding box,
    /// null when ObjectType isn't "person" or no face was found in the crop (e.g. back
    /// turned to the camera). Powers search-by-photo (cosine similarity kNN in Elasticsearch)
    /// independently of the text-based SearchQueryParser path.
    /// </summary>
    public float[]? FaceEmbedding { get; set; }

    /// <summary>
    /// Set when this "person" detection's face embedding matched an enrolled KnownPerson
    /// above the similarity threshold (see KnownPersonMatcher) — null when unmatched or not
    /// a person. Makes "search by name" possible without a face-similarity photo upload.
    /// </summary>
    public string? PersonName { get; set; }

    /// <summary>
    /// Best-effort OCR read of a vehicle detection's plate (see PlateOcrReader) — normalized
    /// to uppercase alphanumeric only. Null when ObjectType isn't a vehicle class, no
    /// plate-like region was found, or the OCR result was too short/noisy to trust. There is
    /// no dedicated plate-localizer model in this project, so accuracy is modest; documented
    /// as a known limitation in the READMEs.
    /// </summary>
    public string? PlateNumber { get; set; }
}

public record BoundingBox(double X, double Y, double Width, double Height);
