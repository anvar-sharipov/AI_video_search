using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VMS.Backend.Server.Startup;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

public record EMapDto(Guid Id, string Name, DateTimeOffset CreatedAt);
public record EMapPinDto(Guid Id, Guid EMapId, Guid CameraId, string CameraCode, string CameraName, double X, double Y);
public record CreateEMapPinRequest(Guid CameraId, double X, double Y);

/// <summary>E-map: a floor-plan image with camera pins — click a pin to jump to that camera's live view. See EMap/EMapPin.</summary>
public static class EMapEndpoints
{
    public static void MapEMapEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/emaps").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var maps = await db.EMaps.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
            return Results.Ok(maps.Select(m => new EMapDto(m.Id, m.Name, m.CreatedAt)));
        });

        group.MapPost("/", async (HttpRequest request, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IOptions<VmsOptions> vmsOptions) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var form = await request.ReadFormAsync();
            var name = form["name"].ToString();
            var image = form.Files["image"];
            if (string.IsNullOrWhiteSpace(name) || image is null)
            {
                return Results.BadRequest("Name and image are required.");
            }

            var mapsRoot = vmsOptions.Value.EMapsRootPath;
            Directory.CreateDirectory(mapsRoot);
            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(image.FileName)}";
            await using (var stream = File.Create(Path.Combine(mapsRoot, fileName)))
            {
                await image.CopyToAsync(stream);
            }

            var map = new EMap { Name = name.Trim(), ImageFileName = fileName };
            db.EMaps.Add(map);
            await db.SaveChangesAsync();
            return Results.Ok(new EMapDto(map.Id, map.Name, map.CreatedAt));
        }).DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IOptions<VmsOptions> vmsOptions) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var map = await db.EMaps.FindAsync(id);
            if (map is null)
            {
                return Results.NotFound();
            }

            var pins = db.EMapPins.Where(p => p.EMapId == id);
            db.EMapPins.RemoveRange(pins);
            db.EMaps.Remove(map);
            await db.SaveChangesAsync();

            var imagePath = Path.Combine(vmsOptions.Value.EMapsRootPath, map.ImageFileName);
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }

            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/image", async (Guid id, HttpContext http, IAccessControlManager accessControl, VmsDbContext db, IOptions<VmsOptions> vmsOptions) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var map = await db.EMaps.FindAsync(id);
            if (map is null)
            {
                return Results.NotFound();
            }

            var path = Path.Combine(vmsOptions.Value.EMapsRootPath, map.ImageFileName);
            if (!File.Exists(path))
            {
                return Results.NotFound();
            }

            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "image/jpeg"
            };
            return Results.File(await File.ReadAllBytesAsync(path), contentType);
        });

        group.MapGet("/{id:guid}/pins", async (Guid id, HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var pins = await db.EMapPins.AsNoTracking().Where(p => p.EMapId == id).ToListAsync();
            var cameraIds = pins.Select(p => p.CameraId).ToList();
            var cameras = await db.Cameras.AsNoTracking().Where(c => cameraIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

            return Results.Ok(pins
                .Where(p => cameras.ContainsKey(p.CameraId))
                .Select(p => new EMapPinDto(p.Id, p.EMapId, p.CameraId, cameras[p.CameraId].Code, cameras[p.CameraId].Name, p.X, p.Y)));
        });

        group.MapPost("/{id:guid}/pins", async (Guid id, CreateEMapPinRequest request, HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            if (await db.EMaps.FindAsync(id) is null)
            {
                return Results.NotFound("E-map not found.");
            }

            var camera = await db.Cameras.FindAsync(request.CameraId);
            if (camera is null)
            {
                return Results.NotFound("Camera not found.");
            }

            var pin = new EMapPin { EMapId = id, CameraId = request.CameraId, X = request.X, Y = request.Y };
            db.EMapPins.Add(pin);
            await db.SaveChangesAsync();
            return Results.Ok(new EMapPinDto(pin.Id, pin.EMapId, pin.CameraId, camera.Code, camera.Name, pin.X, pin.Y));
        });

        group.MapDelete("/{id:guid}/pins/{pinId:guid}", async (Guid id, Guid pinId, HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try { accessControl.Authorize(http.User, Permission.ManageCameras); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var pin = await db.EMapPins.FirstOrDefaultAsync(p => p.Id == pinId && p.EMapId == id);
            if (pin is null)
            {
                return Results.NotFound();
            }

            db.EMapPins.Remove(pin);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
