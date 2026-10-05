using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>
/// Backs the People Counting screen: pick a camera, then draw the counting tripwire over its
/// latest snapshot as a polyline of 2 or more points (see CameraCountingLine/PeopleCountingTracker
/// for the honest accuracy caveat — this is snapshot-interval tracking, not real video tracking),
/// then query how many crossings happened over a date range.
/// </summary>
public partial class PeopleCountingViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<CameraDto> Cameras { get; } = [];

    /// <summary>Always at least 2 points, in order — the code-behind (PeopleCountingWindow) is
    /// responsible for keeping it that way (blocking removal below 2) since it owns the drag/
    /// add/remove mouse interaction.</summary>
    public ObservableCollection<(double X, double Y)> Points { get; } = [(0.5, 0.15), (0.5, 0.85)];

    [ObservableProperty] private CameraDto? _selectedCamera;
    [ObservableProperty] private byte[]? _snapshotBytes;
    [ObservableProperty] private bool _leftToRightIsIn = true;
    [ObservableProperty] private bool _hasLine;
    [ObservableProperty] private DateTime _fromDate = DateTime.Today;
    [ObservableProperty] private DateTime _toDate = DateTime.Today.AddDays(1);
    [ObservableProperty] private int _inCount;
    [ObservableProperty] private int _outCount;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public PeopleCountingViewModel(ApiClient api)
    {
        _api = api;
        _ = LoadCamerasAsync();
    }

    [RelayCommand]
    private async Task LoadCamerasAsync()
    {
        try
        {
            Cameras.Clear();
            foreach (var c in await _api.GetCamerasAsync())
            {
                Cameras.Add(c);
            }
            SelectedCamera ??= Cameras.FirstOrDefault();
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    partial void OnSelectedCameraChanged(CameraDto? value)
    {
        if (value is not null)
        {
            _ = LoadForCameraAsync(value.Code);
        }
    }

    private async Task LoadForCameraAsync(string cameraCode)
    {
        SnapshotBytes = await _api.GetCameraSnapshotAsync(cameraCode);

        var line = await _api.GetCountingLineAsync(cameraCode);
        if (line is not null && line.Points.Count >= 2)
        {
            Points.Clear();
            foreach (var p in line.Points)
            {
                Points.Add((p.X, p.Y));
            }

            LeftToRightIsIn = line.LeftToRightIsIn;
            HasLine = true;
        }
        else
        {
            HasLine = false;
        }
    }

    [RelayCommand]
    private async Task SaveLineAsync()
    {
        if (SelectedCamera is null || Points.Count < 2)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var points = Points.Select(p => new CountingLinePointDto(p.X, p.Y)).ToList();
            await _api.SetCountingLineAsync(SelectedCamera.Code, new SetCountingLineRequestDto(points, LeftToRightIsIn));
            HasLine = true;
            StatusMessage = LocalizationService.Get("PeopleCounting_LineSaved");
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
    private async Task DeleteLineAsync()
    {
        if (SelectedCamera is null)
        {
            return;
        }

        try
        {
            await _api.DeleteCountingLineAsync(SelectedCamera.Code);
            HasLine = false;
            StatusMessage = LocalizationService.Get("PeopleCounting_LineDeleted");
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task QueryCountAsync()
    {
        if (SelectedCamera is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _api.GetPeopleCountAsync(SelectedCamera.Code, FromDate, ToDate);
            InCount = result.In;
            OutCount = result.Out;
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
}
