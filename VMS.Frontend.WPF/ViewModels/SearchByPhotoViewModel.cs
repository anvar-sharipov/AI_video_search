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
}
