namespace VMS.Core.Domain;

/// <summary>
/// Role hierarchy is strictly ordered: SuperAdmin > Admin > Operator > Viewer > Guard.
/// AccessControlManager relies on this numeric ordering for "at least" checks.
///
/// Guard = -1 (not 0, with everyone else renumbered up) deliberately: Role is persisted as a
/// plain integer column (see VmsDbContext/migrations — no enum-to-string conversion configured),
/// so every existing user row already has a numeric Role baked in. Inserting Guard below the
/// existing values instead of renumbering keeps every already-stored row meaning exactly what it
/// meant before — renumbering would have silently reassigned everyone's role on the next login.
/// </summary>
public enum UserRole
{
    /// <summary>Below Viewer: live camera video only (Permission.ViewLiveStream), no search/archive/export — a monitor-wall guard, not an investigator.</summary>
    Guard = -1,
    Viewer = 0,
    Operator = 1,
    Admin = 2,
    SuperAdmin = 3
}
