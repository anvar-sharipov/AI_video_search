using VMS.Core.Domain;
using VMS.MetadataIndexer.Search;

namespace VMS.MetadataIndexer.Indexing;

/// <summary>One face-search hit paired with Elasticsearch's cosine-similarity score for that kNN match (0..1, higher is more similar).</summary>
public record ScoredEvent(ObjectMetadataEvent Event, double Score);

/// <summary>One camera's totals for one calendar day — the "how many people were there" report row.</summary>
public record PersonSightingReportRow(string CameraId, DateOnly Date, int Total, int Known, int Unknown);

public interface IMetadataIndexer
{
    Task EnsureIndexAsync(CancellationToken ct = default);
    Task IndexAsync(ObjectMetadataEvent metadataEvent, CancellationToken ct = default);
    Task<IReadOnlyList<ObjectMetadataEvent>> SearchAsync(SearchQuery query, CancellationToken ct = default);

    /// <summary>Single event lookup by its Elasticsearch document id — powers per-event thumbnail generation.</summary>
    Task<ObjectMetadataEvent?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>kNN search against "person" detections' face embeddings — the search-by-photo path. Each hit carries Elasticsearch's cosine-similarity score so the UI can show an honest match percentage.</summary>
    Task<IReadOnlyList<ScoredEvent>> SearchByFaceAsync(float[] embedding, int size, CancellationToken ct = default);

    /// <summary>"person" detections indexed before face search existed (or whose face embedder call failed) — the backfill target set.</summary>
    Task<IReadOnlyList<ObjectMetadataEvent>> FindMissingFaceEmbeddingsAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Records one People Counting line-crossing (see CameraCountingLine/PeopleCountingTracker).</summary>
    Task IndexPeopleCountEventAsync(PeopleCountEvent evt, CancellationToken ct = default);

    /// <summary>In/Out totals for one camera over a date range — People Counting's "count people that entered/left during a certain period."</summary>
    Task<(int In, int Out)> GetPeopleCountAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Records one "new person seen in frame" sighting (see PersonPresenceTracker) — the screenshot itself lives on disk, this just indexes the pointer + identity.</summary>
    Task IndexPersonSightingEventAsync(PersonSightingEvent evt, CancellationToken ct = default);

    /// <summary>Single sighting lookup by its Elasticsearch document id — powers serving the saved screenshot.</summary>
    Task<PersonSightingEvent?> GetPersonSightingAsync(Guid id, CancellationToken ct = default);

    /// <summary>Individual sightings for one camera within a date range, most recent first — the report table's drill-down thumbnail gallery.</summary>
    Task<IReadOnlyList<PersonSightingEvent>> GetPersonSightingsAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, int size = 200, CancellationToken ct = default);

    /// <summary>Per camera, per calendar day: total/known/unknown sighting counts over a date range — the "how many people were there" report.</summary>
    Task<IReadOnlyList<PersonSightingReportRow>> GetPersonSightingReportAsync(IReadOnlyList<string> cameraIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Removes one sighting's ES document — the caller is responsible for also deleting its saved screenshot file. Returns false when there was nothing to delete.</summary>
    Task<bool> DeletePersonSightingAsync(Guid id, CancellationToken ct = default);
}
