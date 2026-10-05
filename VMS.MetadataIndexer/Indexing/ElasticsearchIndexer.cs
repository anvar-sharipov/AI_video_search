using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using VMS.Core.Domain;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Search;

namespace VMS.MetadataIndexer.Indexing;

/// <summary>
/// Indexes structured detection events (not raw video) into Elasticsearch and
/// answers AI-search queries against them. Dev-only single-node ES, security
/// disabled — see docker-compose.yml.
///
/// Field names below ("objectType", "colorAttribute", ...) are the client's default
/// camelCase serialization of the ObjectMetadataEvent C# properties.
/// </summary>
public class ElasticsearchIndexer : IMetadataIndexer
{
    private const string IndexName = "vms-object-events";
    private const string PeopleCountIndexName = "vms-people-count-events";
    private const string PersonSightingIndexName = "vms-person-sighting-events";
    private readonly ElasticsearchClient _client;

    public ElasticsearchIndexer(string elasticsearchUri)
    {
        var settings = new ElasticsearchClientSettings(new Uri(elasticsearchUri))
            .DefaultIndex(IndexName);
        _client = new ElasticsearchClient(settings);
    }

    // faceEmbedding: SFace's fixed output size (FaceEmbeddingDimensions.Value) — must
    // match exactly, dense_vector mapping isn't dynamic like the other fields.
    private static readonly Properties FaceEmbeddingMapping = new()
    {
        {
            "faceEmbedding",
            new DenseVectorProperty
            {
                Dims = FaceEmbeddingDimensions.Value,
                Similarity = DenseVectorSimilarity.Cosine,
                Index = true
            }
        }
    };

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        var exists = await _client.Indices.ExistsAsync(IndexName, ct);
        if (!exists.Exists)
        {
            await _client.Indices.CreateAsync(IndexName, c => c
                .Mappings(new TypeMapping { Properties = FaceEmbeddingMapping }), ct);
            return;
        }

        // Index already existed before face search shipped (it was created with no
        // explicit mapping at all — ES inferred everything dynamically). Adding a new
        // field to an existing mapping doesn't touch documents already stored, so this
        // is safe to run every time the app starts, not just once.
        await _client.Indices.PutMappingAsync(IndexName, m => m.Properties(FaceEmbeddingMapping), ct);
    }

    public async Task IndexAsync(ObjectMetadataEvent metadataEvent, CancellationToken ct = default)
    {
        var response = await _client.IndexAsync(metadataEvent, i => i.Index(IndexName).Id(metadataEvent.Id), ct);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to index detection event {metadataEvent.Id}: {response.DebugInformation}");
        }
    }

    public async Task<IReadOnlyList<ObjectMetadataEvent>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var filters = new List<Query>();

        if (!string.IsNullOrWhiteSpace(query.ObjectType))
        {
            filters.Add(new MatchQuery { Field = new Field("objectType"), Query = query.ObjectType });
        }

        if (!string.IsNullOrWhiteSpace(query.ColorAttribute))
        {
            filters.Add(new MatchQuery { Field = new Field("colorAttribute"), Query = query.ColorAttribute });
        }

        if (!string.IsNullOrWhiteSpace(query.CameraId))
        {
            filters.Add(new MatchQuery { Field = new Field("cameraId"), Query = query.CameraId });
        }

        if (query.From is not null || query.To is not null)
        {
            var dateRange = new DateRangeQuery { Field = new Field("timestamp") };
            if (query.From is not null) dateRange.Gte = query.From.Value.ToString("O");
            if (query.To is not null) dateRange.Lte = query.To.Value.ToString("O");
            filters.Add(dateRange);
        }

        if (!string.IsNullOrWhiteSpace(query.PersonName))
        {
            filters.Add(new MatchQuery { Field = new Field("personName"), Query = query.PersonName });
        }

        var boolQuery = new BoolQuery { Filter = filters };

        // Free text (a leftover token SearchQueryParser couldn't map to a color/object-type)
        // might be a person's name or a plate number — both open vocabularies, so match it
        // against either field rather than picking one. MinimumShouldMatch=1 makes this a real
        // filter (AND'd with everything in Filter above) instead of a mere scoring boost, which
        // is what a bare Should list means once Filter clauses are already present.
        if (!string.IsNullOrWhiteSpace(query.FreeTextQuery))
        {
            boolQuery.Should = new List<Query>
            {
                new MatchQuery { Field = new Field("personName"), Query = query.FreeTextQuery },
                new MatchQuery { Field = new Field("plateNumber"), Query = query.FreeTextQuery }
            };
            boolQuery.MinimumShouldMatch = 1;
        }

        // Sort must happen in Elasticsearch, not after Size has already truncated the result
        // set: without this, ES returns an arbitrary first `Size` matches (no scoring
        // differentiates filter-only queries, so it's effectively internal doc order), and a
        // camera with many more historical detections than the others can fill the entire page
        // before a single hit from another camera is ever returned — client-side sorting only
        // reorders the wrong subset.
        var request = new SearchRequest(IndexName)
        {
            Size = query.Size,
            Query = boolQuery,
            Sort = new List<SortOptions>
            {
                new SortOptions { Field = new FieldSort { Field = new Field("timestamp"), Order = SortOrder.Desc } }
            }
        };

        var response = await _client.SearchAsync<ObjectMetadataEvent>(request, ct);

        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Search failed: {response.DebugInformation}");
        }

        return response.Documents.OrderByDescending(d => d.Timestamp).ToList();
    }

    public async Task<ObjectMetadataEvent?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var response = await _client.GetAsync<ObjectMetadataEvent>(id, g => g.Index(IndexName), ct);
        return response.Found ? response.Source : null;
    }

    public async Task<IReadOnlyList<ScoredEvent>> SearchByFaceAsync(float[] embedding, int size, CancellationToken ct = default)
    {
        var request = new SearchRequest(IndexName)
        {
            Size = size,
            Knn = new List<KnnSearch>
            {
                new()
                {
                    Field = new Field("faceEmbedding"),
                    QueryVector = embedding,
                    K = size,
                    NumCandidates = Math.Max(size * 10, 100),
                    Filter = new List<Query> { new MatchQuery { Field = new Field("objectType"), Query = "person" } }
                }
            }
        };

        var response = await _client.SearchAsync<ObjectMetadataEvent>(request, ct);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Face search failed: {response.DebugInformation}");
        }

        // kNN already ranks by similarity (best first) — that's the right order here,
        // unlike SearchAsync's timestamp-desc (a face match's relevance matters more
        // than recency; the caller sees camera/time per hit either way). Score is ES's
        // own kNN similarity score (cosine, 0..1) — the only place an honest "match %"
        // can come from; never fabricated client-side.
        return response.Hits.Select(h => new ScoredEvent(h.Source!, h.Score ?? 0)).ToList();
    }

    public async Task<IReadOnlyList<ObjectMetadataEvent>> FindMissingFaceEmbeddingsAsync(int batchSize, CancellationToken ct = default)
    {
        var request = new SearchRequest(IndexName)
        {
            Size = batchSize,
            Query = new BoolQuery
            {
                Filter = new List<Query> { new MatchQuery { Field = new Field("objectType"), Query = "person" } },
                MustNot = new List<Query> { new ExistsQuery { Field = new Field("faceEmbedding") } }
            }
        };

        var response = await _client.SearchAsync<ObjectMetadataEvent>(request, ct);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Backfill scan failed: {response.DebugInformation}");
        }

        return response.Documents.ToList();
    }

    public async Task IndexPeopleCountEventAsync(PeopleCountEvent evt, CancellationToken ct = default)
    {
        var response = await _client.IndexAsync(evt, i => i.Index(PeopleCountIndexName).Id(evt.Id), ct);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to index people-count event {evt.Id}: {response.DebugInformation}");
        }
    }

    public async Task<(int In, int Out)> GetPeopleCountAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Two plain counts rather than a terms aggregation on the boolean field — simpler and
        // avoids depending on exactly how this client version surfaces boolean bucket keys.
        var inCount = await CountAsync(cameraId, from, to, isIn: true, ct);
        var outCount = await CountAsync(cameraId, from, to, isIn: false, ct);
        return (inCount, outCount);
    }

    private async Task<int> CountAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, bool isIn, CancellationToken ct)
    {
        var filters = new List<Query>
        {
            new MatchQuery { Field = new Field("cameraId"), Query = cameraId },
            new TermQuery { Field = new Field("isIn"), Value = isIn },
            new DateRangeQuery { Field = new Field("timestamp"), Gte = from.ToString("O"), Lte = to.ToString("O") }
        };

        var response = await _client.CountAsync<PeopleCountEvent>(c => c
            .Indices(PeopleCountIndexName)
            .Query(new BoolQuery { Filter = filters }), ct);

        // The index may not exist yet (no counting line configured/crossed on any camera) — that's zero, not an error.
        return response.IsValidResponse ? (int)response.Count : 0;
    }

    public async Task IndexPersonSightingEventAsync(PersonSightingEvent evt, CancellationToken ct = default)
    {
        var response = await _client.IndexAsync(evt, i => i.Index(PersonSightingIndexName).Id(evt.Id), ct);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to index person-sighting event {evt.Id}: {response.DebugInformation}");
        }
    }

    public async Task<PersonSightingEvent?> GetPersonSightingAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _client.GetAsync<PersonSightingEvent>(id, g => g.Index(PersonSightingIndexName), ct);
        return response.Found ? response.Source : null;
    }

    public async Task<IReadOnlyList<PersonSightingEvent>> GetPersonSightingsAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, int size = 200, CancellationToken ct = default)
    {
        var filters = new List<Query>
        {
            new MatchQuery { Field = new Field("cameraId"), Query = cameraId },
            new DateRangeQuery { Field = new Field("timestamp"), Gte = from.ToString("O"), Lte = to.ToString("O") }
        };

        var request = new SearchRequest(PersonSightingIndexName)
        {
            Size = size,
            Query = new BoolQuery { Filter = filters },
            Sort = new List<SortOptions>
            {
                new SortOptions { Field = new FieldSort { Field = new Field("timestamp"), Order = SortOrder.Desc } }
            }
        };

        var response = await _client.SearchAsync<PersonSightingEvent>(request, ct);
        // The index may not exist yet (no sightings recorded on any camera) — that's an empty list, not an error.
        return response.IsValidResponse ? response.Documents.OrderByDescending(d => d.Timestamp).ToList() : [];
    }

    public async Task<IReadOnlyList<PersonSightingReportRow>> GetPersonSightingReportAsync(IReadOnlyList<string> cameraIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Per camera, per day: two plain counts (total, then known-only via ExistsQuery on
        // personName) rather than a terms/date-histogram aggregation — same reasoning as
        // GetPeopleCountAsync above (simpler, avoids depending on this client version's exact
        // aggregation-result shape). Fine for a modest number of cameras/days; a long range
        // across many cameras means many small ES round-trips.
        var rows = new List<PersonSightingReportRow>();
        var firstDay = DateOnly.FromDateTime(from.Date);
        var lastDay = DateOnly.FromDateTime(to.Date);

        foreach (var cameraId in cameraIds)
        {
            for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
            {
                var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), from.Offset);
                var dayEnd = dayStart.AddDays(1).AddTicks(-1);

                var total = await CountPersonSightingsAsync(cameraId, dayStart, dayEnd, knownOnly: false, ct);
                if (total == 0)
                {
                    continue;
                }

                var known = await CountPersonSightingsAsync(cameraId, dayStart, dayEnd, knownOnly: true, ct);
                rows.Add(new PersonSightingReportRow(cameraId, day, total, known, total - known));
            }
        }

        return rows;
    }

    private async Task<int> CountPersonSightingsAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, bool knownOnly, CancellationToken ct)
    {
        var filters = new List<Query>
        {
            new MatchQuery { Field = new Field("cameraId"), Query = cameraId },
            new DateRangeQuery { Field = new Field("timestamp"), Gte = from.ToString("O"), Lte = to.ToString("O") }
        };

        if (knownOnly)
        {
            filters.Add(new ExistsQuery { Field = new Field("personName") });
        }

        var response = await _client.CountAsync<PersonSightingEvent>(c => c
            .Indices(PersonSightingIndexName)
            .Query(new BoolQuery { Filter = filters }), ct);

        // The index may not exist yet (no sightings recorded on any camera) — that's zero, not an error.
        return response.IsValidResponse ? (int)response.Count : 0;
    }

    public async Task<bool> DeletePersonSightingAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _client.DeleteAsync<PersonSightingEvent>(id, d => d.Index(PersonSightingIndexName), ct);
        return response.IsValidResponse && response.Result == Elastic.Clients.Elasticsearch.Result.Deleted;
    }
}
