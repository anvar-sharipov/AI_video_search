using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>
/// Backs the Face Recognition screen: enrolled known persons (reuses the same registry
/// SearchByPhotoWindow enrolls into) and a feed of recent "person" detections that matched one
/// of them — i.e. every search hit that has a PersonName, which is exactly what
/// KnownPersonMatcher/DetectionWorker tag in real time as cameras run.
/// </summary>
public partial class FaceRecognitionViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<KnownPersonDto> KnownPersons { get; } = [];
    public ObservableCollection<SearchResultViewModel> RecognizedHits { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public event Action<SearchResultViewModel>? ClipRequested;

    public FaceRecognitionViewModel(ApiClient api)
    {
        _api = api;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            KnownPersons.Clear();
            foreach (var p in await _api.GetKnownPersonsAsync())
            {
                KnownPersons.Add(p);
            }

            RecognizedHits.Clear();
            var results = await _api.SearchAsync("person");
            foreach (var r in results.Where(r => r.PersonName is not null))
            {
                RecognizedHits.Add(new SearchResultViewModel(r));
            }

            StatusMessage = string.Empty;
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
    private void ViewClip(SearchResultViewModel? result)
    {
        if (result is not null)
        {
            ClipRequested?.Invoke(result);
        }
    }

    [RelayCommand]
    private async Task DeleteKnownPersonAsync(KnownPersonDto? person)
    {
        if (person is null)
        {
            return;
        }

        try
        {
            await _api.DeleteKnownPersonAsync(person.Id);
            KnownPersons.Remove(person);
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
