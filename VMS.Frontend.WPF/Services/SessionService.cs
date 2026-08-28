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

    public bool CanManageCameras => _accessControl.HasPermission(Role, Permission.ManageCameras);
    public bool CanExportClip => _accessControl.HasPermission(Role, Permission.ExportClip);
    public bool CanDeleteImmutableArchive => _accessControl.HasPermission(Role, Permission.DeleteImmutableArchive);
    public bool CanDeleteStandardArchive => _accessControl.HasPermission(Role, Permission.DeleteStandardArchive);

    public void SetLoggedIn(string username, UserRole role)
    {
        Username = username;
        Role = role;
        IsLoggedIn = true;
        OnPropertyChanged(nameof(CanManageCameras));
        OnPropertyChanged(nameof(CanExportClip));
        OnPropertyChanged(nameof(CanDeleteImmutableArchive));
        OnPropertyChanged(nameof(CanDeleteStandardArchive));
    }

    public void LogOut()
    {
        Username = string.Empty;
        Role = UserRole.Viewer;
        IsLoggedIn = false;
    }
}
