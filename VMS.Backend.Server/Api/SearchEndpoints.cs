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
            string? personName,
            HttpContext http,
            IAccessControlManager accessControl,
            IMetadataIndexer indexer) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.SearchMetadata);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            // personName is an explicit, exact filter (Named Person search tab, picked from
            // the enrolled KnownPersons list) layered onto whatever SearchQueryParser derived
            // from q — distinct from q's own fuzzy free-text name/plate matching.
            var query = SearchQueryParser.Parse(q, cameraId, from, to, size ?? 50) with { PersonName = personName };
            var results = await indexer.SearchAsync(query);
            return Results.Ok(results.Select(e => SearchResultMapper.ToDto(e, matchScore: null)));
        }).RequireAuthorization();

        // Search-by-photo: independent of SearchQueryParser's text/color keyword matching —
        // a reference photo goes straight to a face embedding, then a kNN similarity search
        // over every indexed "person" detection's own embedding (DetectionWorker/IFaceEmbedder).
        // Gated behind Permission.SearchByFace (Operator+, not Viewer) since it tracks a
        // specific person's movements across the whole archive.
        app.MapPost("/api/search/by-face", async (
            IFormFile photo,
            int? size,
            // Optional crop rectangle (pixels, in the uploaded photo's own coordinate space) —
            // set when the photo came from "crop from player" (a rough person/face selection
            // over a paused live-archive frame) rather than a dedicated reference photo, so the
            // embedder looks inside the selection instead of the whole frame.
            int? cropX,
            int? cropY,
            int? cropWidth,
            int? cropHeight,
            HttpContext http,
            IAccessControlManager accessControl,
            IFaceEmbedder faceEmbedder,
            IMetadataIndexer indexer) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.SearchByFace);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var cropBox = cropX is not null && cropY is not null && cropWidth is not null && cropHeight is not null
                ? new DetectionBoundingBox(cropX.Value, cropY.Value, cropWidth.Value, cropHeight.Value)
                : null;

            var tempPath = Path.Combine(Path.GetTempPath(), $"face-search-{Guid.NewGuid():N}{Path.GetExtension(photo.FileName)}");
            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await photo.CopyToAsync(stream);
                }

                var embedding = await faceEmbedder.TryGetEmbeddingAsync(tempPath, cropBox);
                if (embedding is null)
                {
                    return Results.BadRequest("No face was found in the uploaded photo.");
                }

                var results = await indexer.SearchByFaceAsync(embedding, size ?? 50);
                return Results.Ok(results.Select(r => SearchResultMapper.ToDto(r.Event, r.Score)));
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
