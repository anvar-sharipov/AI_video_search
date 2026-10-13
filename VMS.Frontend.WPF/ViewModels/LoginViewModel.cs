using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class LoginViewModel(ApiClient api, SessionService session, LocalizationService localization, ThemeService theme) : ObservableObject
{
    public ThemeService Theme => theme;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public event Action? LoginSucceeded;

    [RelayCommand]
    private async Task LoginAsync(object? passwordBoxParameter)
    {
        var password = passwordBoxParameter as string ?? string.Empty;
        ErrorMessage = string.Empty;
        IsBusy = true;

        try
        {
            var result = await api.LoginAsync(Username, password);
            api.SetToken(result.Token, result.ExpiresAt, result.RefreshToken);
            session.ApplyLogin(result);
            LoginSucceeded?.Invoke();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception)
        {
            ErrorMessage = LocalizationService.Get("Login_ConnectError");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetLanguage(string languageCode) => localization.SetLanguage(languageCode);

    [RelayCommand]
    private void ToggleTheme() => theme.ToggleTheme();
}
