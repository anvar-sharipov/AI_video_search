using Microsoft.Extensions.Logging;
using VMS.Core.Domain;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;

namespace VMS.MetadataIndexer;

/// <summary>
/// Bridges MediaEngine's SnapshotSampler to the detection + indexing pipeline:
/// each captured frame is run through the detector, interesting detections get
/// tagged with a color (vehicles only) and written to Elasticsearch as
/// ObjectMetadataEvent documents.
/// </summary>
public sealed class DetectionWorker(
    string cameraId,
    IObjectDetector detector,
    IFaceEmbedder faceEmbedder,
    IMetadataIndexer indexer,
    string archiveRoot,
    ILogger logger)
{
    private static readonly HashSet<string> InterestingTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "person", "car", "truck", "bus", "motorcycle", "bicycle"
    };

    public async Task OnSnapshotCapturedAsync(string snapshotPath, CancellationToken ct = default)
    {
        List<DetectedObject> detections;
        try
        {
            detections = (await detector.DetectAsync(snapshotPath, ct))
                .Where(d => InterestingTypes.Contains(d.ObjectType))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Detection failed for {CameraId} snapshot {Path}", cameraId, snapshotPath);
            return;
        }

        if (detections.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var videoChunk = FindCurrentSegment() ?? snapshotPath;

        foreach (var d in detections)
        {
            var evt = new ObjectMetadataEvent
            {
                CameraId = cameraId,
                Timestamp = now,
                ObjectType = d.ObjectType,
                ColorAttribute = d.ColorAttribute,
                Confidence = d.Confidence,
                BoundingBox = new BoundingBox(d.BoundingBox.X, d.BoundingBox.Y, d.BoundingBox.Width, d.BoundingBox.Height),
                VideoChunkLocation = videoChunk
            };

            if (string.Equals(d.ObjectType, "person", StringComparison.OrdinalIgnoreCase))
            {
                evt.FaceEmbedding = await TryEmbedFaceAsync(snapshotPath, d.BoundingBox, ct);
            }

            try
            {
                await indexer.IndexAsync(evt, ct);
                logger.LogInformation(
                    "Indexed detection: {CameraId} {ObjectType}{Color} conf={Confidence:F2}",
                    cameraId, d.ObjectType, d.ColorAttribute is null ? "" : $" ({d.ColorAttribute})", d.Confidence);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to index detection event for {CameraId}", cameraId);
            }
        }
    }

    /// <summary>
    /// Best-effort: a person crop without a usable face (turned away, too small/blurry)
    /// is common and not an error, so failures here are logged and swallowed the same
    /// way a failed color tag or a failed index write is — one detection's face embedder
    /// hiccup must never drop the underlying person/vehicle detection itself.
    /// </summary>
    private async Task<float[]?> TryEmbedFaceAsync(string snapshotPath, DetectionBoundingBox personBox, CancellationToken ct)
    {
        try
        {
            return await faceEmbedder.TryGetEmbeddingAsync(snapshotPath, personBox, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Face embedding failed for {CameraId} snapshot {Path}", cameraId, snapshotPath);
            return null;
        }
    }

    /// <summary>Best-effort pointer to the archive segment a detection likely falls within — same lookup VideoClipExtractor does later, just picking the newest file here.</summary>
    private string? FindCurrentSegment()
    {
        var cameraDir = Path.Combine(archiveRoot, cameraId);
        if (!Directory.Exists(cameraDir))
        {
            return null;
        }

        return Directory.GetFiles(cameraDir, "*.mp4")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }
}
