using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class CameraGroupsWindow : Window
{
    private readonly ApiClient _api;

    public CameraGroupsWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    /// <summary>Same one-click-toggle fix as CameraManagementWindow's checkbox column.</summary>
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
        if (DataContext is CameraGroupsViewModel vm)
        {
            vm.EditRequested += OnEditRequested;
            await vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        OpenEditWindow(null);
    }

    private void OnEditRequested(CameraGroupDto group)
    {
        OpenEditWindow(group);
    }

    private void OpenEditWindow(CameraGroupDto? group)
    {
        var editVm = new EditCameraGroupViewModel(_api, group);
        var window = new EditCameraGroupWindow { Owner = this, DataContext = editVm };

        editVm.Saved += async () =>
        {
            window.Close();
            if (DataContext is CameraGroupsViewModel groupsVm)
            {
                await groupsVm.ReloadAsync();
            }
        };

        window.ShowDialog();
    }
}
