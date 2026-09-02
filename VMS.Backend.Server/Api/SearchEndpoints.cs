using VMS.Core.Security;
using VMS.MetadataIndexer.Detection;
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

        // Search-by-photo: independent of SearchQueryParser's text/color keyword matching —
        // a reference photo goes straight to a face embedding, then a kNN similarity search
        // over every indexed "person" detection's own embedding (DetectionWorker/IFaceEmbedder).
        // Gated behind Permission.SearchByFace (Operator+, not Viewer) since it tracks a
        // specific person's movements across the whole archive.
        app.MapPost("/api/search/by-face", async (
            IFormFile photo,
            int? size,
            HttpContext http,
            IAccessControlManager accessControl,
            IFaceEmbedder faceEmbedder,
            IMetadataIndexer indexer) =>
        {
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.SearchByFace);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"face-search-{Guid.NewGuid():N}{Path.GetExtension(photo.FileName)}");
            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await photo.CopyToAsync(stream);
                }

                var embedding = await faceEmbedder.TryGetEmbeddingAsync(tempPath, cropBox: null);
                if (embedding is null)
                {
                    return Results.BadRequest("No face was found in the uploaded photo.");
                }

                var results = await indexer.SearchByFaceAsync(embedding, size ?? 50);
                return Results.Ok(results);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }).RequireAuthorization().DisableAntiforgery();
    }
}
