using System.Windows;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class VideoWallLauncherWindow : Window
{
    private readonly ApiClient _api;

    public VideoWallLauncherWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is VideoWallViewModel vm)
            {
                vm.RequestOpen += OnRequestOpen;
                vm.ManageLayoutsRequested += OnManageLayoutsRequested;
            }
        };
    }

    private void OnRequestOpen(MonitorOption monitor, VideoWallDisplayViewModel displayViewModel)
    {
        var bounds = monitor.Screen.Bounds;
        var wallWindow = new VideoWallWindow
        {
            DataContext = displayViewModel,
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            WindowState = System.Windows.WindowState.Normal
        };
        wallWindow.Show();
        wallWindow.WindowState = System.Windows.WindowState.Maximized;
    }

    private async void OnManageLayoutsRequested()
    {
        if (DataContext is not VideoWallViewModel vm)
        {
            return;
        }

        var layoutsVm = new VideoWallLayoutsViewModel(_api, vm.MainViewModel.Session);
        var window = new VideoWallLayoutsWindow(_api) { Owner = this, DataContext = layoutsVm };
        window.ShowDialog();

        await vm.ReloadLayoutsAsync();
    }
}
