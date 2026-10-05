using VMS.Core.Auditing;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        // Shows who did what — reuses ManageSystemConfig (Admin+) rather than a new
        // permission: it's the same tier as the other admin-only sidebar entries this
        // button sits next to (system settings, user management), and every action
        // already recorded here (camera add/delete, archive protect/delete, login) is
        // itself gated at Admin+ or higher, so a lower role reading the log would just
        // see entries for actions it can't perform anyway.
        app.MapGet("/api/audit-log", async (
            Guid? userId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpContext http,
            IAccessControlManager accessControl,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageSystemConfig);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var entries = await auditLogger.QueryAsync(userId, from, to);
            return Results.Ok(entries);
        }).RequireAuthorization();
    }
}
