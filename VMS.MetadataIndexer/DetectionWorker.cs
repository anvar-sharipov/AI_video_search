using Microsoft.Extensions.Logging;
using VMS.Core.Domain;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;

namespace VMS.MetadataIndexer;

/// <summary>
/// Bridges MediaEngine's SnapshotSampler to the detection + indexing pipeline: each captured
/// frame is run through the detector, interesting detections get enriched (color for vehicles,
/// face embedding + known-person name for people, best-effort plate OCR for vehicles) and
/// written to Elasticsearch as ObjectMetadataEvent documents. Also feeds "person" centroids
/// through PeopleCountingTracker when this camera has a configured counting line.
/// </summary>
public sealed class DetectionWorker(
    string cameraId,
    IObjectDetector detector,
    IFaceEmbedder faceEmbedder,
    IKnownPersonMatcher knownPersonMatcher,
    IPlateOcrReader plateOcrReader,
    ICountingLineProvider countingLineProvider,
    IMetadataIndexer indexer,
    string archiveRoot,
    ILogger logger)
{
    private static readonly HashSet<string> InterestingTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "person", "car", "truck", "bus", "motorcycle", "bicycle"
    };

    private readonly PeopleCountingTracker _countingTracker = new();
    private readonly PersonPresenceTracker _presenceTracker = new();

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

        // Still worth a counting pass even with zero detections: it lets the tracker see "nobody
        // here now" and clear stale tracked centroids, instead of matching a much-later arrival
        // against someone who really just walked out of frame.
        await ProcessPeopleCountingAsync(snapshotPath, detections, DateTimeOffset.Now, ct);

        if (detections.Count == 0)
        {
            // Same "nobody here now" reasoning as the counting tracker above, so a later arrival
            // isn't matched against a track that really just walked out of frame.
            _presenceTracker.ProcessFrame([]);
            return;
        }

        var now = DateTimeOffset.Now;
        var videoChunk = FindCurrentSegment() ?? snapshotPath;
        var personItems = new List<(DetectionBoundingBox Box, string? PersonName)>();

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
                if (evt.FaceEmbedding is not null)
                {
                    evt.PersonName = await TryMatchKnownPersonAsync(evt.FaceEmbedding, ct);
                }
                personItems.Add((d.BoundingBox, evt.PersonName));
            }
            else if (IPlateOcrReader.VehicleTypes.Contains(d.ObjectType))
            {
                evt.PlateNumber = await TryReadPlateAsync(snapshotPath, d.BoundingBox, ct);
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

        await ProcessPersonSightingsAsync(snapshotPath, personItems, now, ct);
    }

    /// <summary>Best-effort: skipped entirely (not an error) when this camera has no configured
    /// counting line, when the snapshot's dimensions can't be read, or when the crossing math
    /// throws — a miscounted/missed crossing must never affect the detections already indexed
    /// above. See PeopleCountingTracker's own accuracy caveat.</summary>
    private async Task ProcessPeopleCountingAsync(string snapshotPath, List<DetectedObject> detections, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var line = await countingLineProvider.GetLineAsync(cameraId, ct);
            if (line is null)
            {
                return;
            }

            var imageInfo = SkiaSharp.SKBitmap.DecodeBounds(snapshotPath);
            if (imageInfo.Width <= 0 || imageInfo.Height <= 0)
            {
                return;
            }

            var centroids = detections
                .Where(d => string.Equals(d.ObjectType, "person", StringComparison.OrdinalIgnoreCase))
                .Select(d => (
                    X: (d.BoundingBox.X + (d.BoundingBox.Width / 2.0)) / imageInfo.Width,
                    Y: (d.BoundingBox.Y + (d.BoundingBox.Height / 2.0)) / imageInfo.Height))
                .ToList();

            var crossings = _countingTracker.ProcessFrame(centroids, line);
            foreach (var isIn in crossings)
            {
                await indexer.IndexPeopleCountEventAsync(new PeopleCountEvent { CameraId = cameraId, Timestamp = now, IsIn = isIn }, ct);
                logger.LogInformation("People counting: {CameraId} {Direction}", cameraId, isIn ? "In" : "Out");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "People counting failed for {CameraId}", cameraId);
        }
    }

    /// <summary>Best-effort: skipped entirely (not an error) when there are no person detections
    /// this cycle, when the snapshot's dimensions can't be read, or when a screenshot fails to
    /// crop/save — a missed "new person" sighting must never affect the detections already
    /// indexed above. See PersonPresenceTracker's own accuracy caveat.</summary>
    private async Task ProcessPersonSightingsAsync(string snapshotPath, List<(DetectionBoundingBox Box, string? PersonName)> personItems, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            if (personItems.Count == 0)
            {
                _presenceTracker.ProcessFrame([]);
                return;
            }

            var imageInfo = SkiaSharp.SKBitmap.DecodeBounds(snapshotPath);
            if (imageInfo.Width <= 0 || imageInfo.Height <= 0)
            {
                return;
            }

            var centroids = personItems
                .Select(p => (
                    X: (p.Box.X + (p.Box.Width / 2.0)) / imageInfo.Width,
                    Y: (p.Box.Y + (p.Box.Height / 2.0)) / imageInfo.Height))
                .ToList();

            var newSightingIndices = _presenceTracker.ProcessFrame(centroids);
            foreach (var i in newSightingIndices)
            {
                await SavePersonSightingAsync(snapshotPath, personItems[i].Box, personItems[i].PersonName, now, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Person sighting tracking failed for {CameraId}", cameraId);
        }
    }

    /// <summary>Crops+saves a screenshot for one newly-tracked person and indexes a PersonSightingEvent — best-effort, logged and swallowed like every other per-detection enrichment step here.</summary>
    private async Task SavePersonSightingAsync(string snapshotPath, DetectionBoundingBox box, string? personName, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var domainBox = new BoundingBox(box.X, box.Y, box.Width, box.Height);
            var bytes = ThumbnailCropper.CropToBoundingBox(snapshotPath, domainBox);

            var dayDir = Path.Combine(archiveRoot, "people", cameraId, now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dayDir);
            var filePath = Path.Combine(dayDir, $"{now:HHmmss_fff}_{Guid.NewGuid():N}.jpg");
            await File.WriteAllBytesAsync(filePath, bytes, ct);

            await indexer.IndexPersonSightingEventAsync(new PersonSightingEvent
            {
                CameraId = cameraId,
                Timestamp = now,
                BoundingBox = domainBox,
                PersonName = personName,
                ScreenshotPath = filePath
            }, ct);

            logger.LogInformation("Person sighting: {CameraId} {Who}", cameraId, personName ?? "unknown");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save person sighting for {CameraId}", cameraId);
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

    /// <summary>Best-effort: an unrecognized face is the common case (most people simply
    /// aren't enrolled), not an error — same swallow-and-log treatment as the embedding itself.</summary>
    private async Task<string?> TryMatchKnownPersonAsync(float[] embedding, CancellationToken ct)
    {
        try
        {
            return await knownPersonMatcher.TryMatchAsync(embedding, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Known-person matching failed for {CameraId}", cameraId);
            return null;
        }
    }

    /// <summary>Best-effort: no plate-shaped region found, or an unreadable/angled plate, is
    /// the common case (see TesseractPlateOcrReader's own accuracy caveat) — swallowed like
    /// every other per-detection enrichment step here.</summary>
    private async Task<string?> TryReadPlateAsync(string snapshotPath, DetectionBoundingBox vehicleBox, CancellationToken ct)
    {
        try
        {
            return await plateOcrReader.TryReadPlateAsync(snapshotPath, vehicleBox, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Plate OCR failed for {CameraId}", cameraId);
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
