using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class SelectableCameraViewModel(CameraDto camera) : ObservableObject
{
    public CameraDto Camera { get; } = camera;

    [ObservableProperty] private bool _isChecked;
}

/// <summary>Create-or-edit a CameraGroup: a name plus a checkbox list of every camera. Works for
/// both Add (group is null) and Edit (group's existing members are pre-checked) — one window,
/// like the codebase's other create/edit pairs that DO differ (Camera has separate Add/Edit
/// windows) but here the two forms are identical apart from what's pre-checked.</summary>
public partial class EditCameraGroupViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly Guid? _groupId;

    public ObservableCollection<SelectableCameraViewModel> Cameras { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event Action? Saved;

    public EditCameraGroupViewModel(ApiClient api, CameraGroupDto? group)
    {
        _api = api;
        _groupId = group?.Id;
        _name = group?.Name ?? string.Empty;
        _ = LoadCamerasAsync(group);
    }

    private async Task LoadCamerasAsync(CameraGroupDto? group)
    {
        var memberIds = group?.Members.Select(m => m.CameraId).ToHashSet() ?? [];
        try
        {
            foreach (var camera in await _api.GetCamerasAsync())
            {
                Cameras.Add(new SelectableCameraViewModel(camera) { IsChecked = memberIds.Contains(camera.Id) });
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = LocalizationService.Get("EditCameraGroup_NameRequiredError");
            return;
        }

        var cameraIds = Cameras.Where(c => c.IsChecked).Select(c => c.Camera.Id).ToList();
        var request = new SaveCameraGroupRequestDto(Name.Trim(), cameraIds);

        IsBusy = true;
        try
        {
            if (_groupId is { } id)
            {
                await _api.UpdateCameraGroupAsync(id, request);
            }
            else
            {
                await _api.CreateCameraGroupAsync(request);
            }
            Saved?.Invoke();
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
