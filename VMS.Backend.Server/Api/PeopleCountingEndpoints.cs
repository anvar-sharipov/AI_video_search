using Microsoft.EntityFrameworkCore;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;

namespace VMS.Backend.Server.Api;

public record CountingLinePointDto(double X, double Y);
public record CountingLineDto(string CameraCode, List<CountingLinePointDto> Points, bool LeftToRightIsIn);
public record SetCountingLineRequest(List<CountingLinePointDto> Points, bool LeftToRightIsIn);
public record PeopleCountDto(string CameraId, DateTimeOffset From, DateTimeOffset To, int In, int Out);

/// <summary>People Counting: a configurable per-camera line (see CameraCountingLine) that DetectionWorker/PeopleCountingTracker turns into In/Out events, queryable over a date range.</summary>
public static class PeopleCountingEndpoints
{
    public static void MapPeopleCountingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/counting-lines").RequireAuthorization();

        group.MapGet("/{cameraCode}", async (string cameraCode, HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var line = await db.CameraCountingLines.AsNoTracking().FirstOrDefaultAsync(l => l.CameraCode == cameraCode);
            return line is null
                ? Results.NotFound()
                : Results.Ok(ToDto(line));
        });

        group.MapPut("/{cameraCode}", async (
            string cameraCode, SetCountingLineRequest request, HttpContext http,
            IAccessControlManager accessControl, VmsDbContext db, ICountingLineProvider countingLineProvider) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            if (request.Points.Count < 2)
            {
                return Results.BadRequest("A counting line needs at least 2 points.");
            }

            var points = request.Points.Select(p => (p.X, p.Y)).ToList();
            var line = await db.CameraCountingLines.FirstOrDefaultAsync(l => l.CameraCode == cameraCode);
            if (line is null)
            {
                line = new CameraCountingLine { CameraCode = cameraCode, PointsJson = "[]", LeftToRightIsIn = request.LeftToRightIsIn };
                line.SetPoints(points);
                db.CameraCountingLines.Add(line);
            }
            else
            {
                line.SetPoints(points);
                line.LeftToRightIsIn = request.LeftToRightIsIn;
            }

            await db.SaveChangesAsync();
            countingLineProvider.Invalidate();
            return Results.Ok(ToDto(line));
        });

        group.MapDelete("/{cameraCode}", async (
            string cameraCode, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, ICountingLineProvider countingLineProvider) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var line = await db.CameraCountingLines.FirstOrDefaultAsync(l => l.CameraCode == cameraCode);
            if (line is null)
            {
                return Results.NotFound();
            }

            db.CameraCountingLines.Remove(line);
            await db.SaveChangesAsync();
            countingLineProvider.Invalidate();
            return Results.NoContent();
        });

        app.MapGet("/api/people-count", async (
            string cameraId, DateTimeOffset from, DateTimeOffset to,
            HttpContext http, IAccessControlManager accessControl, IMetadataIndexer indexer) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var (inCount, outCount) = await indexer.GetPeopleCountAsync(cameraId, from, to);
            return Results.Ok(new PeopleCountDto(cameraId, from, to, inCount, outCount));
        }).RequireAuthorization();
    }

    private static CountingLineDto ToDto(CameraCountingLine line) =>
        new(line.CameraCode, line.GetPoints().Select(p => new CountingLinePointDto(p.X, p.Y)).ToList(), line.LeftToRightIsIn);
}
