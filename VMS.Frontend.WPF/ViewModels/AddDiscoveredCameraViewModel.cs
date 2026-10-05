using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class AddDiscoveredCameraViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly DiscoveredDeviceDto _device;

    public string IpAddress => _device.IpAddress;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event Action? Added;

    public AddDiscoveredCameraViewModel(ApiClient api, DiscoveredDeviceDto device)
    {
        _api = api;
        _device = device;
        _name = device.Model ?? device.IpAddress;
    }

    [RelayCommand]
    private async Task SaveAsync(object? passwordBoxParameter)
    {
        var password = passwordBoxParameter as string ?? string.Empty;
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = LocalizationService.Get("AddDiscoveredCamera_RequiredError");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.AddDiscoveredCameraAsync(new AddDiscoveredCameraRequestDto(
                _device.IpAddress, _device.OnvifPort, string.IsNullOrWhiteSpace(Name) ? _device.IpAddress : Name,
                Username, password, null));
            Added?.Invoke();
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
