using VMS.Core.Security;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;
using VMS.StorageEngine.Clips;

namespace VMS.Backend.Server.Api;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // One-off admin operation (no UI button — see readme Known Limitations): re-derives
        // face embeddings for "person" detections that were indexed before search-by-photo
        // existed. Triggered manually (curl/Postman) since it can take a while — thousands
        // of existing detections each need an ffmpeg frame-extract + face-embed call.
        app.MapPost("/api/admin/backfill-face-embeddings", (
            HttpContext http,
            IAccessControlManager accessControl,
            IMetadataIndexer indexer,
            IFaceEmbedder faceEmbedder,
            VideoClipExtractor clipExtractor,
            ILoggerFactory loggerFactory) =>
        {
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.ManageSystemConfig);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var logger = loggerFactory.CreateLogger("FaceEmbeddingBackfill");
            _ = Task.Run(() => RunBackfillAsync(indexer, faceEmbedder, clipExtractor, logger));
            return Results.Accepted(value: "Backfill started in the background — see server logs for progress.");
        }).RequireAuthorization();
    }

    private static async Task RunBackfillAsync(
        IMetadataIndexer indexer, IFaceEmbedder faceEmbedder, VideoClipExtractor clipExtractor, ILogger logger)
    {
        var framesDir = Path.Combine(Path.GetTempPath(), "vms-face-backfill");
        Directory.CreateDirectory(framesDir);

        // FindMissingFaceEmbeddingsAsync always returns the same page as long as a document
        // stays without an embedding — a doc we couldn't embed (no face found, or its segment
        // was already deleted by retention) would show up again forever. Tracking Ids we've
        // already attempted this run is what lets the loop actually terminate.
        var seen = new HashSet<Guid>();
        var embedded = 0;
        var skippedNoSegment = 0;
        var skippedNoFace = 0;

        while (true)
        {
            var batch = await indexer.FindMissingFaceEmbeddingsAsync(batchSize: 100);
            var fresh = batch.Where(e => seen.Add(e.Id)).ToList();
            if (fresh.Count == 0)
            {
                break;
            }

            foreach (var evt in fresh)
            {
                string? framePath = null;
                try
                {
                    framePath = await clipExtractor.ExtractFrameAsync(evt.CameraId, evt.Timestamp, framesDir);
                    if (framePath is null)
                    {
                        skippedNoSegment++;
                        continue;
                    }

                    var box = new DetectionBoundingBox(
                        (int)evt.BoundingBox.X, (int)evt.BoundingBox.Y,
                        (int)evt.BoundingBox.Width, (int)evt.BoundingBox.Height);
                    var embedding = await faceEmbedder.TryGetEmbeddingAsync(framePath, box);
                    if (embedding is null)
                    {
                        skippedNoFace++;
                        continue;
                    }

                    evt.FaceEmbedding = embedding;
                    await indexer.IndexAsync(evt);
                    embedded++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Backfill failed for detection {Id} ({CameraId} @ {Timestamp})", evt.Id, evt.CameraId, evt.Timestamp);
                }
                finally
                {
                    if (framePath is not null && File.Exists(framePath))
                    {
                        File.Delete(framePath);
                    }
                }
            }

            logger.LogInformation(
                "Face embedding backfill progress: {Embedded} embedded, {NoSegment} segment already gone, {NoFace} no face found",
                embedded, skippedNoSegment, skippedNoFace);
        }

        logger.LogInformation(
            "Face embedding backfill complete: {Embedded} embedded, {NoSegment} segment already gone, {NoFace} no face found",
            embedded, skippedNoSegment, skippedNoFace);
    }
}
