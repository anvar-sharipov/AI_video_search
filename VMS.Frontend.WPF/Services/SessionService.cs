using CommunityToolkit.Mvvm.ComponentModel;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.Services;

/// <summary>
/// Holds the logged-in user's identity for the process lifetime and answers UI
/// gating questions via the same AccessControlManager the backend uses — one
/// rule set, so the client never drifts from what the server will actually allow.
/// </summary>
public partial class SessionService : ObservableObject
{
    private readonly IAccessControlManager _accessControl = new AccessControlManager();

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private UserRole _role = UserRole.Viewer;

    [ObservableProperty]
    private bool _isLoggedIn;

    /// <summary>Individual permissions granted to this user on top of their Role — see User.AdditionalPermissionsCsv/AccessControlManager's additive-grant overloads. Empty until SetLoggedIn.</summary>
    public List<Permission> AdditionalPermissions { get; private set; } = [];

    public bool CanManageCameras => HasPermission(Permission.ManageCameras);
    public bool CanExportClip => HasPermission(Permission.ExportClip);
    public bool CanSearchByFace => HasPermission(Permission.SearchByFace);
    public bool CanViewAuditLog => HasPermission(Permission.ManageSystemConfig);
    public bool CanDeleteImmutableArchive => HasPermission(Permission.DeleteImmutableArchive);
    public bool CanDeleteStandardArchive => HasPermission(Permission.DeleteStandardArchive);
    public bool CanManageUsers => HasPermission(Permission.ManageUsers);

    /// <summary>Effective check (Role baseline OR an additional grant) — the same rule AccessControlManager enforces server-side, so UI gating never disagrees with what the server will actually allow.</summary>
    public bool HasPermission(Permission permission) => _accessControl.HasPermission(Role, AdditionalPermissions, permission);

    public void SetLoggedIn(string username, UserRole role, IEnumerable<Permission>? additionalPermissions = null)
    {
        Username = username;
        Role = role;
        AdditionalPermissions = additionalPermissions?.ToList() ?? [];
        IsLoggedIn = true;
        OnPropertyChanged(nameof(CanManageCameras));
        OnPropertyChanged(nameof(CanExportClip));
        OnPropertyChanged(nameof(CanSearchByFace));
        OnPropertyChanged(nameof(CanViewAuditLog));
        OnPropertyChanged(nameof(CanDeleteImmutableArchive));
        OnPropertyChanged(nameof(CanDeleteStandardArchive));
        OnPropertyChanged(nameof(CanManageUsers));
    }

    public void LogOut()
    {
        Username = string.Empty;
        Role = UserRole.Viewer;
        AdditionalPermissions = [];
        IsLoggedIn = false;
    }
}
