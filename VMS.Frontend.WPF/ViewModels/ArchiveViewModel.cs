using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Pixel span of one recorded stretch on the 24h timeline bar (see TimelineWidth) — filled bar segments, like a typical NVR's day timeline.</summary>
public record TimelineSegment(double Left, double Width);

/// <summary>One hour-ruler label above the timeline bar (every 2 hours: 00:00, 02:00, ...).</summary>
public record TimelineHourMark(string Label, double Left);

/// <summary>A colored tick on the timeline marking a search hit at that moment — purple for a person match, yellow (gold) for a vehicle match. There is no separate "motion" signal in this system distinct from object detection, so no red tick exists; ticks only ever come from real SearchResults, never synthesized.</summary>
public record TimelineEventMark(double Left, Brush MarkBrush);

/// <summary>One selectable color for the Attribute Search tab — the exact 9 names ColorTagger can ever tag (VMS.MetadataIndexer.Detection.ColorTagger.NamedColors), so filtering by name always matches something real.</summary>
public record ColorOption(string Name, string Hex);

/// <summary>
/// Camera + date/time archive browser with an integrated AI search drawer (Named Person /
/// Attributes / Reverse Image) and a visual results grid — the "Playback" screen. Reuses the
/// same POST /api/clips extraction the AI-search clip popup uses (ffmpeg -c copy, including its
/// existing multi-segment concat when the window spans a boundary), just with a much longer
/// PostRollSeconds window instead of a few seconds either side of a detection.
/// </summary>
public partial class ArchiveViewModel : ObservableObject, IDisposable
{
    // Per "Play"/"◀"/"▶" step — a short, precise step for scrubbing through footage a few
    // seconds at a time (user-requested; was 900s/15min, which made stepping too coarse to
    // land on a specific moment).
    private const int WindowSeconds = 15;
    private const double DefaultTimelineWidth = 880;

    private static readonly Brush PersonMarkBrush = FrozenBrush(Color.FromRgb(0x8E, 0x5B, 0xD6));
    private static readonly Brush VehicleMarkBrush = FrozenBrush(Color.FromRgb(0xDC, 0xD2, 0x28));
    private static readonly HashSet<string> VehicleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "car", "truck", "bus", "motorcycle", "bicycle"
    };

    private readonly ApiClient _api;
    private readonly LibVLC _libVlc;
    private Media? _media;
    private DateTimeOffset? _windowStart;

    /// <summary>True once this window's VideoView is realized (native Hwnd exists) — playing
    /// before that leaves LibVLC with nowhere to render (see ClipPopupViewModel for the same
    /// pattern). Play used to only ever be triggered by a post-Loaded user action here, but
    /// double-clicking a search result now also triggers it programmatically, so this guard
    /// is defensive rather than provably necessary.</summary>
    private bool _viewReady;

    private (int X, int Y, int Width, int Height)? _photoCropBox;

    public MediaPlayer Player { get; }
    public ObservableCollection<CameraDto> Cameras { get; } = [];
    public ObservableCollection<TimelineSegment> TimelineSegments { get; } = [];
    public ObservableCollection<TimelineEventMark> EventMarks { get; } = [];
    public ObservableCollection<SearchResultCardViewModel> SearchResults { get; } = [];
    public ObservableCollection<KnownPersonDto> KnownPersons { get; } = [];
    public ICollectionView ResultsView { get; }

    public List<string> ObjectTypeOptions { get; } = ["person", "car", "truck", "bus", "motorcycle", "bicycle"];

    public List<ColorOption> ColorOptions { get; } =
    [
        new("black", "#000000"), new("white", "#FFFFFF"), new("gray", "#808080"),
        new("red", "#C81E1E"), new("orange", "#E6821E"), new("yellow", "#DCD228"),
        new("green", "#288C3C"), new("blue", "#2846B4"), new("silver", "#BEBEBE")
    ];

    [ObservableProperty] private CameraDto? _selectedCamera;
    [ObservableProperty] private string _dateText = DateTime.Today.ToString("yyyy-MM-dd");
    [ObservableProperty] private string _timeText = "00:00";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _cursorLeft = -10; // off-track until a window has actually been loaded
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private double _timelineWidth = DefaultTimelineWidth;
    [ObservableProperty] private List<TimelineHourMark> _hourMarks = BuildHourMarks(DefaultTimelineWidth);

    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchStatusMessage = string.Empty;

    // Named Person tab
    [ObservableProperty] private string? _personSearchText;

    // Attributes tab — only real, model-backed filters (object type, color, plate). No
    // gender/age/glasses/mask/vehicle-subtype controls: nothing in the detection pipeline
    // produces those attributes, so offering them (even disabled) would misrepresent what
    // this system can do.
    [ObservableProperty] private string? _selectedObjectType;
    [ObservableProperty] private ColorOption? _selectedColorOption;
    [ObservableProperty] private string _plateText = string.Empty;

    // Reverse Image tab
    [ObservableProperty] private string? _photoPath;
    [ObservableProperty] private double _similarityThreshold = 50;

    /// <summary>Hides the results-grid panel entirely until there's actually something to show
    /// — an empty fixed-height panel under the video looked like a pointless second box when
    /// just browsing the timeline without having searched for anything.</summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    public ArchiveViewModel(ApiClient api)
    {
        _api = api;
        _libVlc = new LibVLC("--avcodec-hw=none");
        Player = new MediaPlayer(_libVlc);
        ResultsView = CollectionViewSource.GetDefaultView(SearchResults);
        ResultsView.Filter = FilterResult;
        SearchResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSearchResults));
    }

    partial void OnSelectedCameraChanged(CameraDto? value)
    {
        _ = LoadCoverageAsync();
        RebuildEventMarks();
    }

    partial void OnDateTextChanged(string value)
    {
        _ = LoadCoverageAsync();
        RebuildEventMarks();
    }

    partial void OnSimilarityThresholdChanged(double value) => ResultsView.Refresh();

    partial void OnTimelineWidthChanged(double value)
    {
        HourMarks = BuildHourMarks(value);
        _ = LoadCoverageAsync();
        RebuildEventMarks();
        if (_windowStart is { } start)
        {
            CursorLeft = ToTimelinePixels(start.TimeOfDay.TotalSeconds);
        }
    }

    private bool FilterResult(object obj) =>
        obj is not SearchResultCardViewModel card || card.MatchScore is null || card.MatchScore.Value * 100 >= SimilarityThreshold;

    private static List<TimelineHourMark> BuildHourMarks(double width) => Enumerable.Range(0, 13)
        .Select(i => new TimelineHourMark($"{i * 2:D2}:00", i * 2 / 24.0 * width))
        .ToList();

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    [RelayCommand]
    private async Task LoadCamerasAsync()
    {
        Cameras.Clear();
        foreach (var c in await _api.GetCamerasAsync())
        {
            Cameras.Add(c);
        }

        SelectedCamera ??= Cameras.FirstOrDefault();

        KnownPersons.Clear();
        try
        {
            foreach (var p in await _api.GetKnownPersonsAsync())
            {
                KnownPersons.Add(p);
            }
        }
        catch (ApiException)
        {
            // Named Person tab just shows an empty autocomplete list for a Viewer-role
            // user (ManageKnownPersons is Operator+) — they can still type a name they
            // already know and search by it.
        }
    }

    [RelayCommand]
    private async Task LoadCoverageAsync()
    {
        TimelineSegments.Clear();

        if (SelectedCamera is null || !DateOnly.TryParse(DateText, out var date))
        {
            return;
        }

        try
        {
            var segments = await _api.GetArchiveCoverageAsync(SelectedCamera.Code, date);

            var startOfDay = date.ToDateTime(TimeOnly.MinValue);
            foreach (var s in segments)
            {
                var left = ToTimelinePixels((s.Start.LocalDateTime - startOfDay).TotalSeconds);
                var right = ToTimelinePixels((s.End.LocalDateTime - startOfDay).TotalSeconds);
                if (right <= 0 || left >= TimelineWidth)
                {
                    continue; // entirely outside this day (the "coverage" heuristic can overshoot into the next day)
                }

                left = Math.Max(0, left);
                right = Math.Min(TimelineWidth, right);
                TimelineSegments.Add(new TimelineSegment(left, Math.Max(2, right - left)));
            }
        }
        catch (ApiException)
        {
            // Coverage is a hint, not a gate — Play still tells the user plainly if the
            // chosen moment turns out to have no footage. Nothing to show when it fails.
        }
    }

    private double ToTimelinePixels(double secondsIntoDay) => secondsIntoDay / 86400.0 * TimelineWidth;

    private void RebuildEventMarks()
    {
        EventMarks.Clear();
        if (SelectedCamera is null || !DateOnly.TryParse(DateText, out var date))
        {
            return;
        }

        var startOfDay = date.ToDateTime(TimeOnly.MinValue);
        foreach (var r in SearchResults)
        {
            if (r.CameraId != SelectedCamera.Code || DateOnly.FromDateTime(r.Timestamp.LocalDateTime) != date)
            {
                continue;
            }

            var left = ToTimelinePixels((r.Timestamp.LocalDateTime - startOfDay).TotalSeconds);
            if (left < 0 || left > TimelineWidth)
            {
                continue;
            }

            var brush = r.ObjectType == "person" ? PersonMarkBrush : VehicleTypes.Contains(r.ObjectType) ? VehicleMarkBrush : null;
            if (brush is not null)
            {
                EventMarks.Add(new TimelineEventMark(left, brush));
            }
        }
    }

    /// <summary>Timeline bar click — ArchiveWindow's code-behind converts the click's pixel X into a 0..1 fraction of the day and hands it here.</summary>
    public void SeekToFraction(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var totalMinutes = (int)(fraction * 24 * 60);
        TimeText = $"{totalMinutes / 60:D2}:{totalMinutes % 60:D2}";
        _ = PlayAsync();
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (SelectedCamera is null || !DateOnly.TryParse(DateText, out var date) || !TimeSpan.TryParse(TimeText, out var timeOfDay))
        {
            StatusMessage = LocalizationService.Get("Archive_InvalidTimeError");
            return;
        }

        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue) + timeOfDay, DateTimeOffset.Now.Offset);
        await LoadWindowAsync(SelectedCamera.Code, start);
    }

    /// <summary>Double-click on a search-results card — jumps the player straight to that moment, switching cameras first if the hit is on a different one than currently selected.</summary>
    [RelayCommand]
    private async Task JumpToResultAsync(SearchResultCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var camera = Cameras.FirstOrDefault(c => c.Code == card.CameraId);
        if (camera is not null)
        {
            SelectedCamera = camera;
        }

        await LoadWindowAsync(card.CameraId, card.Timestamp);
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private async Task NextAsync()
    {
        if (SelectedCamera is not null && _windowStart is not null)
        {
            await LoadWindowAsync(SelectedCamera.Code, _windowStart.Value.AddSeconds(WindowSeconds));
        }
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private async Task PreviousAsync()
    {
        if (SelectedCamera is not null && _windowStart is not null)
        {
            await LoadWindowAsync(SelectedCamera.Code, _windowStart.Value.AddSeconds(-WindowSeconds));
        }
    }

    private bool CanStep() => _windowStart is not null;

    [RelayCommand]
    private void TogglePause()
    {
        if (Player.IsPlaying)
        {
            Player.Pause();
            IsPaused = true;
        }
        else
        {
            Player.Play();
            IsPaused = false;
        }
    }

    // --- Real transport controls: every one below calls an actual LibVLC API, none are stubs. ---

    [RelayCommand] private void SetSpeed1x() => Player.SetRate(1f);
    [RelayCommand] private void SetSpeed2x() => Player.SetRate(2f);
    [RelayCommand] private void SetSpeed4x() => Player.SetRate(4f);
    [RelayCommand] private void SetSpeed16x() => Player.SetRate(16f);

    /// <summary>Only meaningful while paused (LibVLC decodes and displays exactly one further frame) — the Step button's IsEnabled is bound to IsPaused in XAML.</summary>
    [RelayCommand] private void StepFrame() => Player.NextFrame();

    /// <summary>Saves the currently displayed frame to disk — called from code-behind after a SaveFileDialog picks the destination, or by the "crop from player" flow. Works while paused (a paused frame is exactly what crop-to-search wants), just not before any window has ever been loaded. Returns false when there's no frame to capture yet.</summary>
    public bool SaveSnapshot(string path) => _media is not null && Player.TakeSnapshot(0, path, 0, 0);

    // --- Reverse Image tab ---

    /// <summary>Sets the reference photo from a file picker or drag-and-drop — clears any earlier crop-from-player selection since this is a fresh, uncropped photo.</summary>
    public void SetPhoto(string path)
    {
        PhotoPath = path;
        _photoCropBox = null;
    }

    /// <summary>Sets the reference photo AND a rough crop rectangle (in the photo's own pixel coordinates) — used by the "crop from player" flow, where the photo is a full-frame snapshot and the crop narrows the embedder to the selected person/face.</summary>
    public void SetPhotoWithCrop(string path, (int X, int Y, int Width, int Height) cropBox)
    {
        PhotoPath = path;
        _photoCropBox = cropBox;
    }

    [RelayCommand]
    private async Task SearchByPersonAsync()
    {
        if (string.IsNullOrWhiteSpace(PersonSearchText))
        {
            SearchStatusMessage = LocalizationService.Get("Archive_Search_EnterNameError");
            return;
        }

        await RunSearchAsync(() => _api.SearchAsync(string.Empty, personName: PersonSearchText.Trim()));
    }

    [RelayCommand]
    private async Task SearchByAttributesAsync()
    {
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(SelectedObjectType))
        {
            terms.Add(SelectedObjectType);
        }

        if (SelectedColorOption is not null)
        {
            terms.Add(SelectedColorOption.Name);
        }

        if (!string.IsNullOrWhiteSpace(PlateText))
        {
            terms.Add(PlateText.Trim());
        }

        if (terms.Count == 0)
        {
            SearchStatusMessage = LocalizationService.Get("Archive_Search_NoFiltersError");
            return;
        }

        await RunSearchAsync(() => _api.SearchAsync(string.Join(' ', terms)));
    }

    [RelayCommand]
    private async Task SearchByPhotoAsync()
    {
        if (string.IsNullOrWhiteSpace(PhotoPath))
        {
            SearchStatusMessage = LocalizationService.Get("Archive_Search_ChoosePhotoError");
            return;
        }

        await RunSearchAsync(() => _api.SearchByFaceAsync(PhotoPath, _photoCropBox));
    }

    private async Task RunSearchAsync(Func<Task<List<SearchResultDto>>> fetch)
    {
        IsSearching = true;
        SearchStatusMessage = string.Empty;
        try
        {
            var results = await fetch();
            SearchResults.Clear();
            foreach (var dto in results)
            {
                SearchResults.Add(new SearchResultCardViewModel(_api, dto));
            }

            RebuildEventMarks();
            SearchStatusMessage = results.Count == 0 ? LocalizationService.Get("Archive_Search_NoResults") : string.Empty;
        }
        catch (ApiException ex)
        {
            SearchStatusMessage = ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task LoadWindowAsync(string cameraId, DateTimeOffset start)
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var fileName = await _api.RequestClipAsync(new ClipRequestDto(cameraId, start, 0, WindowSeconds));
            var uri = _api.GetClipDownloadUri(fileName);

            _media?.Dispose();
            _media = new Media(_libVlc, uri);
            if (_viewReady)
            {
                Player.Play(_media);
            }
            IsPaused = false;

            _windowStart = start;
            TimeText = start.ToString("HH:mm");
            DateText = start.ToString("yyyy-MM-dd");
            CursorLeft = ToTimelinePixels(start.TimeOfDay.TotalSeconds);
            NextCommand.NotifyCanExecuteChanged();
            PreviousCommand.NotifyCanExecuteChanged();
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

    /// <summary>Called from ArchiveWindow's VideoView.Loaded — see the _viewReady field comment.</summary>
    public void NotifyViewLoaded()
    {
        if (_viewReady)
        {
            return;
        }

        _viewReady = true;
        if (_media is not null)
        {
            Player.Play(_media);
        }
    }

    public void Dispose()
    {
        Player.Stop();
        Player.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
    }
}
