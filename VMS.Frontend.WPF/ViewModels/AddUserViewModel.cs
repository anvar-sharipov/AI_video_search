using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class AddUserViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly SessionService _session;
    private readonly IAccessControlManager _accessControl = new AccessControlManager();

    public List<string> RoleOptions { get; } = ["Guard", "Viewer", "Operator", "Admin", "SuperAdmin"];

    /// <summary>Only functions the CALLER themselves currently has — you can't hand out a permission you don't hold (see UserEndpoints.TryResolveAdditionalPermissions, enforced again server-side).</summary>
    public ObservableCollection<PermissionOptionViewModel> PermissionOptions { get; }

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _selectedRole = "Viewer";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event Action? Created;

    public AddUserViewModel(ApiClient api, SessionService session)
    {
        _api = api;
        _session = session;
        PermissionOptions = new ObservableCollection<PermissionOptionViewModel>(
            PermissionOptionViewModel.BuildAll().Where(o => _session.HasPermission(o.Permission)));
        RecomputeLocks();
    }

    partial void OnSelectedRoleChanged(string value) => RecomputeLocks();

    /// <summary>Boxes the selected Role already grants are shown checked-and-locked (not a real choice — unchecking them wouldn't do anything, since Role alone already covers it); every other box is the operator's free choice, added as an additional grant on save.</summary>
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

    [RelayCommand]
    private async Task SaveAsync(object? passwordBoxParameter)
    {
        var password = passwordBoxParameter as string ?? string.Empty;
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = LocalizationService.Get("AddUser_RequiredError");
            return;
        }

        IsBusy = true;
        try
        {
            var additional = PermissionOptions.Where(o => o.IsChecked && !o.IsLocked)
                .Select(o => o.Permission.ToString()).ToList();
            await _api.CreateUserAsync(new CreateUserRequestDto(Username.Trim(), password, SelectedRole, additional));
            Created?.Invoke();
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
