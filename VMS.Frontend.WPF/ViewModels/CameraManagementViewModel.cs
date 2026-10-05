using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class CameraRowViewModel(CameraDto camera) : ObservableObject
{
    public CameraDto Camera { get; } = camera;

    [ObservableProperty] private bool _isSelected;
}

public partial class DiscoveredDeviceRowViewModel(DiscoveredDeviceDto device) : ObservableObject
{
    public DiscoveredDeviceDto Device { get; } = device;

    [ObservableProperty] private bool _isSelected;
}

public partial class CameraManagementViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<CameraRowViewModel> Cameras { get; } = [];
    public ObservableCollection<DiscoveredDeviceRowViewModel> OnlineDevices { get; } = [];

    [ObservableProperty] private ICollectionView _camerasView = null!;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isDiscoveryBusy;
    [ObservableProperty] private string _discoveryStatusMessage = string.Empty;

    /// <summary>Fired after any successful add/edit/delete so MainWindow can refresh the live-view grid.</summary>
    public event Action? CamerasChanged;

    /// <summary>Raised when exactly one camera is checked and Edit is clicked; MainWindow's code-behind owns opening EditCameraWindow.</summary>
    public event Action<CameraDto>? EditRequested;

    /// <summary>Raised when exactly one online device is checked and Add is clicked; MainWindow's code-behind owns opening AddDiscoveredCameraWindow.</summary>
    public event Action<DiscoveredDeviceDto>? AddDiscoveredRequested;

    public void InitializeView()
    {
        CamerasView = CollectionViewSource.GetDefaultView(Cameras);
        CamerasView.Filter = FilterCamera;
    }

    partial void OnSearchTextChanged(string value) => CamerasView?.Refresh();

    private bool FilterCamera(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var row = (CameraRowViewModel)obj;
        var needle = SearchText.Trim();
        return row.Camera.Code.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || row.Camera.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || row.Camera.IpAddress.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = LocalizationService.Get("CameraManagement_Status_Loading");
        try
        {
            var cameras = await api.GetCamerasAsync();
            Cameras.Clear();
            foreach (var c in cameras)
            {
                Cameras.Add(new CameraRowViewModel(c));
            }
            StatusMessage = LocalizationService.Get("CameraManagement_Status_Loaded", Cameras.Count);
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
        var selected = Cameras.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("CameraManagement_SelectCameraFirst");
            return;
        }
        if (selected.Count > 1)
        {
            StatusMessage = LocalizationService.Get("CameraManagement_SelectOneForEdit");
            return;
        }

        EditRequested?.Invoke(selected[0].Camera);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = Cameras.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("CameraManagement_SelectCameraFirst");
            return;
        }

        var confirmed = MessageBox.Show(
            LocalizationService.Get("CameraManagement_ConfirmDeleteMultipleText", selected.Count),
            LocalizationService.Get("CameraManagement_ConfirmDeleteTitle"),
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
                await api.DeleteCameraAsync(row.Camera.Code);
                Cameras.Remove(row);
            }
            StatusMessage = LocalizationService.Get("CameraManagement_Status_Loaded", Cameras.Count);
            CamerasChanged?.Invoke();
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

    public async Task ReloadAndNotifyAsync()
    {
        await LoadAsync();
        CamerasChanged?.Invoke();
        await RefreshDiscoveryAsync();
    }

    [RelayCommand]
    private async Task RefreshDiscoveryAsync()
    {
        IsDiscoveryBusy = true;
        DiscoveryStatusMessage = LocalizationService.Get("CameraManagement_Status_Discovering");
        try
        {
            var devices = await api.DiscoverCamerasAsync();
            OnlineDevices.Clear();
            foreach (var d in devices)
            {
                OnlineDevices.Add(new DiscoveredDeviceRowViewModel(d));
            }
            DiscoveryStatusMessage = LocalizationService.Get("CameraManagement_Status_DiscoveredCount", OnlineDevices.Count);
        }
        catch (ApiException ex)
        {
            DiscoveryStatusMessage = ex.Message;
        }
        finally
        {
            IsDiscoveryBusy = false;
        }
    }

    [RelayCommand]
    private void AddSelectedDiscovered()
    {
        var selected = OnlineDevices.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            DiscoveryStatusMessage = LocalizationService.Get("CameraManagement_SelectDeviceFirst");
            return;
        }
        if (selected.Count > 1)
        {
            DiscoveryStatusMessage = LocalizationService.Get("CameraManagement_SelectOneDeviceToAdd");
            return;
        }

        AddDiscoveredRequested?.Invoke(selected[0].Device);
    }
}
