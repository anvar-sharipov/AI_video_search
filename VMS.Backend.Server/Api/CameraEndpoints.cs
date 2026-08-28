using Microsoft.EntityFrameworkCore;
using VMS.Backend.Server.Orchestration;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

public static class CameraEndpoints
{
    public static void MapCameraEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cameras").RequireAuthorization();

        group.MapGet("/", async (VmsDbContext db, CameraOrchestrator orchestrator) =>
        {
            var cameras = await db.Cameras.AsNoTracking().ToListAsync();
            var running = orchestrator.RunningCameraCodes;

            return Results.Ok(cameras.Select(c => new CameraDto(
                c.Id, c.Code, c.Name, c.IpAddress, c.OnvifPort, c.IsEnabled, running.Contains(c.Code))));
        });

        group.MapGet("/{code}/status", (string code, CameraOrchestrator orchestrator) =>
        {
            var status = orchestrator.GetStatus(code);
            return status is null ? Results.NotFound() : Results.Ok(status);
        });

        group.MapPost("/", async (
            CreateCameraRequest request,
            HttpContext http,
            VmsDbContext db,
            IAccessControlManager accessControl,
            CameraOrchestrator orchestrator,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.ManageCameras);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            if (await db.Cameras.AnyAsync(c => c.Code == request.Code))
            {
                return Results.Conflict($"A camera with code '{request.Code}' already exists.");
            }

            var camera = new Camera
            {
                Code = request.Code,
                Name = request.Name,
                IpAddress = request.IpAddress,
                OnvifPort = request.OnvifPort,
                OnvifUsername = request.Username,
                OnvifPasswordPlaintext = request.Password,
                RetentionPolicyId = request.RetentionPolicyId,
                IsEnabled = true
            };
            db.Cameras.Add(camera);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "CreateCamera",
                TargetType = nameof(Camera),
                TargetId = camera.Id.ToString(),
                Details = $"code={camera.Code}, ip={camera.IpAddress}"
            });

            try
            {
                await orchestrator.OnboardCameraAsync(camera);
            }
            catch (Exception ex)
            {
                // Camera row is saved either way — onboarding can be retried without re-entering credentials.
                return Results.Ok(new
                {
                    Camera = new CameraDto(camera.Id, camera.Code, camera.Name, camera.IpAddress, camera.OnvifPort, camera.IsEnabled, false),
                    OnboardWarning = ex.Message
                });
            }

            return Results.Created($"/api/cameras/{camera.Code}/status",
                new CameraDto(camera.Id, camera.Code, camera.Name, camera.IpAddress, camera.OnvifPort, camera.IsEnabled, true));
        });

        group.MapDelete("/{code}", async (
            string code,
            HttpContext http,
            VmsDbContext db,
            IAccessControlManager accessControl,
            CameraOrchestrator orchestrator,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.ManageCameras);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var camera = await db.Cameras.FirstOrDefaultAsync(c => c.Code == code);
            if (camera is null)
            {
                return Results.NotFound();
            }

            await orchestrator.StopCameraAsync(code);
            db.Cameras.Remove(camera);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "DeleteCamera",
                TargetType = nameof(Camera),
                TargetId = camera.Id.ToString(),
                Details = $"code={camera.Code}"
            });

            return Results.NoContent();
        });
    }
}
