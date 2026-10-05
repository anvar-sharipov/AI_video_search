using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

public partial class VideoWallLayoutRowViewModel(VideoWallLayoutDto layout) : ObservableObject
{
    public VideoWallLayoutDto Layout { get; } = layout;
    public string SizeSummary => $"{Layout.Rows}x{Layout.Columns}";
    public string CameraSummary => string.Join(", ", Layout.Cells.Where(c => c.CameraName is not null).Select(c => c.CameraName));

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Backs the Video Wall Layouts management screen. Mirrors CameraGroupsViewModel's
/// checkbox-row-select + Edit/Delete pattern exactly.</summary>
public partial class VideoWallLayoutsViewModel(ApiClient api, SessionService session) : ObservableObject
{
    public SessionService Session { get; } = session;

    public ObservableCollection<VideoWallLayoutRowViewModel> Layouts { get; } = [];

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Raised when exactly one layout is checked and Edit is clicked; the Window's code-behind owns opening EditVideoWallLayoutWindow.</summary>
    public event Action<VideoWallLayoutDto>? EditRequested;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = LocalizationService.Get("VideoWallLayouts_Status_Loading");
        try
        {
            var layouts = await api.GetVideoWallLayoutsAsync();
            Layouts.Clear();
            foreach (var l in layouts)
            {
                Layouts.Add(new VideoWallLayoutRowViewModel(l));
            }
            StatusMessage = LocalizationService.Get("VideoWallLayouts_Status_Loaded", Layouts.Count);
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
        var selected = Layouts.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("VideoWallLayouts_SelectLayoutFirst");
            return;
        }
        if (selected.Count > 1)
        {
            StatusMessage = LocalizationService.Get("VideoWallLayouts_SelectOneForEdit");
            return;
        }

        EditRequested?.Invoke(selected[0].Layout);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = Layouts.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationService.Get("VideoWallLayouts_SelectLayoutFirst");
            return;
        }

        var confirmed = MessageBox.Show(
            LocalizationService.Get("VideoWallLayouts_ConfirmDeleteText", selected.Count),
            LocalizationService.Get("VideoWallLayouts_ConfirmDeleteTitle"),
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
                await api.DeleteVideoWallLayoutAsync(row.Layout.Id);
                Layouts.Remove(row);
            }
            StatusMessage = LocalizationService.Get("VideoWallLayouts_Status_Loaded", Layouts.Count);
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
