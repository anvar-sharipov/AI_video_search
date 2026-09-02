using System.Collections.ObjectModel;
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

/// <summary>
/// Camera + date/time archive browser: plays back an arbitrary point in the
/// continuous recording, not just the moments the AI search already found. Reuses
/// the same POST /api/clips extraction the AI-search clip popup uses (ffmpeg -c copy,
/// including its existing multi-segment concat when the window spans a boundary) —
/// just with a much longer PostRollSeconds window instead of a few seconds either
/// side of a detection.
/// </summary>
public partial class ArchiveViewModel : ObservableObject, IDisposable
{
    // Per "Play"/"◀"/"▶" step. Long enough to watch a real stretch of footage,
    // short enough that -c copy concatenation across a handful of 5-minute segments
    // still returns quickly.
    private const int WindowSeconds = 900;

    // Matches ArchiveWindow.xaml's timeline Border Width exactly — TimelineSegments/
    // hour marks are pre-computed in pixels against this constant rather than reacting
    // to ActualWidth, so the window is fixed-size (ResizeMode=NoResize) to keep them lined up.
    public const double TimelineWidth = 880;

    private readonly ApiClient _api;
    private readonly LibVLC _libVlc;
    private Media? _media;
    private DateTimeOffset? _windowStart;

    public MediaPlayer Player { get; }
    public ObservableCollection<CameraDto> Cameras { get; } = [];
    public ObservableCollection<TimelineSegment> TimelineSegments { get; } = [];

    public List<TimelineHourMark> HourMarks { get; } = Enumerable.Range(0, 13)
        .Select(i => new TimelineHourMark($"{i * 2:D2}:00", i * 2 / 24.0 * TimelineWidth))
        .ToList();

    [ObservableProperty] private CameraDto? _selectedCamera;
    [ObservableProperty] private string _dateText = DateTime.Today.ToString("yyyy-MM-dd");
    [ObservableProperty] private string _timeText = "00:00";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _coverageSummary = string.Empty;
    [ObservableProperty] private double _cursorLeft = -10; // off-track until a window has actually been loaded
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isPaused;

    public ArchiveViewModel(ApiClient api)
    {
        _api = api;
        _libVlc = new LibVLC("--avcodec-hw=none");
        Player = new MediaPlayer(_libVlc);
    }

    partial void OnSelectedCameraChanged(CameraDto? value) => _ = LoadCoverageAsync();

    partial void OnDateTextChanged(string value) => _ = LoadCoverageAsync();

    [RelayCommand]
    private async Task LoadCamerasAsync()
    {
        Cameras.Clear();
        foreach (var c in await _api.GetCamerasAsync())
        {
            Cameras.Add(c);
        }

        SelectedCamera ??= Cameras.FirstOrDefault();
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
            CoverageSummary = segments.Count == 0
                ? LocalizationService.Get("Archive_NoCoverage")
                : string.Join(", ", segments.Select(s => $"{s.Start:HH:mm}–{s.End:HH:mm}"));

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
            // chosen moment turns out to have no footage. Silently blank the hint here.
            CoverageSummary = string.Empty;
        }
    }

    private static double ToTimelinePixels(double secondsIntoDay) => secondsIntoDay / 86400.0 * TimelineWidth;

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
            Player.Play(_media);
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

    public void Dispose()
    {
        Player.Stop();
        Player.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
    }
}
