using System.Collections.ObjectModel;
using System.IO;
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
    private readonly LibVLC _expandLibVlc;

    public SessionService Session { get; }
    public LocalizationService Localization { get; }

    public ObservableCollection<CameraTileViewModel> Cameras { get; } = [];
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    /// <summary>
    /// UniformGrid.Rows/Columns: 0 means "auto, size from item count" (the original behavior);
    /// 1/2/3/4 forces an NxN layout, per the layout-switcher toolbar.
    /// </summary>
    [ObservableProperty]
    private int _gridSize;

    /// <summary>The single-clicked "active" tile — highlighted, not the same as the fullscreen popup (see ToggleExpand).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTile))]
    private CameraTileViewModel? _selectedTile;

    /// <summary>Drives IsEnabled on the bottom toolbar's per-tile buttons (snapshot/record/pause/fullscreen).</summary>
    public bool HasSelectedTile => SelectedTile is not null;

    /// <summary>Which top-level screen is showing — Control Panel (dashboard of feature cards) or Live View (the camera grid).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlPanelActive))]
    private bool _isLiveViewActive;

    public bool IsControlPanelActive => !IsLiveViewActive;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public event Action<SearchResultViewModel>? ClipRequested;
    public event Action? AddCameraRequested;
    public event Action<CameraTileViewModel>? ExpandRequested;
    public event Action? SearchByPhotoRequested;
    public event Action? OperationLogRequested;
    public event Action? ArchiveRequested;

    public MainViewModel(ApiClient api, SessionService session, LocalizationService localization)
    {
        _api = api;
        Session = session;
        Localization = localization;
        // Hardware HEVC decode (D3D11VA) via LibVLC shares one small, fixed-size GPU decode
        // surface pool across every simultaneous stream in the process — 3 grid tiles alone
        // already exhausted it ("not enough decoding slices in the texture (6/28)"), well before
        // ever expanding one to main quality. Software decode has no such shared-pool ceiling;
        // for a handful of live-view tiles it costs CPU, not correctness.
        _libVlc = new LibVLC("--verbose=2", "--avcodec-hw=none");
        _libVlc.Log += OnLibVlcLog;

        // The fullscreen single-camera popup gets its own LibVLC instance (own D3D11 device),
        // entirely separate from the grid's shared one — see ExpandedCameraViewModel.
        _expandLibVlc = new LibVLC("--verbose=2", "--avcodec-hw=none");
        _expandLibVlc.Log += OnLibVlcLog;
    }

    /// <summary>
    /// Diagnostic only: LibVLC's own decode/network errors never surface anywhere else
    /// (a failed stream just renders as a blank tile with no .NET exception), so this is
    /// the only way to see *why* a specific camera's video didn't come up.
    /// </summary>
    private static void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        if (e.Level is LogLevel.Warning or LogLevel.Error)
        {
            try
            {
                File.AppendAllText("libvlc.log", $"{DateTime.Now:O} [{e.Level}] {e.Module}: {e.Message}\n");
            }
            catch
            {
                // logging must never itself throw
            }
        }
    }

    [RelayCommand]
    public async Task LoadCamerasAsync()
    {
        IsBusy = true;
        StatusMessage = LocalizationService.Get("Status_LoadingCameras");
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
                    tile.SetStreamUris(status.MainStreamUri, status.SubStreamUri);
                }
            }

            StatusMessage = LocalizationService.Get("Status_CamerasCount", Cameras.Count);
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
        StatusMessage = LocalizationService.Get("Status_Searching");
        try
        {
            SearchResults.Clear();
            var results = await _api.SearchAsync(SearchText);
            foreach (var r in results)
            {
                SearchResults.Add(new SearchResultViewModel(r));
            }
            StatusMessage = LocalizationService.Get("Status_FoundCount", results.Count);
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

    /// <summary>Single click on a tile's header: marks it the "active" tile (highlighted border) — distinct from double-clicking/expanding to fullscreen.</summary>
    [RelayCommand]
    private void SelectTile(CameraTileViewModel? tile)
    {
        if (SelectedTile is not null)
        {
            SelectedTile.IsSelected = false;
        }

        SelectedTile = tile;

        if (tile is not null)
        {
            tile.IsSelected = true;
        }
    }

    /// <summary>NxN layout, or 0 for auto — set from the layout-switcher toolbar.</summary>
    [RelayCommand]
    private void SetGridSize(string sizeText)
    {
        GridSize = int.Parse(sizeText);
    }

    [RelayCommand]
    private void SetLanguage(string languageCode) => Localization.SetLanguage(languageCode);

    [RelayCommand]
    private void ShowControlPanel() => IsLiveViewActive = false;

    [RelayCommand]
    private void ShowLiveView() => IsLiveViewActive = true;

    /// <summary>
    /// The tile's small "expand" button opens that camera fullscreen at main-stream quality, in
    /// its own popup window (see ExpandedCameraViewModel for why it's a separate window/player
    /// rather than reusing the grid tile's own VideoView). The grid keeps playing underneath
    /// untouched.
    /// </summary>
    [RelayCommand]
    private void ToggleExpand(CameraTileViewModel? tile)
    {
        if (tile?.MainStreamUri is null)
        {
            return;
        }

        ExpandRequested?.Invoke(tile);
    }

    public ExpandedCameraViewModel CreateExpandedViewModel(CameraTileViewModel tile) =>
        new(_expandLibVlc, tile.Name, tile.MainStreamUri!);

    // Bottom toolbar (Live View): ✕ acts on every tile; the rest act on SelectedTile only.

    /// <summary>✕ — halts decode/display on every tile; a paused/stopped tile resumes via PlayPause once reselected.</summary>
    [RelayCommand]
    private void StopAll()
    {
        foreach (var tile in Cameras)
        {
            tile.Stop();
        }

        StatusMessage = LocalizationService.Get("Status_AllCamerasStopped");
    }

    /// <summary>📷 — saves the selected tile's current frame as a PNG under Pictures\VMS Snapshots.</summary>
    [RelayCommand]
    private void Snapshot()
    {
        if (SelectedTile is null)
        {
            return;
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VMS Snapshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{SelectedTile.Code}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        StatusMessage = SelectedTile.TakeSnapshot(path)
            ? LocalizationService.Get("Status_SnapshotSaved", path)
            : LocalizationService.Get("Status_SnapshotFailed");
    }

    /// <summary>⏺ — toggles a local mp4 recording of the selected tile's sub-stream under Videos\VMS Recordings.</summary>
    [RelayCommand]
    private void Record()
    {
        if (SelectedTile is null)
        {
            return;
        }

        if (SelectedTile.IsRecording)
        {
            SelectedTile.StopRecording();
            StatusMessage = LocalizationService.Get("Status_RecordingStopped");
            return;
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "VMS Recordings");
        var path = SelectedTile.StartRecording(dir);
        StatusMessage = path is not null
            ? LocalizationService.Get("Status_RecordingStarted", path)
            : LocalizationService.Get("Status_RecordingFailed");
    }

    /// <summary>⏸ — freezes/resumes the selected tile's current frame without dropping the connection.</summary>
    [RelayCommand]
    private void PlayPause()
    {
        SelectedTile?.TogglePause();
    }

    /// <summary>⛶ — same fullscreen popup as the tile's own expand button, applied to the selected tile.</summary>
    [RelayCommand]
    private void Fullscreen()
    {
        if (SelectedTile is not null)
        {
            ToggleExpand(SelectedTile);
        }
    }

    [RelayCommand]
    private void AddCamera()
    {
        AddCameraRequested?.Invoke();
    }

    [RelayCommand]
    private void SearchByPhoto()
    {
        SearchByPhotoRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowOperationLog()
    {
        OperationLogRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowArchive()
    {
        ArchiveRequested?.Invoke();
    }

    /// <summary>Populates the same SearchResults list/panel the text search fills — SearchByPhotoWindow hands its hits back here instead of duplicating the ListBox/ClipPopup wiring.</summary>
    public void ApplySearchByFaceResults(List<SearchResultDto> results)
    {
        SearchResults.Clear();
        foreach (var r in results)
        {
            SearchResults.Add(new SearchResultViewModel(r));
        }

        StatusMessage = LocalizationService.Get("Status_FoundCount", SearchResults.Count);
    }

    [RelayCommand]
    private async Task ProtectAsync(SearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _api.ProtectArchiveFileAsync(result.CameraId, result.ArchiveFileName);
            StatusMessage = LocalizationService.Get("Status_ProtectedFile", result.ArchiveFileName);
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

    public void Dispose()
    {
        foreach (var tile in Cameras)
        {
            tile.Dispose();
        }
        _libVlc.Dispose();
        _expandLibVlc.Dispose();
    }
}
