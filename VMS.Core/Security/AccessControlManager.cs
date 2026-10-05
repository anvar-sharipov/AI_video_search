using System.Security.Claims;
using VMS.Core.Domain;

namespace VMS.Core.Security;

/// <summary>
/// Single source of truth for role -> permission checks. Every module that needs
/// an authorization decision (Backend.Server API, StorageEngine delete paths,
/// Frontend.WPF UI gating) goes through this instead of re-implementing role logic.
/// </summary>
public class AccessControlManager : IAccessControlManager
{
    // Minimum role required for each permission. UserRole is intentionally ordered
    // (Viewer < Operator < Admin < SuperAdmin) so "at least" checks are a single comparison.
    private static readonly Dictionary<Permission, UserRole> MinimumRole = new()
    {
        // The one permission Guard has — a monitor-wall role with no search/archive/export rights.
        [Permission.ViewLiveStream] = UserRole.Guard,
        [Permission.ViewArchive] = UserRole.Viewer,
        [Permission.SearchMetadata] = UserRole.Viewer,

        // Search-by-photo tracks a specific person's movements across the archive —
        // more sensitive than a plain text/color search, so it's gated like ExportClip.
        [Permission.SearchByFace] = UserRole.Operator,

        // Enrolling a known person creates a standing face-match rule that silently tags every
        // future matching detection — at least as sensitive as a one-off SearchByFace lookup.
        [Permission.ManageKnownPersons] = UserRole.Operator,
        [Permission.ExportClip] = UserRole.Operator,
        [Permission.ManageCameras] = UserRole.Admin,
        [Permission.ManageSystemConfig] = UserRole.Admin,
        [Permission.ManageUsers] = UserRole.Admin,
        [Permission.ManageRetentionPolicy] = UserRole.Admin,
        [Permission.DeleteStandardArchive] = UserRole.Admin,

        // Immutable/write-protected archives are the one permission Admin does NOT get
        // automatically — only SuperAdmin can override write-protection. Deliberately excluded
        // from the per-user additional-grants mechanism below too (see HasPermission's
        // ClaimsPrincipal overload doc) — the single most destructive permission in the system
        // stays role-only, no additive-grant escape hatch, even a well-intentioned one.
        [Permission.DeleteImmutableArchive] = UserRole.SuperAdmin
    };

    /// <summary>Permissions that can never be handed out via a per-user additional grant — only the Role hierarchy itself confers them. Currently just the one permission that can permanently destroy write-protected evidence.</summary>
    public static readonly IReadOnlySet<Permission> NonGrantablePermissions = new HashSet<Permission> { Permission.DeleteImmutableArchive };

    public bool HasPermission(UserRole role, Permission permission)
    {
        return role >= MinimumRole[permission];
    }

    public void Authorize(UserRole role, Permission permission)
    {
        if (!HasPermission(role, permission))
        {
            throw new UnauthorizedAccessException(
                $"Role '{role}' does not have permission '{permission}' (requires at least '{MinimumRole[permission]}').");
        }
    }

    public bool HasPermission(UserRole role, IReadOnlyCollection<Permission> additionalGrants, Permission permission)
    {
        return HasPermission(role, permission) || additionalGrants.Contains(permission);
    }

    public void Authorize(UserRole role, IReadOnlyCollection<Permission> additionalGrants, Permission permission)
    {
        if (!HasPermission(role, additionalGrants, permission))
        {
            throw new UnauthorizedAccessException(
                $"Role '{role}' (plus any additional grants) does not have permission '{permission}' (requires at least '{MinimumRole[permission]}').");
        }
    }

    public bool HasPermission(ClaimsPrincipal user, Permission permission)
    {
        return HasPermission(user.GetRole(), user.GetGrantedPermissions(), permission);
    }

    public void Authorize(ClaimsPrincipal user, Permission permission)
    {
        Authorize(user.GetRole(), user.GetGrantedPermissions(), permission);
    }
}
