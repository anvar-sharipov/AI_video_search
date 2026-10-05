using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class CameraManagementWindow : Window
{
    private readonly ApiClient _api;

    public CameraManagementWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    /// <summary>
    /// A DataGridCheckBoxColumn's checkbox isn't interactive until its cell is in edit mode —
    /// by default that takes a first click just to select/focus the cell and a second to
    /// actually toggle it. Starting the edit on the very same mouse-down the click already
    /// delivered makes one click enough, which is what a checkbox column is expected to do.
    /// </summary>
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
        if (DataContext is CameraManagementViewModel vm)
        {
            vm.InitializeView();
            vm.EditRequested += OnEditRequested;
            vm.AddDiscoveredRequested += OnAddDiscoveredRequested;
            await Task.WhenAll(vm.LoadCommand.ExecuteAsync(null), vm.RefreshDiscoveryCommand.ExecuteAsync(null));
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var addVm = new AddCameraViewModel(_api);
        var window = new AddCameraWindow { Owner = this, DataContext = addVm };

        addVm.Created += async () =>
        {
            window.Close();
            if (DataContext is CameraManagementViewModel mgmtVm)
            {
                await mgmtVm.ReloadAndNotifyAsync();
            }
        };

        window.ShowDialog();
    }

    private void OnEditRequested(CameraDto camera)
    {
        var editVm = new EditCameraViewModel(_api, camera);
        var window = new EditCameraWindow { Owner = this, DataContext = editVm };

        editVm.Updated += async () =>
        {
            window.Close();
            if (DataContext is CameraManagementViewModel mgmtVm)
            {
                await mgmtVm.ReloadAndNotifyAsync();
            }
        };

        window.ShowDialog();
    }

    private void OnAddDiscoveredRequested(DiscoveredDeviceDto device)
    {
        var addVm = new AddDiscoveredCameraViewModel(_api, device);
        var window = new AddDiscoveredCameraWindow { Owner = this, DataContext = addVm };

        addVm.Added += async () =>
        {
            window.Close();
            if (DataContext is CameraManagementViewModel mgmtVm)
            {
                await mgmtVm.ReloadAndNotifyAsync();
            }
        };

        window.ShowDialog();
    }
}
