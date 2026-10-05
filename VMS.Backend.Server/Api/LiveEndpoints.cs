using VMS.Backend.Server.Orchestration;
using VMS.Core.Security;
using VMS.MediaEngine.Capture;

namespace VMS.Backend.Server.Api;

/// <summary>
/// HLS live view for off-LAN clients (phone app, any future web client) — see
/// <see cref="LiveHlsRelayManager"/> for why this exists alongside the WPF client's
/// direct-RTSP-via-LibVLC path instead of replacing it.
/// </summary>
public static class LiveEndpoints
{
    public static void MapLiveEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/live").RequireAuthorization();

        group.MapGet("/{code}/index.m3u8", async (
            string code, HttpContext http, IAccessControlManager accessControl,
            CameraOrchestrator orchestrator, LiveHlsRelayManager relayManager) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            var status = orchestrator.GetStatus(code);
            if (status is null)
            {
                return Results.NotFound("Camera is not running.");
            }

            var rtspUri = string.IsNullOrEmpty(status.SubStreamUri) ? status.MainStreamUri : status.SubStreamUri;
            var playlistPath = await relayManager.EnsureStartedAsync(code, rtspUri, http.RequestAborted);
            if (playlistPath is null)
            {
                return Results.Problem("Live relay did not produce a playlist in time.", statusCode: 503);
            }

            return Results.File(File.ReadAllBytes(playlistPath), "application/vnd.apple.mpegurl");
        });

        group.MapGet("/{code}/{segment}", (
            string code, string segment, HttpContext http, IAccessControlManager accessControl, LiveHlsRelayManager relayManager) =>
        {
            try { accessControl.Authorize(http.User, Permission.ViewLiveStream); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }

            if (!segment.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || segment.Contains('/') || segment.Contains('\\'))
            {
                return Results.NotFound();
            }

            var path = Path.Combine(relayManager.GetOutputDir(code), segment);
            if (!File.Exists(path))
            {
                return Results.NotFound();
            }

            relayManager.Touch(code);
            return Results.File(File.OpenRead(path), "video/mp2t");
        });
    }
}
