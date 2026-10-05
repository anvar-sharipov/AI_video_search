using System.Security.Claims;
using VMS.Core.Domain;

namespace VMS.Core.Security;

/// <summary>
/// Reads identity/role/permission claims off a logged-in JWT principal. Lives in VMS.Core (not
/// Backend.Server, where the JWT is actually issued) so AccessControlManager's ClaimsPrincipal
/// overloads can use it directly — ClaimsPrincipal itself is a plain BCL type (System.Security.
/// Claims), not an ASP.NET-specific one, so this doesn't pull a web dependency into Core.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public const string RoleClaimType = ClaimTypes.Role;

    /// <summary>Comma-separated Permission names granted to this user on top of their Role — see User.AdditionalPermissionsCsv, encoded into the JWT at login so the server doesn't need a DB round trip per request to know them.</summary>
    public const string PermissionsClaimType = "vms_permissions";

    public static UserRole GetRole(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(RoleClaimType)?.Value;
        return Enum.TryParse<UserRole>(value, out var role) ? role : UserRole.Viewer;
    }

    // "sub" is JwtRegisteredClaimNames.Sub's literal value — hardcoded rather than referencing
    // System.IdentityModel.Tokens.Jwt so this lean Core project doesn't need that package.
    private const string SubjectClaimType = "sub";

    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(SubjectClaimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static List<Permission> GetGrantedPermissions(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(PermissionsClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Enum.TryParse<Permission>(s.Trim(), out var p) ? (Permission?)p : null)
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToList();
    }
}
