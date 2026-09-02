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

        // Sort must happen in Elasticsearch, not after Size has already truncated the result
        // set: without this, ES returns an arbitrary first `Size` matches (no scoring
        // differentiates filter-only queries, so it's effectively internal doc order), and a
        // camera with many more historical detections than the others can fill the entire page
        // before a single hit from another camera is ever returned — client-side sorting only
        // reorders the wrong subset.
        var request = new SearchRequest(IndexName)
        {
            Size = query.Size,
            Query = new BoolQuery { Filter = filters },
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

    public async Task<IReadOnlyList<ObjectMetadataEvent>> SearchByFaceAsync(float[] embedding, int size, CancellationToken ct = default)
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
        // than recency; the caller sees camera/time per hit either way).
        return response.Documents.ToList();
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
}
