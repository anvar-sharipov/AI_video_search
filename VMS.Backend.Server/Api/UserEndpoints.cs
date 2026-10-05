using Microsoft.EntityFrameworkCore;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Backend.Server.Api;

/// <summary>
/// User account CRUD — gated behind Permission.ManageUsers (Admin+). Creating/editing a
/// SuperAdmin account additionally requires the caller themselves to be SuperAdmin (mirrors
/// DeleteImmutableArchive's precedent: Admin doesn't automatically get the top tier). A few
/// self-service/lockout guards exist because this is the one CRUD screen where a careless
/// action can lock every admin out of the system — none of the other management screens
/// (cameras, known persons) have an equivalent risk.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageUsers);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
            return Results.Ok(users.Select(ToDto));
        });

        group.MapPost("/", async (
            CreateUserRequest request,
            HttpContext http,
            IAccessControlManager accessControl,
            IAuthService auth,
            VmsDbContext db,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageUsers);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest("Username and password are required.");
            }

            if (!Enum.TryParse<UserRole>(request.Role, out var role))
            {
                return Results.BadRequest($"Unknown role '{request.Role}'.");
            }

            var callerRole = http.User.GetRole();
            if (role == UserRole.SuperAdmin && callerRole != UserRole.SuperAdmin)
            {
                return Results.Forbid();
            }

            if (await db.Users.AnyAsync(u => u.Username == request.Username))
            {
                return Results.Conflict($"Username '{request.Username}' is already taken.");
            }

            if (!TryResolveAdditionalPermissions(request.AdditionalPermissions, http.User, accessControl, out var additional, out var permissionError))
            {
                return Results.BadRequest(permissionError);
            }

            var user = new User
            {
                Username = request.Username.Trim(),
                PasswordHash = auth.HashPassword(request.Password),
                Role = role
            };
            user.SetAdditionalPermissions(additional);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "CreateUser",
                TargetType = nameof(User),
                TargetId = user.Id.ToString(),
                Details = $"username={user.Username}, role={user.Role}, additionalPermissions={user.AdditionalPermissionsCsv}"
            });

            return Results.Ok(ToDto(user));
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateUserRequest request,
            HttpContext http,
            IAccessControlManager accessControl,
            IAuthService auth,
            VmsDbContext db,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageUsers);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            if (!Enum.TryParse<UserRole>(request.Role, out var role))
            {
                return Results.BadRequest($"Unknown role '{request.Role}'.");
            }

            var user = await db.Users.FindAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var callerRole = http.User.GetRole();
            if ((role == UserRole.SuperAdmin || user.Role == UserRole.SuperAdmin) && callerRole != UserRole.SuperAdmin)
            {
                return Results.Forbid();
            }

            var callerId = http.User.GetUserId();
            var demotingOrDeactivatingSelf = id == callerId && (role != user.Role || !request.IsActive);
            if (demotingOrDeactivatingSelf)
            {
                return Results.BadRequest("You cannot change your own role or deactivate your own account.");
            }

            var losingLastSuperAdmin = user.Role == UserRole.SuperAdmin && user.IsActive
                && (role != UserRole.SuperAdmin || !request.IsActive);
            if (losingLastSuperAdmin)
            {
                var otherActiveSuperAdmins = await db.Users.CountAsync(u =>
                    u.Id != id && u.Role == UserRole.SuperAdmin && u.IsActive);
                if (otherActiveSuperAdmins == 0)
                {
                    return Results.BadRequest("Cannot demote or deactivate the last remaining SuperAdmin.");
                }
            }

            if (!TryResolveAdditionalPermissions(request.AdditionalPermissions, http.User, accessControl, out var additional, out var permissionError))
            {
                return Results.BadRequest(permissionError);
            }

            user.Role = role;
            user.IsActive = request.IsActive;
            user.SetAdditionalPermissions(additional);
            if (!string.IsNullOrWhiteSpace(request.NewPassword))
            {
                user.PasswordHash = auth.HashPassword(request.NewPassword);
            }

            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = callerId,
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "UpdateUser",
                TargetType = nameof(User),
                TargetId = user.Id.ToString(),
                Details = $"username={user.Username}, role={user.Role}, isActive={user.IsActive}, additionalPermissions={user.AdditionalPermissionsCsv}"
                    + (string.IsNullOrWhiteSpace(request.NewPassword) ? "" : ", passwordChanged=true")
            });

            return Results.Ok(ToDto(user));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IAccessControlManager accessControl,
            VmsDbContext db,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageUsers);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var user = await db.Users.FindAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (user.Role == UserRole.SuperAdmin && http.User.GetRole() != UserRole.SuperAdmin)
            {
                return Results.Forbid();
            }

            if (id == http.User.GetUserId())
            {
                return Results.BadRequest("You cannot delete your own account.");
            }

            if (user.Role == UserRole.SuperAdmin)
            {
                var otherActiveSuperAdmins = await db.Users.CountAsync(u =>
                    u.Id != id && u.Role == UserRole.SuperAdmin && u.IsActive);
                if (otherActiveSuperAdmins == 0)
                {
                    return Results.BadRequest("Cannot delete the last remaining SuperAdmin.");
                }
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "DeleteUser",
                TargetType = nameof(User),
                TargetId = user.Id.ToString(),
                Details = $"username={user.Username}"
            });

            return Results.NoContent();
        });
    }

    private static UserDto ToDto(User u) => new(
        u.Id, u.Username, u.Role.ToString(), u.IsActive, u.CreatedAt, u.LastLoginAt,
        u.GetAdditionalPermissions().Select(p => p.ToString()).ToList());

    /// <summary>
    /// Parses the requested extra-permission names and enforces the two safety rules for this
    /// feature: (1) DeleteImmutableArchive can never be granted this way (Role-only, see
    /// AccessControlManager.NonGrantablePermissions), and (2) a caller can only hand out a
    /// permission they themselves currently hold — no privilege escalation via this endpoint.
    /// </summary>
    private static bool TryResolveAdditionalPermissions(
        List<string>? requested, System.Security.Claims.ClaimsPrincipal caller, IAccessControlManager accessControl,
        out List<Permission> resolved, out string? error)
    {
        resolved = [];
        error = null;

        if (requested is null || requested.Count == 0)
        {
            return true;
        }

        foreach (var name in requested)
        {
            if (!Enum.TryParse<Permission>(name, out var permission))
            {
                error = $"Unknown permission '{name}'.";
                return false;
            }

            if (AccessControlManager.NonGrantablePermissions.Contains(permission))
            {
                error = $"'{permission}' can only come from the Role hierarchy — it can't be granted individually.";
                return false;
            }

            if (!accessControl.HasPermission(caller, permission))
            {
                error = $"You can't grant '{permission}' to someone else — you don't have it yourself.";
                return false;
            }

            resolved.Add(permission);
        }

        return true;
    }
}
