using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class AddCameraViewModel(ApiClient api)
    : ObservableObject
{
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _ipAddress = string.Empty;
    [ObservableProperty] private int _onvifPort = 80;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event Action? Created;

    [RelayCommand]
    private async Task SaveAsync(object? passwordBoxParameter)
    {
        var password = passwordBoxParameter as string ?? string.Empty;
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(IpAddress))
        {
            ErrorMessage = LocalizationService.Get("AddCamera_RequiredError");
            return;
        }

        IsBusy = true;
        try
        {
            await api.CreateCameraAsync(new CreateCameraRequestDto(
                Code, string.IsNullOrWhiteSpace(Name) ? Code : Name, IpAddress, OnvifPort, Username, password, null));
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
