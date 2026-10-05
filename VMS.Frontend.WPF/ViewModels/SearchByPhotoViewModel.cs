using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Backs the "search by photo" dialog: pick a reference photo, submit it, hand the hits back to MainViewModel's existing SearchResults panel.</summary>
public partial class SearchByPhotoViewModel(ApiClient api) : ObservableObject
{
    [ObservableProperty] private string? _photoPath;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _enrollName = string.Empty;
    [ObservableProperty] private string _enrollMessage = string.Empty;
    [ObservableProperty] private bool _isEnrolling;

    /// <summary>Fired once the server returns matches — the window closes and MainViewModel.ApplySearchByFaceResults populates the shared results list.</summary>
    public event Action<List<SearchResultDto>>? Found;

    public void SetPhoto(string path)
    {
        PhotoPath = path;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(PhotoPath))
        {
            ErrorMessage = LocalizationService.Get("SearchByPhoto_ChooseFileFirstError");
            return;
        }

        IsBusy = true;
        try
        {
            var results = await api.SearchByFaceAsync(PhotoPath);
            Found?.Invoke(results);
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

    /// <summary>Enrolls the currently-loaded photo under a name, so future matching faces get tagged with it (see KnownPersonMatcher) — makes "search by name" possible without re-uploading a photo each time.</summary>
    [RelayCommand]
    private async Task EnrollAsync()
    {
        EnrollMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(PhotoPath))
        {
            EnrollMessage = LocalizationService.Get("SearchByPhoto_ChooseFileFirstError");
            return;
        }

        if (string.IsNullOrWhiteSpace(EnrollName))
        {
            EnrollMessage = LocalizationService.Get("SearchByPhoto_EnrollNameRequiredError");
            return;
        }

        IsEnrolling = true;
        try
        {
            var person = await api.RegisterKnownPersonAsync(EnrollName.Trim(), PhotoPath);
            EnrollMessage = LocalizationService.Get("SearchByPhoto_EnrollSuccess", person.Name);
            EnrollName = string.Empty;
        }
        catch (ApiException ex)
        {
            EnrollMessage = ex.Message;
        }
        finally
        {
            IsEnrolling = false;
        }
    }
}
