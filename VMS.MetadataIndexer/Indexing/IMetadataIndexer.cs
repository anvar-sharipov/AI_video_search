using VMS.Core.Domain;
using VMS.MetadataIndexer.Search;

namespace VMS.MetadataIndexer.Indexing;

public interface IMetadataIndexer
{
    Task EnsureIndexAsync(CancellationToken ct = default);
    Task IndexAsync(ObjectMetadataEvent metadataEvent, CancellationToken ct = default);
    Task<IReadOnlyList<ObjectMetadataEvent>> SearchAsync(SearchQuery query, CancellationToken ct = default);

    /// <summary>kNN search against "person" detections' face embeddings — the search-by-photo path.</summary>
    Task<IReadOnlyList<ObjectMetadataEvent>> SearchByFaceAsync(float[] embedding, int size, CancellationToken ct = default);

    /// <summary>"person" detections indexed before face search existed (or whose face embedder call failed) — the backfill target set.</summary>
    Task<IReadOnlyList<ObjectMetadataEvent>> FindMissingFaceEmbeddingsAsync(int batchSize, CancellationToken ct = default);
}
