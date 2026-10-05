using Microsoft.EntityFrameworkCore;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.MetadataIndexer.Indexing;
using VMS.MetadataIndexer.Search;

namespace VMS.Backend.Server.Api;

public record AlarmRecordDto(ObjectMetadataEvent Detection, bool IsAcknowledged);

/// <summary>
/// Alarm Records: rather than a separate rule engine deciding what "counts" as an alarm (a much
/// bigger feature on its own), this treats every indexed person/vehicle detection as a
/// real-time alarm record — which is genuinely what a VMS operator watching this screen wants to
/// see ("did anything happen") — and adds an acknowledge/audit trail on top via
/// AlarmAcknowledgement, kept in Postgres since it's operator action, not AI-detection output.
/// </summary>
public static class AlarmEndpoints
{
    public static void MapAlarmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/alarms").RequireAuthorization();

        group.MapGet("/", async (
            string? cameraId, DateTimeOffset? from, DateTimeOffset? to, int? size,
            HttpContext http, IAccessControlManager accessControl, IMetadataIndexer indexer, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var query = new SearchQuery(CameraId: cameraId, From: from, To: to, Size: size ?? 100);
            var detections = await indexer.SearchAsync(query);

            var detectionIds = detections.Select(d => d.Id).ToList();
            var acknowledgedIds = await db.AlarmAcknowledgements
                .Where(a => detectionIds.Contains(a.DetectionEventId))
                .Select(a => a.DetectionEventId)
                .ToListAsync();
            var acknowledgedSet = acknowledgedIds.ToHashSet();

            return Results.Ok(detections.Select(d => new AlarmRecordDto(d, acknowledgedSet.Contains(d.Id))));
        });

        group.MapPost("/{eventId:guid}/acknowledge", async (
            Guid eventId, HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ExportClip); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var alreadyAcknowledged = await db.AlarmAcknowledgements.AnyAsync(a => a.DetectionEventId == eventId);
            if (!alreadyAcknowledged)
            {
                var userId = http.User.GetUserId();
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                db.AlarmAcknowledgements.Add(new AlarmAcknowledgement { DetectionEventId = eventId, AcknowledgedByUserId = userId.Value });
                await db.SaveChangesAsync();
            }

            return Results.NoContent();
        });
    }
}
