using Microsoft.EntityFrameworkCore;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

public record VideoWallCellDto(Guid Id, int Row, int Column, int RowSpan, int ColumnSpan, Guid? CameraId, string? CameraCode, string? CameraName);
public record VideoWallLayoutDto(Guid Id, string Name, int Rows, int Columns, List<VideoWallCellDto> Cells);
public record SaveVideoWallCellRequest(int Row, int Column, int RowSpan, int ColumnSpan, Guid? CameraId);
public record SaveVideoWallLayoutRequest(string Name, int Rows, int Columns, List<SaveVideoWallCellRequest> Cells);

/// <summary>Named, saved Video Wall layouts — a grid of cells (possibly merged into larger
/// rectangles, see VideoWallCell) each optionally showing one camera. See VideoWallLayout.</summary>
public static class VideoWallEndpoints
{
    public static void MapVideoWallEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/video-wall-layouts").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var layouts = await db.VideoWallLayouts.AsNoTracking().OrderBy(l => l.Name).ToListAsync();
            var cells = await db.VideoWallCells.AsNoTracking().ToListAsync();
            var cameras = await db.Cameras.AsNoTracking().ToDictionaryAsync(c => c.Id);

            var result = layouts.Select(l => ToDto(l, cells.Where(c => c.VideoWallLayoutId == l.Id), cameras));

            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var layout = await db.VideoWallLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
            if (layout is null)
            {
                return Results.NotFound();
            }

            var cells = await db.VideoWallCells.AsNoTracking().Where(c => c.VideoWallLayoutId == id).ToListAsync();
            var cameraIds = cells.Where(c => c.CameraId is not null).Select(c => c.CameraId!.Value).ToList();
            var cameras = await db.Cameras.AsNoTracking().Where(c => cameraIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

            return Results.Ok(ToDto(layout, cells, cameras));
        });

        group.MapPost("/", async (
            SaveVideoWallLayoutRequest request, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IAuditLogger auditLogger) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }
            if (request.Rows <= 0 || request.Columns <= 0)
            {
                return Results.BadRequest("Rows and Columns must be positive.");
            }

            var layout = new VideoWallLayout { Name = request.Name.Trim(), Rows = request.Rows, Columns = request.Columns };
            db.VideoWallLayouts.Add(layout);
            AddCells(db, layout.Id, request.Cells);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "CreateVideoWallLayout",
                TargetType = nameof(VideoWallLayout),
                TargetId = layout.Id.ToString(),
                Details = $"name={layout.Name}, rows={layout.Rows}, columns={layout.Columns}"
            });

            return Results.Ok(await ToDtoAsync(db, layout.Id));
        });

        group.MapPut("/{id:guid}", async (
            Guid id, SaveVideoWallLayoutRequest request, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IAuditLogger auditLogger) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }
            if (request.Rows <= 0 || request.Columns <= 0)
            {
                return Results.BadRequest("Rows and Columns must be positive.");
            }

            var layout = await db.VideoWallLayouts.FindAsync(id);
            if (layout is null)
            {
                return Results.NotFound();
            }

            layout.Name = request.Name.Trim();
            layout.Rows = request.Rows;
            layout.Columns = request.Columns;

            // Replacing the whole cell list is simpler and safer than diffing — a wall layout
            // has at most a few dozen cells, so this is cheap.
            db.VideoWallCells.RemoveRange(db.VideoWallCells.Where(c => c.VideoWallLayoutId == id));
            AddCells(db, id, request.Cells);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "UpdateVideoWallLayout",
                TargetType = nameof(VideoWallLayout),
                TargetId = layout.Id.ToString(),
                Details = $"name={layout.Name}, rows={layout.Rows}, columns={layout.Columns}"
            });

            return Results.Ok(await ToDtoAsync(db, id));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IAuditLogger auditLogger) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var layout = await db.VideoWallLayouts.FindAsync(id);
            if (layout is null)
            {
                return Results.NotFound();
            }

            db.VideoWallCells.RemoveRange(db.VideoWallCells.Where(c => c.VideoWallLayoutId == id));
            db.VideoWallLayouts.Remove(layout);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "DeleteVideoWallLayout",
                TargetType = nameof(VideoWallLayout),
                TargetId = layout.Id.ToString(),
                Details = $"name={layout.Name}"
            });

            return Results.NoContent();
        });
    }

    private static void AddCells(VmsDbContext db, Guid layoutId, List<SaveVideoWallCellRequest> cells)
    {
        foreach (var cell in cells)
        {
            db.VideoWallCells.Add(new VideoWallCell
            {
                VideoWallLayoutId = layoutId,
                Row = cell.Row,
                Column = cell.Column,
                RowSpan = cell.RowSpan,
                ColumnSpan = cell.ColumnSpan,
                CameraId = cell.CameraId
            });
        }
    }

    private static VideoWallLayoutDto ToDto(VideoWallLayout layout, IEnumerable<VideoWallCell> cells, Dictionary<Guid, Camera> cameras)
    {
        return new VideoWallLayoutDto(layout.Id, layout.Name, layout.Rows, layout.Columns,
            cells.Select(c => new VideoWallCellDto(
                c.Id, c.Row, c.Column, c.RowSpan, c.ColumnSpan, c.CameraId,
                c.CameraId is { } camId && cameras.TryGetValue(camId, out var cam) ? cam.Code : null,
                c.CameraId is { } camId2 && cameras.TryGetValue(camId2, out var cam2) ? cam2.Name : null))
                .ToList());
    }

    private static async Task<VideoWallLayoutDto> ToDtoAsync(VmsDbContext db, Guid id)
    {
        var layout = await db.VideoWallLayouts.AsNoTracking().FirstAsync(l => l.Id == id);
        var cells = await db.VideoWallCells.AsNoTracking().Where(c => c.VideoWallLayoutId == id).ToListAsync();
        var cameraIds = cells.Where(c => c.CameraId is not null).Select(c => c.CameraId!.Value).ToList();
        var cameras = await db.Cameras.AsNoTracking().Where(c => cameraIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

        return ToDto(layout, cells, cameras);
    }
}
