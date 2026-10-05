using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class EditUserViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly SessionService _session;
    private readonly IAccessControlManager _accessControl = new AccessControlManager();
    private readonly Guid _id;

    public string Username { get; }
    public List<string> RoleOptions { get; } = ["Guard", "Viewer", "Operator", "Admin", "SuperAdmin"];

    /// <summary>Only functions the CALLER themselves currently has — same "can't grant what you don't hold" rule as AddUserViewModel.</summary>
    public ObservableCollection<PermissionOptionViewModel> PermissionOptions { get; }

    [ObservableProperty] private string _selectedRole;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event Action? Updated;

    public EditUserViewModel(ApiClient api, SessionService session, UserDto user)
    {
        _api = api;
        _session = session;
        _id = user.Id;
        Username = user.Username;
        _selectedRole = user.Role;
        _isActive = user.IsActive;

        PermissionOptions = new ObservableCollection<PermissionOptionViewModel>(
            PermissionOptionViewModel.BuildAll().Where(o => _session.HasPermission(o.Permission)));
        foreach (var option in PermissionOptions)
        {
            if (user.AdditionalPermissions.Contains(option.Permission.ToString()))
            {
                option.IsChecked = true;
            }
        }
        RecomputeLocks();
    }

    partial void OnSelectedRoleChanged(string value) => RecomputeLocks();

    /// <summary>Same reasoning as AddUserViewModel.RecomputeLocks: boxes the selected Role already grants are shown checked-and-locked.</summary>
    private void RecomputeLocks()
    {
        var role = Enum.TryParse<UserRole>(SelectedRole, out var r) ? r : UserRole.Viewer;
        foreach (var option in PermissionOptions)
        {
            option.IsLocked = _accessControl.HasPermission(role, option.Permission);
            if (option.IsLocked)
            {
                option.IsChecked = true;
            }
        }
    }

    /// <summary>New password is optional here — blank leaves the existing password unchanged, same convention as EditCameraViewModel's ONVIF password field.</summary>
    [RelayCommand]
    private async Task SaveAsync(object? passwordBoxParameter)
    {
        var newPassword = passwordBoxParameter as string ?? string.Empty;
        ErrorMessage = string.Empty;

        IsBusy = true;
        try
        {
            var additional = PermissionOptions.Where(o => o.IsChecked && !o.IsLocked)
                .Select(o => o.Permission.ToString()).ToList();
            await _api.UpdateUserAsync(_id, new UpdateUserRequestDto(
                SelectedRole, IsActive, string.IsNullOrWhiteSpace(newPassword) ? null : newPassword, additional));
            Updated?.Invoke();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
