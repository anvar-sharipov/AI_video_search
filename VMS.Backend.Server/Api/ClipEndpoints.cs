using Microsoft.Extensions.Options;
using VMS.Backend.Server.Startup;
using VMS.Core.Security;
using VMS.StorageEngine.Clips;

namespace VMS.Backend.Server.Api;

public static class ClipEndpoints
{
    public static void MapClipEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/clips").RequireAuthorization();

        group.MapPost("/", async (
            ClipRequest request,
            HttpContext http,
            IAccessControlManager accessControl,
            VideoClipExtractor extractor) =>
        {
            try
            {
                accessControl.Authorize(http.User.GetRole(), Permission.ExportClip);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            try
            {
                var preRoll = TimeSpan.FromSeconds(request.PreRollSeconds ?? 2);
                var postRoll = TimeSpan.FromSeconds(request.PostRollSeconds ?? 3);
                var path = await extractor.ExtractClipAsync(request.CameraId, request.DetectionTime, preRoll, postRoll);
                return Results.Ok(new ClipResponse(Path.GetFileName(path)));
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ex.Message);
            }
        });

        group.MapGet("/{fileName}", (string fileName, IOptions<VmsOptions> vmsOptions) =>
        {
            IResult result;

            if (fileName.Contains('/') || fileName.Contains('\\'))
            {
                result = Results.NotFound();
                return result;
            }

            var clipsRoot = Path.GetFullPath(vmsOptions.Value.ClipsRootPath);
            var path = Path.GetFullPath(Path.Combine(clipsRoot, fileName));
            if (!path.StartsWith(clipsRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                result = Results.NotFound();
                return result;
            }

            // File(Stream,...), not File(string,...) — the string overload resolves
            // against the content-root virtual file provider, not an arbitrary
            // absolute path, and silently 500s (FileNotFoundException) on a real file.
            var stream = File.OpenRead(path);
            result = Results.File(stream, "video/mp4", enableRangeProcessing: true);
            return result;
        });
    }
}
