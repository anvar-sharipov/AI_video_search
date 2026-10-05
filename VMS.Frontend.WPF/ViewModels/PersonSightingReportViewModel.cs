using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>One selectable camera checkbox row for the report's camera picker.</summary>
public partial class CameraSelectionViewModel(CameraDto camera) : ObservableObject
{
    public CameraDto Camera { get; } = camera;
    public string Name => Camera.Name;

    [ObservableProperty]
    private bool _isSelected = true;
}

/// <summary>
/// Backs the "how many people were there" report: pick cameras + a date range, get back a
/// per-camera-per-day table of total/known/unknown sightings (see PersonSightingEndpoints,
/// PersonPresenceTracker), with a drill-down to the saved screenshots behind one row (see
/// PersonSightingGalleryWindow).
/// </summary>
public partial class PersonSightingReportViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<CameraSelectionViewModel> Cameras { get; } = [];
    public ObservableCollection<PersonSightingReportRowDto> Rows { get; } = [];

    [ObservableProperty] private DateTime _fromDate = DateTime.Today;
    [ObservableProperty] private DateTime _toDate = DateTime.Today;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>One-directional convenience toggle for the camera checklist — checking/unchecking
    /// applies to every camera; it doesn't try to reflect a mixed selection back (kept simple).</summary>
    [ObservableProperty] private bool _selectAllCameras = true;

    public event Action<PersonSightingReportRowDto>? SightingsRequested;

    partial void OnSelectAllCamerasChanged(bool value)
    {
        foreach (var c in Cameras)
        {
            c.IsSelected = value;
        }
    }

    public PersonSightingReportViewModel(ApiClient api)
    {
        _api = api;
        _ = LoadCamerasAsync();
    }

    private async Task LoadCamerasAsync()
    {
        try
        {
            Cameras.Clear();
            foreach (var c in await _api.GetCamerasAsync())
            {
                Cameras.Add(new CameraSelectionViewModel(c));
            }
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task QueryAsync()
    {
        var selectedCodes = Cameras.Where(c => c.IsSelected).Select(c => c.Camera.Code).ToList();
        if (selectedCodes.Count == 0)
        {
            StatusMessage = LocalizationService.Get("PersonSightingReport_NoCameraSelected");
            return;
        }

        IsBusy = true;
        try
        {
            var rows = await _api.GetPersonSightingReportAsync(selectedCodes, FromDate, ToDate.AddDays(1).AddTicks(-1));
            Rows.Clear();
            foreach (var r in rows)
            {
                Rows.Add(r);
            }
            StatusMessage = Rows.Count == 0 ? LocalizationService.Get("PersonSightingReport_NoData") : string.Empty;
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
    private void ShowSightings(PersonSightingReportRowDto? row)
    {
        if (row is not null)
        {
            SightingsRequested?.Invoke(row);
        }
    }
}
