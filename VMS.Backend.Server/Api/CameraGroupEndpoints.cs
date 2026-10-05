using Microsoft.EntityFrameworkCore;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

public record CameraGroupMemberDto(Guid CameraId, string Code, string Name);
public record CameraGroupDto(Guid Id, string Name, List<CameraGroupMemberDto> Members);
public record SaveCameraGroupRequest(string Name, List<Guid> CameraIds);

/// <summary>Named, ordered camera subsets ("screens") for the Live View grid — e.g. cameras 1-5
/// on one view, 6-20 on another. See CameraGroup/CameraGroupMember.</summary>
public static class CameraGroupEndpoints
{
    public static void MapCameraGroupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/camera-groups").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var groups = await db.CameraGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
            var members = await db.CameraGroupMembers.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync();
            var cameras = await db.Cameras.AsNoTracking().ToDictionaryAsync(c => c.Id);

            var result = groups.Select(g => new CameraGroupDto(
                g.Id, g.Name,
                members.Where(m => m.CameraGroupId == g.Id && cameras.ContainsKey(m.CameraId))
                    .Select(m => new CameraGroupMemberDto(m.CameraId, cameras[m.CameraId].Code, cameras[m.CameraId].Name))
                    .ToList()));

            return Results.Ok(result);
        });

        group.MapPost("/", async (
            SaveCameraGroupRequest request, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IAuditLogger auditLogger) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            var cameraGroup = new CameraGroup { Name = request.Name.Trim() };
            db.CameraGroups.Add(cameraGroup);
            AddMembers(db, cameraGroup.Id, request.CameraIds);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "CreateCameraGroup",
                TargetType = nameof(CameraGroup),
                TargetId = cameraGroup.Id.ToString(),
                Details = $"name={cameraGroup.Name}, cameraCount={request.CameraIds.Count}"
            });

            return Results.Ok(await ToDtoAsync(db, cameraGroup.Id));
        });

        group.MapPut("/{id:guid}", async (
            Guid id, SaveCameraGroupRequest request, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IAuditLogger auditLogger) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var cameraGroup = await db.CameraGroups.FindAsync(id);
            if (cameraGroup is null)
            {
                return Results.NotFound();
            }

            cameraGroup.Name = request.Name.Trim();

            // Replacing the whole membership list is simpler and safer than diffing —
            // groups are small (a handful to a few dozen cameras), so this is cheap.
            var existingMembers = db.CameraGroupMembers.Where(m => m.CameraGroupId == id);
            db.CameraGroupMembers.RemoveRange(existingMembers);
            AddMembers(db, id, request.CameraIds);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "UpdateCameraGroup",
                TargetType = nameof(CameraGroup),
                TargetId = cameraGroup.Id.ToString(),
                Details = $"name={cameraGroup.Name}, cameraCount={request.CameraIds.Count}"
            });

            return Results.Ok(await ToDtoAsync(db, id));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IAuditLogger auditLogger) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var cameraGroup = await db.CameraGroups.FindAsync(id);
            if (cameraGroup is null)
            {
                return Results.NotFound();
            }

            db.CameraGroupMembers.RemoveRange(db.CameraGroupMembers.Where(m => m.CameraGroupId == id));
            db.CameraGroups.Remove(cameraGroup);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "DeleteCameraGroup",
                TargetType = nameof(CameraGroup),
                TargetId = cameraGroup.Id.ToString(),
                Details = $"name={cameraGroup.Name}"
            });

            return Results.NoContent();
        });
    }

    private static void AddMembers(VmsDbContext db, Guid cameraGroupId, List<Guid> cameraIds)
    {
        for (var i = 0; i < cameraIds.Count; i++)
        {
            db.CameraGroupMembers.Add(new CameraGroupMember { CameraGroupId = cameraGroupId, CameraId = cameraIds[i], SortOrder = i });
        }
    }

    private static async Task<CameraGroupDto> ToDtoAsync(VmsDbContext db, Guid id)
    {
        var cameraGroup = (await db.CameraGroups.AsNoTracking().FirstAsync(g => g.Id == id));
        var members = await db.CameraGroupMembers.AsNoTracking()
            .Where(m => m.CameraGroupId == id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
        var cameraIds = members.Select(m => m.CameraId).ToList();
        var cameras = await db.Cameras.AsNoTracking().Where(c => cameraIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

        return new CameraGroupDto(cameraGroup.Id, cameraGroup.Name,
            members.Where(m => cameras.ContainsKey(m.CameraId))
                .Select(m => new CameraGroupMemberDto(m.CameraId, cameras[m.CameraId].Code, cameras[m.CameraId].Name))
                .ToList());
    }
}
