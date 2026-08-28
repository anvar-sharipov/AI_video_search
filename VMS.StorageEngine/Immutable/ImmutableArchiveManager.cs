using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.StorageEngine.Immutable;

/// <summary>
/// Write-protection for archive clips: a Windows ACL Deny rule on Delete (for
/// Everyone) plus the ReadOnly attribute. Two mechanisms because either one alone
/// is trivially bypassed (clearing ReadOnly is a one-liner; the ACL alone doesn't
/// stop accidental overwrites) — the spec calls for both.
///
/// Deleting a protected file always goes through <see cref="Delete"/>, which asks
/// VMS.Core's <see cref="IAccessControlManager"/> for the authorization decision —
/// this class never decides who is allowed to delete, only how protection works.
/// </summary>
public class ImmutableArchiveManager(ILogger logger)
{
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);

    public void Protect(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var acl = fileInfo.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(
            Everyone, FileSystemRights.Delete, AccessControlType.Deny));
        fileInfo.SetAccessControl(acl);

        File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.ReadOnly);

        logger.LogInformation("Archive file protected (immutable): {Path}", filePath);
    }

    public bool IsProtected(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        if ((File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0)
        {
            return true;
        }

        var acl = new FileInfo(filePath).GetAccessControl();
        return acl.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(r => r.IdentityReference == Everyone
                      && r.AccessControlType == AccessControlType.Deny
                      && r.FileSystemRights.HasFlag(FileSystemRights.Delete));
    }

    /// <summary>
    /// Deletes an archive file, enforcing the same rule everywhere in the system:
    /// only SuperAdmin may delete a protected/immutable file; Admin+ may delete a
    /// standard one. Throws <see cref="UnauthorizedAccessException"/> if denied.
    /// </summary>
    public void Delete(string filePath, UserRole requesterRole, IAccessControlManager accessControl)
    {
        var isProtected = IsProtected(filePath);
        var permission = isProtected ? Permission.DeleteImmutableArchive : Permission.DeleteStandardArchive;

        accessControl.Authorize(requesterRole, permission);

        if (isProtected)
        {
            RemoveProtection(filePath);
        }

        File.Delete(filePath);
        logger.LogInformation("Archive file deleted by role {Role} (was protected: {WasProtected}): {Path}", requesterRole, isProtected, filePath);
    }

    private void RemoveProtection(string filePath)
    {
        File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);

        var fileInfo = new FileInfo(filePath);
        var acl = fileInfo.GetAccessControl();
        acl.RemoveAccessRule(new FileSystemAccessRule(
            Everyone, FileSystemRights.Delete, AccessControlType.Deny));
        fileInfo.SetAccessControl(acl);
    }
}
