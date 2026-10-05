using CommunityToolkit.Mvvm.ComponentModel;
using VMS.Core.Security;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>
/// One row in the Add/Edit User "functions" checklist — see AddUserViewModel/EditUserViewModel.
/// IsChecked reflects whether this user gets the permission at all (via Role or an additional
/// grant); IsLocked means the currently-selected Role already grants it, so the box is shown
/// checked-and-disabled rather than lying about it being optional.
/// </summary>
public partial class PermissionOptionViewModel(Permission permission, string displayName) : ObservableObject
{
    public Permission Permission { get; } = permission;
    public string DisplayName { get; } = displayName;

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isLocked;

    /// <summary>All every permission has, in the order shown to the user, excluding AccessControlManager.NonGrantablePermissions (currently just DeleteImmutableArchive — Role-only, never an additional grant, see that constant's own doc).</summary>
    public static List<PermissionOptionViewModel> BuildAll()
    {
        Permission[] order =
        [
            Permission.ViewLiveStream, Permission.ViewArchive, Permission.SearchMetadata, Permission.SearchByFace,
            Permission.ManageKnownPersons, Permission.ExportClip, Permission.ManageCameras,
            Permission.ManageRetentionPolicy, Permission.DeleteStandardArchive,
            Permission.ManageSystemConfig, Permission.ManageUsers
        ];

        return order
            .Where(p => !AccessControlManager.NonGrantablePermissions.Contains(p))
            .Select(p => new PermissionOptionViewModel(p, LocalizationService.Get($"Permission_{p}")))
            .ToList();
    }
}
