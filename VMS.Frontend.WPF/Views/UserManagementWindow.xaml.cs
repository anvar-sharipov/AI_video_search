using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class UserManagementWindow : Window
{
    private readonly ApiClient _api;
    private readonly SessionService _session;

    public UserManagementWindow(ApiClient api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    /// <summary>Same one-click-toggle fix as CameraManagementWindow's checkbox column — see there for the full rationale.</summary>
    private void OnCheckBoxCellPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not DataGridCell)
        {
            dep = VisualTreeHelper.GetParent(dep);
        }

        if (dep is DataGridCell { IsEditing: false } cell && FindVisualChild<CheckBox>(cell) is not null)
        {
            cell.Focus();
            ((DataGrid)sender).BeginEdit(e);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }
            if (FindVisualChild<T>(child) is T found)
            {
                return found;
            }
        }
        return null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is UserManagementViewModel vm)
        {
            vm.InitializeView();
            vm.EditRequested += OnEditRequested;
            await vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var addVm = new AddUserViewModel(_api, _session);
        var window = new AddUserWindow { Owner = this, DataContext = addVm };

        addVm.Created += async () =>
        {
            window.Close();
            if (DataContext is UserManagementViewModel mgmtVm)
            {
                await mgmtVm.ReloadAsync();
            }
        };

        window.ShowDialog();
    }

    private void OnEditRequested(UserDto user)
    {
        var editVm = new EditUserViewModel(_api, _session, user);
        var window = new EditUserWindow { Owner = this, DataContext = editVm };

        editVm.Updated += async () =>
        {
            window.Close();
            if (DataContext is UserManagementViewModel mgmtVm)
            {
                await mgmtVm.ReloadAsync();
            }
        };

        window.ShowDialog();
    }
}
