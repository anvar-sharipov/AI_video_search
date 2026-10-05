using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VMS.Backend.Server.Orchestration;
using VMS.Backend.Server.Startup;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.MediaEngine.Onvif;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;
using VMS.MetadataIndexer.Search;

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

        // The People Counting line editor (and anything else that just wants a quick still) uses
        // this instead of pulling a live RTSP frame — SnapshotSampler already refreshes this file
        // every few seconds for the detection pipeline, so it's essentially free to serve.
        group.MapGet("/{code}/snapshot", (string code, IOptions<VmsOptions> vmsOptions) =>
        {
            var path = Path.Combine(vmsOptions.Value.SnapshotsRootPath, code, "latest.jpg");
            return File.Exists(path) ? Results.File(File.ReadAllBytes(path), "image/jpeg") : Results.NotFound();
        });

        // Live known/unknown overlay: the last few seconds of "person" detections for this
        // camera, with each BoundingBox normalized to fractional (0..1) coordinates against the
        // current snapshot's own resolution — the WPF grid tile doesn't know (and shouldn't need
        // to know) the source frame's pixel size, only the on-screen VideoView's. Explicitly
        // permission-gated (unlike /snapshot above) because the response carries PersonName —
        // an enrolled person's identity — same gate as /api/people-count.
        group.MapGet("/{code}/detections", async (
            string code, HttpContext http, IAccessControlManager accessControl, IOptions<VmsOptions> vmsOptions, IMetadataIndexer indexer) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var snapshotPath = Path.Combine(vmsOptions.Value.SnapshotsRootPath, code, "latest.jpg");
            if (!File.Exists(snapshotPath))
            {
                return Results.Ok(Array.Empty<LiveDetectionDto>());
            }

            var (width, height) = ThumbnailCropper.ReadDimensions(snapshotPath);
            if (width <= 0 || height <= 0)
            {
                return Results.Ok(Array.Empty<LiveDetectionDto>());
            }

            var events = await indexer.SearchAsync(new SearchQuery(
                ObjectType: "person", CameraId: code, From: DateTimeOffset.Now.AddSeconds(-3), Size: 30));

            return Results.Ok(events.Select(e => new LiveDetectionDto(
                e.BoundingBox.X / width, e.BoundingBox.Y / height,
                e.BoundingBox.Width / width, e.BoundingBox.Height / height,
                e.PersonName)));
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
                accessControl.Authorize(http.User, Permission.ManageCameras);
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

        group.MapPut("/{code}", async (
            string code,
            UpdateCameraRequest request,
            HttpContext http,
            VmsDbContext db,
            IAccessControlManager accessControl,
            CameraOrchestrator orchestrator,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageCameras);
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

            camera.Name = request.Name;
            camera.IpAddress = request.IpAddress;
            camera.OnvifPort = request.OnvifPort;
            camera.RetentionPolicyId = request.RetentionPolicyId;
            camera.IsEnabled = request.IsEnabled;
            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                camera.OnvifUsername = request.Username;
            }
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                camera.OnvifPasswordPlaintext = request.Password;
            }
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "UpdateCamera",
                TargetType = nameof(Camera),
                TargetId = camera.Id.ToString(),
                Details = $"code={camera.Code}, ip={camera.IpAddress}, enabled={camera.IsEnabled}"
            });

            // Always restart the pipeline rather than diffing which fields actually changed —
            // simplest way to guarantee a changed IP/port/credential takes effect immediately.
            await orchestrator.StopCameraAsync(code);
            if (camera.IsEnabled)
            {
                try
                {
                    await orchestrator.OnboardCameraAsync(camera);
                }
                catch (Exception ex)
                {
                    return Results.Ok(new
                    {
                        Camera = new CameraDto(camera.Id, camera.Code, camera.Name, camera.IpAddress, camera.OnvifPort, camera.IsEnabled, false),
                        OnboardWarning = ex.Message
                    });
                }
            }

            return Results.Ok(new CameraDto(camera.Id, camera.Code, camera.Name, camera.IpAddress, camera.OnvifPort, camera.IsEnabled,
                orchestrator.RunningCameraCodes.Contains(camera.Code)));
        });

        group.MapGet("/discover", async (
            HttpContext http,
            VmsDbContext db,
            IAccessControlManager accessControl,
            OnvifDiscoveryClient discoveryClient) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageCameras);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var existingIps = await db.Cameras.Select(c => c.IpAddress).ToListAsync();
            var devices = await discoveryClient.DiscoverAsync();

            return Results.Ok(devices
                .Where(d => !existingIps.Contains(d.IpAddress))
                .Select(d => new DiscoveredDeviceDto(d.IpAddress, d.OnvifPort, d.Model)));
        });

        group.MapPost("/discover/add", async (
            AddDiscoveredCameraRequest request,
            HttpContext http,
            VmsDbContext db,
            IAccessControlManager accessControl,
            OnvifCameraDiscoveryService discovery,
            CameraOrchestrator orchestrator,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageCameras);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            // NVR/DVR channels share one physical IP but become separate Camera rows —
            // nothing in the schema or CameraOrchestrator assumes one IP maps to one camera.
            var sanitizedIp = request.IpAddress.Replace('.', '-');
            var created = new List<CameraDto>();
            var warnings = new List<string>();

            IReadOnlyList<CameraChannelInfo> channels;
            try
            {
                channels = await discovery.GetChannelsAsync(request.IpAddress, request.Username, request.Password, request.OnvifPort);
            }
            catch (Exception ex)
            {
                // Same fallback as the manual POST / flow: save one row now (channel count is
                // unknown since we couldn't connect to enumerate it) so the credentials aren't
                // lost and onboarding can be retried later without re-entering them, instead of
                // discarding the whole attempt just because the device isn't reachable *right now*.
                if (await db.Cameras.AnyAsync(c => c.Code == sanitizedIp))
                {
                    return Results.BadRequest($"Could not connect to {request.IpAddress}: {ex.Message}");
                }

                var fallbackCamera = new Camera
                {
                    Code = sanitizedIp,
                    Name = request.Name,
                    IpAddress = request.IpAddress,
                    OnvifPort = request.OnvifPort,
                    OnvifUsername = request.Username,
                    OnvifPasswordPlaintext = request.Password,
                    RetentionPolicyId = request.RetentionPolicyId,
                    IsEnabled = true
                };
                db.Cameras.Add(fallbackCamera);
                await db.SaveChangesAsync();

                await auditLogger.AppendAsync(new AuditLogEntry
                {
                    UserId = http.User.GetUserId(),
                    Username = http.User.Identity?.Name ?? "unknown",
                    Action = "CreateCameraFromDiscovery",
                    TargetType = nameof(Camera),
                    TargetId = fallbackCamera.Id.ToString(),
                    Details = $"code={fallbackCamera.Code}, ip={fallbackCamera.IpAddress}"
                });

                return Results.Ok(new
                {
                    Cameras = new[] { new CameraDto(fallbackCamera.Id, fallbackCamera.Code, fallbackCamera.Name, fallbackCamera.IpAddress, fallbackCamera.OnvifPort, fallbackCamera.IsEnabled, false) },
                    Warnings = new[] { ex.Message }
                });
            }

            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                var code = channels.Count == 1 ? sanitizedIp : $"{sanitizedIp}-ch{i + 1}";
                if (await db.Cameras.AnyAsync(c => c.Code == code))
                {
                    warnings.Add($"Camera with code '{code}' already exists — skipped.");
                    continue;
                }

                var camera = new Camera
                {
                    Code = code,
                    Name = channels.Count == 1 ? request.Name : $"{request.Name} Ch{i + 1}",
                    IpAddress = request.IpAddress,
                    OnvifPort = request.OnvifPort,
                    OnvifUsername = request.Username,
                    OnvifPasswordPlaintext = request.Password,
                    OnvifVideoSourceToken = channel.VideoSourceToken,
                    RetentionPolicyId = request.RetentionPolicyId,
                    IsEnabled = true
                };
                db.Cameras.Add(camera);
                await db.SaveChangesAsync();

                await auditLogger.AppendAsync(new AuditLogEntry
                {
                    UserId = http.User.GetUserId(),
                    Username = http.User.Identity?.Name ?? "unknown",
                    Action = "CreateCameraFromDiscovery",
                    TargetType = nameof(Camera),
                    TargetId = camera.Id.ToString(),
                    Details = $"code={camera.Code}, ip={camera.IpAddress}"
                });

                var running = true;
                try
                {
                    await orchestrator.OnboardCameraAsync(camera);
                }
                catch (Exception ex)
                {
                    running = false;
                    warnings.Add($"{camera.Code}: {ex.Message}");
                }

                created.Add(new CameraDto(camera.Id, camera.Code, camera.Name, camera.IpAddress, camera.OnvifPort, camera.IsEnabled, running));
            }

            return Results.Ok(new { Cameras = created, Warnings = warnings });
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
                accessControl.Authorize(http.User, Permission.ManageCameras);
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
