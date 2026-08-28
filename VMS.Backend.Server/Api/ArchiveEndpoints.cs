using Microsoft.Extensions.Options;
using VMS.Backend.Server.Startup;
using VMS.Core.Auditing;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.StorageEngine.Immutable;

namespace VMS.Backend.Server.Api;

/// <summary>Direct archive file management — protect/unprotect/delete, gated by AccessControlManager.</summary>
public static class ArchiveEndpoints
{
    public static void MapArchiveEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/archive").RequireAuthorization();

        group.MapPost("/protect", (
            string cameraId,
            string fileName,
            HttpContext http,
            IAccessControlManager accessControl,
            ImmutableArchiveManager immutableManager,
            IOptions<VmsOptions> vmsOptions) =>
        {
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.ManageCameras);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var path = ResolveArchivePath(vmsOptions.Value.ArchiveRootPath, cameraId, fileName);
            if (path is null || !File.Exists(path))
            {
                return Results.NotFound();
            }

            immutableManager.Protect(path);
            return Results.Ok(new { Protected = true });
        });

        group.MapDelete("/", async (
            string cameraId,
            string fileName,
            HttpContext http,
            IAccessControlManager accessControl,
            ImmutableArchiveManager immutableManager,
            IOptions<VmsOptions> vmsOptions,
            IAuditLogger auditLogger) =>
        {
            // Baseline gate before existence is even checked, so a role that can never
            // delete anything (Viewer/Operator) always gets 403, not a 404 that would
            // leak whether the file exists. The finer "protected files need SuperAdmin"
            // check happens inside immutableManager.Delete once we know the file exists.
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.DeleteStandardArchive);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var path = ResolveArchivePath(vmsOptions.Value.ArchiveRootPath, cameraId, fileName);
            if (path is null || !File.Exists(path))
            {
                return Results.NotFound();
            }

            try
            {
                immutableManager.Delete(path, http.User.GetRole(), accessControl);
            }
            catch (UnauthorizedAccessException ex)
            {
                await auditLogger.AppendAsync(new AuditLogEntry
                {
                    UserId = http.User.GetUserId(),
                    Username = http.User.Identity?.Name ?? "unknown",
                    Action = "DeleteArchiveFile",
                    TargetType = "ArchiveFile",
                    TargetId = path,
                    IsSuccess = false,
                    Details = ex.Message
                });
                return Results.Forbid();
            }

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "DeleteArchiveFile",
                TargetType = "ArchiveFile",
                TargetId = path
            });

            return Results.NoContent();
        });
    }

    private static string? ResolveArchivePath(string archiveRoot, string cameraId, string fileName)
    {
        var root = Path.GetFullPath(Path.Combine(archiveRoot, cameraId));
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}
