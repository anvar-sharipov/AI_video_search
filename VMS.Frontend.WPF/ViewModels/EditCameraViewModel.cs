using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class EditCameraViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public string Code { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _ipAddress;
    [ObservableProperty] private int _onvifPort;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event Action? Updated;

    public EditCameraViewModel(ApiClient api, CameraDto camera)
    {
        _api = api;
        Code = camera.Code;
        _name = camera.Name;
        _ipAddress = camera.IpAddress;
        _onvifPort = camera.OnvifPort;
        _isEnabled = camera.IsEnabled;
    }

    [RelayCommand]
    private async Task SaveAsync(object? passwordBoxParameter)
    {
        var password = passwordBoxParameter as string ?? string.Empty;
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(IpAddress))
        {
            ErrorMessage = LocalizationService.Get("EditCamera_RequiredError");
            return;
        }

        IsBusy = true;
        try
        {
            await _api.UpdateCameraAsync(Code, new UpdateCameraRequestDto(
                string.IsNullOrWhiteSpace(Name) ? Code : Name, IpAddress, OnvifPort, Username, password, IsEnabled, null));
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
