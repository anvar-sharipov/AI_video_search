namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Number of dimensions in every embedding this contract produces — SFace's fixed
/// output size. Shared by the Elasticsearch dense_vector mapping (must match exactly)
/// and by anything comparing/storing embeddings, so the two never drift apart.
/// </summary>
public static class FaceEmbeddingDimensions
{
    public const int Value = 128;
}

public interface IFaceEmbedder
{
    /// <summary>
    /// Finds the most prominent face in <paramref name="imagePath"/> — inside
    /// <paramref name="cropBox"/> when given (a YOLO "person" detection's bounding box),
    /// or across the whole image when null (a user-uploaded reference photo) — and
    /// returns its <see cref="FaceEmbeddingDimensions"/>-dimensional embedding.
    /// Returns null when no face is found; that's an expected outcome (person facing
    /// away from the camera, photo too blurry/small), not an error.
    /// </summary>
    Task<float[]?> TryGetEmbeddingAsync(string imagePath, DetectionBoundingBox? cropBox, CancellationToken ct = default);
}
