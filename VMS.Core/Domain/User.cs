using VMS.Core.Security;

namespace VMS.Core.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Individual permissions granted to this user ON TOP OF whatever their Role already gives
    /// (e.g. a Viewer who's also been granted SearchByFace, without promoting them all the way
    /// to Operator) — comma-separated Permission enum names, or null for "no extras". Deliberately
    /// additive-only: the effective permission set is never smaller than the Role's own baseline,
    /// so this can never accidentally take away something the Role already grants. See
    /// AccessControlManager.HasPermission(UserRole, IReadOnlyCollection&lt;Permission&gt;, Permission)
    /// and UserEndpoints, which also enforces that a caller can only grant a permission they
    /// themselves currently hold (no privilege escalation via this mechanism).
    /// </summary>
    public string? AdditionalPermissionsCsv { get; set; }

    public List<Permission> GetAdditionalPermissions() =>
        string.IsNullOrWhiteSpace(AdditionalPermissionsCsv)
            ? []
            : AdditionalPermissionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Enum.TryParse<Permission>(s.Trim(), out var p) ? (Permission?)p : null)
                .Where(p => p is not null)
                .Select(p => p!.Value)
                .ToList();

    public void SetAdditionalPermissions(IEnumerable<Permission> permissions) =>
        AdditionalPermissionsCsv = string.Join(',', permissions.Distinct());
}
