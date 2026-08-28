using VMS.Core.Domain;
using VMS.MetadataIndexer.Search;

namespace VMS.MetadataIndexer.Indexing;

public interface IMetadataIndexer
{
    Task EnsureIndexAsync(CancellationToken ct = default);
    Task IndexAsync(ObjectMetadataEvent metadataEvent, CancellationToken ct = default);
    Task<IReadOnlyList<ObjectMetadataEvent>> SearchAsync(SearchQuery query, CancellationToken ct = default);
}
