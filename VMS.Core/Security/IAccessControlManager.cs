using System.Security.Claims;
using VMS.Core.Domain;

namespace VMS.Core.Security;

public interface IAccessControlManager
{
    /// <summary>Role-only check — the Role's own baseline, ignoring any per-user additional grants.</summary>
    bool HasPermission(UserRole role, Permission permission);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> if the role lacks the permission.</summary>
    void Authorize(UserRole role, Permission permission);

    /// <summary>Effective check: the role's baseline OR one of this user's additional grants (see User.AdditionalPermissionsCsv) — never fewer permissions than the plain role-only check above.</summary>
    bool HasPermission(UserRole role, IReadOnlyCollection<Permission> additionalGrants, Permission permission);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> unless the role or one of the additional grants covers the permission.</summary>
    void Authorize(UserRole role, IReadOnlyCollection<Permission> additionalGrants, Permission permission);

    /// <summary>Convenience overload for API endpoints: reads role + granted-permissions claims off the JWT principal directly.</summary>
    bool HasPermission(ClaimsPrincipal user, Permission permission);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> unless the principal's role or additional grants cover the permission.</summary>
    void Authorize(ClaimsPrincipal user, Permission permission);
}
