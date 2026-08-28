namespace VMS.Core.Domain;

/// <summary>
/// Role hierarchy is strictly ordered: SuperAdmin > Admin > Operator > Viewer.
/// AccessControlManager relies on this numeric ordering for "at least" checks.
/// </summary>
public enum UserRole
{
    Viewer = 0,
    Operator = 1,
    Admin = 2,
    SuperAdmin = 3
}
