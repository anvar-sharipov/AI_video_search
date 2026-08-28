using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api;
    private readonly LibVLC _libVlc;

    public SessionService Session { get; }

    public ObservableCollection<CameraTileViewModel> Cameras { get; } = [];
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public event Action<SearchResultViewModel>? ClipRequested;
    public event Action? AddCameraRequested;

    public MainViewModel(ApiClient api, SessionService session)
    {
        _api = api;
        Session = session;
        _libVlc = new LibVLC();
    }

    [RelayCommand]
    public async Task LoadCamerasAsync()
    {
        IsBusy = true;
        StatusMessage = "Загрузка камер...";
        try
        {
            foreach (var tile in Cameras)
            {
                tile.Dispose();
            }
            Cameras.Clear();

            var cameras = await _api.GetCamerasAsync();
            foreach (var camera in cameras)
            {
                var tile = new CameraTileViewModel(_libVlc, camera);
                Cameras.Add(tile);

                var status = await _api.GetCameraStatusAsync(camera.Code);
                if (status is not null)
                {
                    tile.PlayStream(status.SubStreamUri);
                }
            }

            StatusMessage = $"Камер: {Cameras.Count}";
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
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Поиск...";
        try
        {
            SearchResults.Clear();
            var results = await _api.SearchAsync(SearchText);
            foreach (var r in results)
            {
                SearchResults.Add(new SearchResultViewModel(r));
            }
            StatusMessage = $"Найдено: {results.Count}";
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
    private void AddCamera()
    {
        AddCameraRequested?.Invoke();
    }

    public void Dispose()
    {
        foreach (var tile in Cameras)
        {
            tile.Dispose();
        }
        _libVlc.Dispose();
    }
}
