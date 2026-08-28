using System.Windows;
using LibVLCSharp.Shared;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;
using VMS.Frontend.WPF.ViewModels;
using VMS.Frontend.WPF.Views;

namespace VMS.Frontend.WPF;

public partial class App : Application
{
    // Change this if the backend runs on another host/port.
    private const string BackendBaseAddress = "http://localhost:5080";

    private ApiClient _api = null!;
    private SessionService _session = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Software rendering: this client is commonly viewed over RDP into a
        // control-room PC, where WPF's default hardware/DirectX compositing
        // doesn't reliably reach the remote frame buffer.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        LibVLCSharp.Shared.Core.Initialize();

        _api = new ApiClient(BackendBaseAddress);
        _session = new SessionService();

        ShowLogin();
    }

    private void ShowLogin()
    {
        var loginVm = new LoginViewModel(_api, _session);
        var loginWindow = new LoginWindow { DataContext = loginVm };

        loginVm.LoginSucceeded += () =>
        {
            ShowMain();
            loginWindow.Close();
        };

        loginWindow.Show();
    }

    private void ShowMain()
    {
        var mainVm = new MainViewModel(_api, _session);
        var mainWindow = new MainWindow(_api) { DataContext = mainVm };
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = mainVm.LoadCamerasCommand.ExecuteAsync(null);
    }
}
