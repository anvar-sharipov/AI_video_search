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
        [Permission.ViewLiveStream] = UserRole.Viewer,
        [Permission.ViewArchive] = UserRole.Viewer,
        [Permission.SearchMetadata] = UserRole.Viewer,

        // Search-by-photo tracks a specific person's movements across the archive —
        // more sensitive than a plain text/color search, so it's gated like ExportClip.
        [Permission.SearchByFace] = UserRole.Operator,
        [Permission.ExportClip] = UserRole.Operator,
        [Permission.ManageCameras] = UserRole.Admin,
        [Permission.ManageSystemConfig] = UserRole.Admin,
        [Permission.ManageUsers] = UserRole.Admin,
        [Permission.ManageRetentionPolicy] = UserRole.Admin,
        [Permission.DeleteStandardArchive] = UserRole.Admin,

        // Immutable/write-protected archives are the one permission Admin does NOT
        // get automatically — only SuperAdmin can override write-protection.
        [Permission.DeleteImmutableArchive] = UserRole.SuperAdmin
    };

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
}
