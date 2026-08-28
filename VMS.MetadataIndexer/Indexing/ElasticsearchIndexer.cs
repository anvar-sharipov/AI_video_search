using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using VMS.Core.Domain;
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

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        var exists = await _client.Indices.ExistsAsync(IndexName, ct);
        if (!exists.Exists)
        {
            await _client.Indices.CreateAsync(IndexName, ct);
        }
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

        var request = new SearchRequest(IndexName)
        {
            Size = query.Size,
            Query = new BoolQuery { Filter = filters }
        };

        var response = await _client.SearchAsync<ObjectMetadataEvent>(request, ct);

        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Search failed: {response.DebugInformation}");
        }

        return response.Documents.OrderByDescending(d => d.Timestamp).ToList();
    }
}
