using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
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
    public ThemeService Theme { get; }

    public ObservableCollection<CameraTileViewModel> Cameras { get; } = [];
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    /// <summary>Drives the search-results dropdown Popup (two-way: StaysOpen="False" pushes this
    /// back to false when the user clicks elsewhere, and a fresh search sets it true again).</summary>
    [ObservableProperty]
    private bool _isSearchResultsOpen;

    /// <summary>The live-view grid's cells. Always in sync with Cameras/GridSize via RebuildGridSlots.</summary>
    public ObservableCollection<GridSlotViewModel> GridSlots { get; } = [];

    /// <summary>Manual slot assignments (fixed layouts only), keyed by slot index → camera Code.
    /// Survives Cameras being cleared/reloaded (e.g. after adding a camera elsewhere) since it's
    /// keyed by the stable Code, not by any CameraTileViewModel instance.</summary>
    private readonly Dictionary<int, string> _slotAssignments = [];

    /// <summary>Set only when the user selected an empty cell (a non-empty cell's selection is
    /// tracked via SelectedTile instead — see SelectSlot).</summary>
    private GridSlotViewModel? _selectedEmptySlot;

    /// <summary>Camera codes to show in Auto layout — null means "all loaded cameras" (the
    /// original, default behavior). Set by picking a saved camera group (quick-switch) or by
    /// the alarm filter; both funnel through ApplyCameraGroupFilter.</summary>
    private HashSet<string>? _activeCameraFilter;

    private readonly DispatcherTimer _alarmFilterTimer;

    public ICollectionView TreeCamerasView { get; }

    [ObservableProperty]
    private string _treeSearchText = string.Empty;

    /// <summary>
    /// UniformGrid.Rows/Columns: 0 means "auto, size from item count" (the original behavior);
    /// 1/2/3/4 forces an NxN layout, per the layout-switcher toolbar.
    /// </summary>
    [ObservableProperty]
    private int _gridSize;

    /// <summary>Backs the grid-size dropdown that replaced the old row of Auto/1x1../4x4 buttons — now goes up to 8x8.</summary>
    public List<int> GridSizeOptions { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>Sidebar's 🕐 icon (repurposed from "open Archive" — Archive is still reachable from the Control Panel's Playback card): toggles showing only cameras with a recent, unacknowledged person/vehicle alarm.</summary>
    [ObservableProperty]
    private bool _isAlarmFilterActive;

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
    public event Action<CameraTileViewModel>? ExpandRequested;
    public event Action? SearchByPhotoRequested;
    public event Action? OperationLogRequested;
    public event Action? ArchiveRequested;
    public event Action? CameraManagementRequested;
    public event Action? UserManagementRequested;
    public event Action? VideoWallRequested;
    public event Action? AudioRequested;
    public event Action? EMapRequested;
    public event Action? PeopleCountingRequested;
    public event Action? PersonSightingReportRequested;
    public event Action? LogOutRequested;
    public event Action? AlarmRecordsRequested;
    public event Action? FaceRecognitionRequested;
    public event Action? CameraGroupsRequested;
    public event Action? QuickGroupSwitchRequested;

    public MainViewModel(ApiClient api, SessionService session, LocalizationService localization, ThemeService theme)
    {
        _api = api;
        Session = session;
        Localization = localization;
        Theme = theme;
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

        TreeCamerasView = CollectionViewSource.GetDefaultView(Cameras);
        TreeCamerasView.Filter = FilterTreeCamera;

        _alarmFilterTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _alarmFilterTimer.Tick += async (_, _) => await RefreshAlarmFilterAsync();
    }

    partial void OnTreeSearchTextChanged(string value) => TreeCamerasView.Refresh();

    private bool FilterTreeCamera(object obj)
    {
        if (string.IsNullOrWhiteSpace(TreeSearchText))
        {
            return true;
        }

        var tile = (CameraTileViewModel)obj;
        var needle = TreeSearchText.Trim();
        return tile.Code.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || tile.Name.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnGridSizeChanged(int value) => RebuildGridSlots();

    /// <summary>
    /// Keeps GridSlots in sync with Cameras/GridSize. Auto (GridSize 0) mirrors Cameras 1:1 in
    /// order — identical to the grid's original behavior, no empty cells possible. A fixed NxN
    /// layout has exactly N*N slots: previously-assigned cameras (by Code, from
    /// _slotAssignments) keep their cell, then any not-yet-assigned camera fills the remaining
    /// empty cells in order, so a fresh layout switch still "just shows" loaded cameras like
    /// before until the user manually rearranges anything.
    /// </summary>
    private void RebuildGridSlots()
    {
        _selectedEmptySlot = null;
        GridSlots.Clear();

        if (GridSize == 0)
        {
            IReadOnlyList<CameraTileViewModel> visible = _activeCameraFilter is null
                ? Cameras
                : Cameras.Where(c => _activeCameraFilter.Contains(c.Code)).ToList();
            for (var i = 0; i < visible.Count; i++)
            {
                GridSlots.Add(new GridSlotViewModel(i) { Tile = visible[i] });
            }
            return;
        }

        var cellCount = GridSize * GridSize;
        var slots = new GridSlotViewModel[cellCount];
        for (var i = 0; i < cellCount; i++)
        {
            slots[i] = new GridSlotViewModel(i);
            if (_slotAssignments.TryGetValue(i, out var code))
            {
                slots[i].Tile = Cameras.FirstOrDefault(c => c.Code == code);
            }
        }

        var alreadyAssigned = slots.Where(s => s.Tile is not null).Select(s => s.Tile!.Code).ToHashSet();
        var unassignedCameras = new Queue<CameraTileViewModel>(Cameras.Where(c => !alreadyAssigned.Contains(c.Code)));
        foreach (var slot in slots.Where(s => s.Tile is null))
        {
            if (!unassignedCameras.TryDequeue(out var camera))
            {
                break;
            }
            slot.Tile = camera;
        }

        foreach (var slot in slots)
        {
            GridSlots.Add(slot);
        }
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
            // SelectedTile must never outlive the tile it points to — Dispose() tears down the
            // native MediaPlayer, and anything still referencing it afterward (e.g. the bottom
            // toolbar's Snapshot/Record buttons, enabled by HasSelectedTile) would call into a
            // freed LibVLC handle and crash the whole process with an access violation.
            SelectTile(null);

            foreach (var tile in Cameras)
            {
                tile.Dispose();
            }
            Cameras.Clear();

            var cameras = await _api.GetCamerasAsync();
            foreach (var camera in cameras)
            {
                var tile = new CameraTileViewModel(_libVlc, camera, _api);
                Cameras.Add(tile);

                var status = await _api.GetCameraStatusAsync(camera.Code);
                if (status is not null)
                {
                    tile.SetStreamUris(status.MainStreamUri, status.SubStreamUri);
                }
            }

            StatusMessage = LocalizationService.Get("Status_CamerasCount", Cameras.Count);
            RebuildGridSlots();
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
            IsSearchResultsOpen = SearchResults.Count > 0;
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

    /// <summary>Single click on a tile's header (grid or tree): marks it the "active" tile
    /// (highlighted border) — distinct from double-clicking/expanding to fullscreen.</summary>
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
            if (_selectedEmptySlot is not null)
            {
                _selectedEmptySlot.IsSelected = false;
                _selectedEmptySlot = null;
            }
        }
    }

    /// <summary>Single click on a camera in the sidebar tree: highlights its tile in the grid if
    /// shown, without disturbing a previously-selected empty slot — a tree double-click always
    /// fires this once (as the first click of the gesture) immediately before the double-click
    /// itself, so this must not clear the assignment target SelectTile normally clears.</summary>
    [RelayCommand]
    private void SelectTileFromTree(CameraTileViewModel? tile)
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

    /// <summary>Click on a grid cell: occupied cells behave exactly like clicking the tile's own
    /// header (SelectTile); empty cells (fixed layouts only) become the assignment target for a
    /// tree double-click, tracked separately since there's no tile to hang IsSelected off of.</summary>
    [RelayCommand]
    private void SelectSlot(GridSlotViewModel slot)
    {
        if (slot.Tile is not null)
        {
            SelectTile(slot.Tile);
            return;
        }

        if (_selectedEmptySlot is not null)
        {
            _selectedEmptySlot.IsSelected = false;
        }

        SelectTile(null);
        _selectedEmptySlot = slot;
        slot.IsSelected = true;
    }

    /// <summary>Double click on a camera in the sidebar tree: puts it in the currently-selected
    /// grid cell (empty or occupied — occupied gets replaced). Only meaningful for a fixed NxN
    /// layout; Auto always shows every loaded camera already, so there's nothing to assign.</summary>
    [RelayCommand]
    private void AssignCameraToSelectedSlot(CameraTileViewModel? camera)
    {
        if (camera is null)
        {
            return;
        }

        if (GridSize == 0)
        {
            StatusMessage = LocalizationService.Get("Slot_OnlyInFixedLayout");
            return;
        }

        int targetIndex;
        if (_selectedEmptySlot is not null)
        {
            targetIndex = _selectedEmptySlot.Index;
        }
        else if (SelectedTile is not null && GridSlots.FirstOrDefault(s => s.Tile == SelectedTile) is { } occupiedSlot)
        {
            targetIndex = occupiedSlot.Index;
        }
        else
        {
            StatusMessage = LocalizationService.Get("Slot_SelectFirst");
            return;
        }

        // A camera can only occupy one cell at a time — moving it here vacates wherever it was.
        foreach (var oldIndex in _slotAssignments.Where(kv => kv.Value == camera.Code).Select(kv => kv.Key).ToList())
        {
            _slotAssignments.Remove(oldIndex);
        }

        _slotAssignments[targetIndex] = camera.Code;
        RebuildGridSlots();
    }

    /// <summary>NxN layout, or 0 for auto — set from the layout-switcher toolbar.</summary>
    [RelayCommand]
    private void SetGridSize(string sizeText)
    {
        GridSize = int.Parse(sizeText);
    }

    /// <summary>Applies a saved camera group's members (quick-switch icon) or clears back to "all
    /// cameras" (null) — always in Auto layout, since a group/alarm view is about which cameras
    /// show, not a fixed slot arrangement.</summary>
    public void ApplyCameraGroupFilter(HashSet<string>? cameraCodes)
    {
        _activeCameraFilter = cameraCodes;
        GridSize = 0;
        RebuildGridSlots();
    }

    [RelayCommand]
    private void ShowCameraGroups() => CameraGroupsRequested?.Invoke();

    [RelayCommand]
    private void ShowQuickGroupSwitch() => QuickGroupSwitchRequested?.Invoke();

    [RelayCommand]
    private async Task ToggleAlarmFilterAsync()
    {
        IsAlarmFilterActive = !IsAlarmFilterActive;

        if (IsAlarmFilterActive)
        {
            _alarmFilterTimer.Start();
            await RefreshAlarmFilterAsync();
        }
        else
        {
            _alarmFilterTimer.Stop();
            ApplyCameraGroupFilter(null);
        }
    }

    /// <summary>"Alarm" = a person detection with no matched enrolled name (a genuinely unknown
    /// person — the only "known/unknown" distinction this system actually has, see
    /// KnownPersonMatcher) or any vehicle-class detection (there's no known/unknown-vehicle
    /// registry, so every vehicle counts — an honest reading of what's actually detectable,
    /// not a made-up rule engine).</summary>
    private async Task RefreshAlarmFilterAsync()
    {
        try
        {
            var alarms = await _api.GetAlarmsAsync(from: DateTimeOffset.UtcNow.AddMinutes(-5));
            var alarmingCameraCodes = alarms
                .Where(a => !a.IsAcknowledged)
                .Where(a => (a.Detection.ObjectType == "person" && a.Detection.PersonName is null)
                    || VehicleTypes.Contains(a.Detection.ObjectType))
                .Select(a => a.Detection.CameraId)
                .ToHashSet();

            ApplyCameraGroupFilter(alarmingCameraCodes);
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private static readonly HashSet<string> VehicleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "car", "truck", "bus", "motorcycle", "bicycle"
    };

    [RelayCommand]
    private void SetLanguage(string languageCode) => Localization.SetLanguage(languageCode);

    [RelayCommand]
    private void ToggleTheme() => Theme.ToggleTheme();

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

        SelectTile(tile);
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

    [RelayCommand]
    private void ShowUserManagement()
    {
        UserManagementRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowCameraManagement()
    {
        CameraManagementRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowVideoWall()
    {
        VideoWallRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowAudio()
    {
        AudioRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowEMap()
    {
        EMapRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowPeopleCounting()
    {
        PeopleCountingRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowPersonSightingReport()
    {
        PersonSightingReportRequested?.Invoke();
    }

    [RelayCommand]
    private void LogOut()
    {
        LogOutRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowAlarmRecords()
    {
        AlarmRecordsRequested?.Invoke();
    }

    [RelayCommand]
    private void ShowFaceRecognition()
    {
        FaceRecognitionRequested?.Invoke();
    }

    /// <summary>Populates the same SearchResults list/panel the text search fills — SearchByPhotoWindow hands its hits back here instead of duplicating the ListBox/ClipPopup wiring.</summary>
    public void ApplySearchByFaceResults(List<SearchResultDto> results)
    {
        SearchResults.Clear();
        foreach (var r in results)
        {
            SearchResults.Add(new SearchResultViewModel(r));
        }

        IsSearchResultsOpen = SearchResults.Count > 0;
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
        _alarmFilterTimer.Stop();
        foreach (var tile in Cameras)
        {
            tile.Dispose();
        }
        _libVlc.Dispose();
        _expandLibVlc.Dispose();
    }
}
