using VMS.Core.Domain;

namespace VMS.Core.Security;

public interface IAccessControlManager
{
    bool HasPermission(UserRole role, Permission permission);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> if the role lacks the permission.</summary>
    void Authorize(UserRole role, Permission permission);
}
