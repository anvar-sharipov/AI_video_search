using Microsoft.Extensions.Logging;
using VMS.Core.Security;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;
using VMS.StorageEngine.Clips;

namespace VMS.Backend.Server.Api;

public static class EventThumbnailEndpoints
{
    public static void MapEventThumbnailEndpoints(this WebApplication app)
    {
        // Same gate as viewing search results at all — a thumbnail reveals nothing a
        // caller couldn't already see by requesting a clip for the same event.
        app.MapGet("/api/events/{id}/thumbnail", async (
            string id,
            HttpContext http,
            IAccessControlManager accessControl,
            IMetadataIndexer indexer,
            VideoClipExtractor clipExtractor,
            ILogger<Program> logger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.SearchMetadata);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var evt = await indexer.GetByIdAsync(id);
            if (evt is null)
            {
                return Results.NotFound();
            }

            string? framePath;
            var tempDir = Path.Combine(Path.GetTempPath(), "vms-thumbnails");
            try
            {
                framePath = await clipExtractor.ExtractFrameAsync(evt.CameraId, evt.Timestamp, tempDir);
            }
            catch (InvalidOperationException ex)
            {
                // Most commonly: the event falls inside the currently-recording segment,
                // which has no moov atom yet (fragmented MP4 only finalizes it when the
                // segment closes — see CLAUDE.md/VideoClipExtractor) and so can't be read
                // by ffmpeg at all yet. A missing thumbnail for a very recent event is an
                // expected, temporary gap, not a server error.
                logger.LogInformation(ex, "Thumbnail frame extraction failed for event {EventId} (camera {CameraId} @ {Timestamp}) — likely still-recording segment", id, evt.CameraId, evt.Timestamp);
                return Results.NotFound();
            }

            if (framePath is null)
            {
                // Retention already deleted the archive segment covering this old event —
                // an expected gap, not a server error.
                return Results.NotFound();
            }

            try
            {
                var bytes = ThumbnailCropper.CropToBoundingBox(framePath, evt.BoundingBox);
                return Results.File(bytes, "image/jpeg");
            }
            finally
            {
                if (File.Exists(framePath))
                {
                    File.Delete(framePath);
                }
            }
        }).RequireAuthorization();
    }
}
