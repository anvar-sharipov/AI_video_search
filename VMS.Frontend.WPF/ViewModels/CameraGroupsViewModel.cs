using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class CameraGroupRowViewModel(CameraGroupDto group) : ObservableObject
{
    public CameraGroupDto Group { get; } = group;
    public string CameraSummary => string.Join(", ", Group.Members.Select(m => m.Name));

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Backs the Camera Groups management screen ("screens" of cameras for Live View — e.g. cameras 1-5 on one view, 6-20 on another). Mirrors CameraManagementViewModel/UserManagementViewModel's checkbox-row-select + Edit/Delete pattern.</summary>
public partial class CameraGroupsViewModel(ApiClient api, SessionService session) : ObservableObject
{
    public SessionService Session { get; } = session;

    public ObservableCollection<CameraGroupRowViewModel> Groups { get; } = [];

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Raised when exactly one group is checked and Edit is clicked; the Window's code-behind owns opening EditCameraGroupWindow.</summary>
    public event Action<CameraGroupDto>? EditRequested;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = LocalizationService.Get("CameraGroups_Status_Loading");
        try
        {
            var groups = await api.GetCameraGroupsAsync();
            Groups.Clear();
            foreach (var g in groups)
            {
                Groups.Add(new CameraGroupRowViewModel(g));
            }
            StatusMessage = LocalizationService.Get("CameraGroups_Status_Loaded", Groups.Count);
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
    private void EditSelected()
    {
        var selected = Groups.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("CameraGroups_SelectGroupFirst");
            return;
        }
        if (selected.Count > 1)
        {
            StatusMessage = LocalizationService.Get("CameraGroups_SelectOneForEdit");
            return;
        }

        EditRequested?.Invoke(selected[0].Group);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = Groups.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("CameraGroups_SelectGroupFirst");
            return;
        }

        var confirmed = MessageBox.Show(
            LocalizationService.Get("CameraGroups_ConfirmDeleteText", selected.Count),
            LocalizationService.Get("CameraGroups_ConfirmDeleteTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var row in selected)
            {
                await api.DeleteCameraGroupAsync(row.Group.Id);
                Groups.Remove(row);
            }
            StatusMessage = LocalizationService.Get("CameraGroups_Status_Loaded", Groups.Count);
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

    public async Task ReloadAsync() => await LoadAsync();
}
