using VMS.Core.Security;
using VMS.MetadataIndexer.Indexing;
using VMS.MetadataIndexer.Search;

namespace VMS.Backend.Server.Api;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", async (
            string q,
            string? cameraId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? size,
            HttpContext http,
            IAccessControlManager accessControl,
            IMetadataIndexer indexer) =>
        {
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.SearchMetadata);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var query = SearchQueryParser.Parse(q, cameraId, from, to, size ?? 50);
            var results = await indexer.SearchAsync(query);
            return Results.Ok(results);
        }).RequireAuthorization();
    }
}
