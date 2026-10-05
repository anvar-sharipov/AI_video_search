using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Backs the Video Wall launcher: pick a connected monitor and a saved layout (or the
/// "mirror Live View" default), open the chosen combination full-screen on that monitor.</summary>
public partial class VideoWallViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public MainViewModel MainViewModel { get; }
    public ObservableCollection<MonitorOption> Monitors { get; } = [];
    public ObservableCollection<VideoWallLayoutOption> Layouts { get; } = [];

    [ObservableProperty] private MonitorOption? _selectedMonitor;
    [ObservableProperty] private VideoWallLayoutOption? _selectedLayout;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>The View opens the wall window on RequestOpen since a Window belongs in the View layer, not here.</summary>
    public event Action<MonitorOption, VideoWallDisplayViewModel>? RequestOpen;

    /// <summary>The View owns opening the layouts management window, same split as RequestOpen.</summary>
    public event Action? ManageLayoutsRequested;

    public VideoWallViewModel(MainViewModel mainViewModel, ApiClient api)
    {
        MainViewModel = mainViewModel;
        _api = api;

        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            Monitors.Add(new MonitorOption(i, screens[i]));
        }
        SelectedMonitor = Monitors.FirstOrDefault(m => m.Screen.Primary) ?? Monitors.FirstOrDefault();

        Layouts.Add(VideoWallLayoutOption.MirrorLiveView);
        SelectedLayout = Layouts[0];
        _ = LoadLayoutsAsync();
    }

    private async Task LoadLayoutsAsync()
    {
        try
        {
            var layouts = await _api.GetVideoWallLayoutsAsync();
            foreach (var layout in layouts)
            {
                Layouts.Add(new VideoWallLayoutOption(layout.Id, layout.Name));
            }
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (SelectedMonitor is null)
        {
            return;
        }

        if (SelectedLayout is null || SelectedLayout.Id is null)
        {
            RequestOpen?.Invoke(SelectedMonitor, new VideoWallDisplayViewModel(MainViewModel));
            return;
        }

        try
        {
            var layout = await _api.GetVideoWallLayoutAsync(SelectedLayout.Id.Value);
            RequestOpen?.Invoke(SelectedMonitor, new VideoWallDisplayViewModel(MainViewModel, layout));
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ManageLayouts() => ManageLayoutsRequested?.Invoke();

    /// <summary>Called after the layouts management window closes, so a newly created/renamed/
    /// deleted layout shows up here without reopening the launcher.</summary>
    public async Task ReloadLayoutsAsync()
    {
        var previouslySelectedId = SelectedLayout?.Id;
        Layouts.Clear();
        Layouts.Add(VideoWallLayoutOption.MirrorLiveView);
        await LoadLayoutsAsync();
        SelectedLayout = Layouts.FirstOrDefault(l => l.Id == previouslySelectedId) ?? Layouts[0];
    }
}

public record MonitorOption(int Index, System.Windows.Forms.Screen Screen)
{
    public string DisplayName => $"{Index + 1}: {Screen.DeviceName} ({Screen.Bounds.Width}x{Screen.Bounds.Height}){(Screen.Primary ? " *" : "")}";
}

/// <summary>Layout dropdown entry — Id is null for the synthetic "mirror Live View" option.</summary>
public record VideoWallLayoutOption(Guid? Id, string Name)
{
    public static VideoWallLayoutOption MirrorLiveView { get; } = new(null, LocalizationService.Get("VideoWall_MirrorLiveView"));
}
