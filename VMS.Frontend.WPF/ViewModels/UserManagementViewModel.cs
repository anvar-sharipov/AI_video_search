using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class UserRowViewModel(UserDto user) : ObservableObject
{
    public UserDto User { get; } = user;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Backs the User Management screen (Admin+ only — Permission.ManageUsers) — mirrors CameraManagementViewModel's checkbox-row-select + Edit/Delete pattern.</summary>
public partial class UserManagementViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<UserRowViewModel> Users { get; } = [];

    [ObservableProperty] private ICollectionView _usersView = null!;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>Raised when exactly one user is checked and Edit is clicked; the Window's code-behind owns opening EditUserWindow.</summary>
    public event Action<UserDto>? EditRequested;

    public void InitializeView()
    {
        UsersView = CollectionViewSource.GetDefaultView(Users);
        UsersView.Filter = FilterUser;
    }

    partial void OnSearchTextChanged(string value) => UsersView?.Refresh();

    private bool FilterUser(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var row = (UserRowViewModel)obj;
        return row.User.Username.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = LocalizationService.Get("UserManagement_Status_Loading");
        try
        {
            var users = await api.GetUsersAsync();
            Users.Clear();
            foreach (var u in users)
            {
                Users.Add(new UserRowViewModel(u));
            }
            StatusMessage = LocalizationService.Get("UserManagement_Status_Loaded", Users.Count);
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void EditSelected()
    {
        var selected = Users.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("UserManagement_SelectUserFirst");
            return;
        }
        if (selected.Count > 1)
        {
            StatusMessage = LocalizationService.Get("UserManagement_SelectOneForEdit");
            return;
        }

        EditRequested?.Invoke(selected[0].User);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = Users.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("UserManagement_SelectUserFirst");
            return;
        }

        var confirmed = MessageBox.Show(
            LocalizationService.Get("UserManagement_ConfirmDeleteText", selected.Count),
            LocalizationService.Get("UserManagement_ConfirmDeleteTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var row in selected)
            {
                await api.DeleteUserAsync(row.User.Id);
                Users.Remove(row);
            }
            StatusMessage = LocalizationService.Get("UserManagement_Status_Loaded", Users.Count);
        }
        catch (ApiException ex)
        {
            // A guard rejection (self-delete, last SuperAdmin) lands here as an ApiException —
            // some rows in the batch may already have been removed before the rejected one.
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReloadAsync() => await LoadAsync();
}
