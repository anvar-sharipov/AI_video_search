using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Backs the E-map screen: upload a floor-plan image, pin cameras onto it at clicked positions, click a pin later to jump to that camera's live view.</summary>
public partial class EMapViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<EMapDto> Maps { get; } = [];
    public ObservableCollection<EMapPinDto> Pins { get; } = [];
    public ObservableCollection<CameraDto> Cameras { get; } = [];

    [ObservableProperty] private EMapDto? _selectedMap;
    [ObservableProperty] private byte[]? _mapImageBytes;
    [ObservableProperty] private CameraDto? _cameraToPin;
    [ObservableProperty] private string _newMapName = string.Empty;
    [ObservableProperty] private string? _newMapImagePath;
    [ObservableProperty] private bool _isPlacingPin;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Set by the View when a pin is clicked; MainWindow subscribes to this to jump to that camera's live view.</summary>
    public event Action<string>? CameraPinActivated;

    public EMapViewModel(ApiClient api)
    {
        _api = api;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Maps.Clear();
            foreach (var m in await _api.GetEMapsAsync())
            {
                Maps.Add(m);
            }

            Cameras.Clear();
            foreach (var c in await _api.GetCamerasAsync())
            {
                Cameras.Add(c);
            }

            SelectedMap ??= Maps.FirstOrDefault();
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    partial void OnSelectedMapChanged(EMapDto? value)
    {
        if (value is not null)
        {
            _ = LoadMapContentAsync(value.Id);
        }
        else
        {
            MapImageBytes = null;
            Pins.Clear();
        }
    }

    private async Task LoadMapContentAsync(Guid mapId)
    {
        try
        {
            MapImageBytes = await _api.GetEMapImageBytesAsync(mapId);

            Pins.Clear();
            foreach (var p in await _api.GetEMapPinsAsync(mapId))
            {
                Pins.Add(p);
            }
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UploadMapAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMapName) || string.IsNullOrWhiteSpace(NewMapImagePath))
        {
            StatusMessage = LocalizationService.Get("EMap_NameAndImageRequired");
            return;
        }

        try
        {
            var map = await _api.CreateEMapAsync(NewMapName.Trim(), NewMapImagePath);
            Maps.Add(map);
            SelectedMap = map;
            NewMapName = string.Empty;
            NewMapImagePath = null;
            StatusMessage = string.Empty;
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteMapAsync()
    {
        if (SelectedMap is null)
        {
            return;
        }

        try
        {
            await _api.DeleteEMapAsync(SelectedMap.Id);
            Maps.Remove(SelectedMap);
            SelectedMap = Maps.FirstOrDefault();
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void TogglePlacingPin() => IsPlacingPin = !IsPlacingPin;

    /// <summary>Called by the View on a map click while IsPlacingPin is true.</summary>
    public async Task PlacePinAsync(double x, double y)
    {
        if (SelectedMap is null || CameraToPin is null)
        {
            StatusMessage = LocalizationService.Get("EMap_ChooseCameraFirst");
            return;
        }

        try
        {
            var pin = await _api.AddEMapPinAsync(SelectedMap.Id, new CreateEMapPinRequestDto(CameraToPin.Id, x, y));
            Pins.Add(pin);
            IsPlacingPin = false;
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RemovePinAsync(EMapPinDto? pin)
    {
        if (pin is null || SelectedMap is null)
        {
            return;
        }

        try
        {
            await _api.DeleteEMapPinAsync(SelectedMap.Id, pin.Id);
            Pins.Remove(pin);
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ActivatePin(EMapPinDto? pin)
    {
        if (pin is not null)
        {
            CameraPinActivated?.Invoke(pin.CameraCode);
        }
    }
}
