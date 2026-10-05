using Microsoft.EntityFrameworkCore;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.MetadataIndexer.Indexing;

namespace VMS.Backend.Server.Api;

public record PersonSightingReportRowDto(string CameraId, string CameraName, DateOnly Date, int Total, int Known, int Unknown);
public record PersonSightingDto(Guid Id, string CameraId, DateTimeOffset Timestamp, string? PersonName);

/// <summary>
/// "Count every person seen + remember with a screenshot" — see PersonPresenceTracker/
/// DetectionWorker, which is what actually writes PersonSightingEvent rows. Read-only from this
/// API's side: a report table over a date range, the individual sightings behind one report row
/// (for the drill-down thumbnail gallery), and the saved screenshot itself.
/// </summary>
public static class PersonSightingEndpoints
{
    // GetPersonSightingReportAsync runs ~2 Elasticsearch round-trips per camera per calendar day
    // (see its own comment) rather than one aggregation query — fine for a normal report, but an
    // unbounded range × camera-count product would hammer Elasticsearch with thousands of
    // sequential requests on a single call. Reject early with a clear message instead of just
    // running very slowly.
    private const int MaxCameraDays = 400;

    public static void MapPersonSightingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/person-sightings").RequireAuthorization();

        group.MapGet("/report", async (
            string cameraIds, DateTimeOffset from, DateTimeOffset to,
            HttpContext http, IAccessControlManager accessControl, IMetadataIndexer indexer, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.SearchMetadata); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var codes = cameraIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var days = Math.Max(1, (to.Date - from.Date).Days + 1);
            if (codes.Length == 0)
            {
                return Results.BadRequest("Select at least one camera.");
            }
            if ((long)codes.Length * days > MaxCameraDays)
            {
                return Results.BadRequest($"That's {codes.Length} camera(s) x {days} day(s) — narrow the date range or camera selection (limit is {MaxCameraDays} camera-days per query).");
            }

            var cameraNames = await db.Cameras.AsNoTracking()
                .Where(c => codes.Contains(c.Code))
                .ToDictionaryAsync(c => c.Code, c => c.Name);

            var rows = await indexer.GetPersonSightingReportAsync(codes, from, to);
            return Results.Ok(rows
                .OrderBy(r => r.CameraId).ThenBy(r => r.Date)
                .Select(r => new PersonSightingReportRowDto(
                    r.CameraId, cameraNames.GetValueOrDefault(r.CameraId, r.CameraId), r.Date, r.Total, r.Known, r.Unknown)));
        });

        group.MapGet("/", async (
            string cameraId, DateTimeOffset from, DateTimeOffset to,
            HttpContext http, IAccessControlManager accessControl, IMetadataIndexer indexer) =>
        {
            try { accessControl.Authorize(http.User, Permission.SearchMetadata); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var sightings = await indexer.GetPersonSightingsAsync(cameraId, from, to);
            return Results.Ok(sightings.Select(s => new PersonSightingDto(s.Id, s.CameraId, s.Timestamp, s.PersonName)));
        });

        // Same gate as the report/list above — a screenshot reveals nothing a caller couldn't
        // already see in the report's Known/Unknown counts for the same camera+day.
        group.MapGet("/{id}/thumbnail", async (
            Guid id, HttpContext http, IAccessControlManager accessControl, IMetadataIndexer indexer) =>
        {
            try { accessControl.Authorize(http.User, Permission.SearchMetadata); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var sighting = await indexer.GetPersonSightingAsync(id);
            if (sighting is null || !File.Exists(sighting.ScreenshotPath))
            {
                // Missing file covers retention having cleaned up an old screenshot — an
                // expected gap, not a server error (same treatment as EventThumbnailEndpoints).
                return Results.NotFound();
            }

            return Results.File(await File.ReadAllBytesAsync(sighting.ScreenshotPath), "image/jpeg");
        });

        // Deleting a person's photo record is more sensitive than viewing it — gated one tier up
        // (ManageKnownPersons, Operator+) rather than the plain SearchMetadata view gate above,
        // same reasoning ArchiveEndpoints uses for delete vs. view.
        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext http, IAccessControlManager accessControl, IMetadataIndexer indexer, IAuditLogger auditLogger) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageKnownPersons); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var sighting = await indexer.GetPersonSightingAsync(id);
            if (sighting is null)
            {
                return Results.NotFound();
            }

            var deleted = await indexer.DeletePersonSightingAsync(id);
            if (!deleted)
            {
                return Results.NotFound();
            }

            if (File.Exists(sighting.ScreenshotPath))
            {
                File.Delete(sighting.ScreenshotPath);
            }

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "DeletePersonSighting",
                TargetType = nameof(PersonSightingEvent),
                TargetId = id.ToString(),
                Details = $"cameraId={sighting.CameraId}, personName={sighting.PersonName ?? "unknown"}"
            });

            return Results.NoContent();
        });
    }
}
