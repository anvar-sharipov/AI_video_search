using System.IO;
using System.Windows;
using System.Windows.Threading;
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
    private const string CrashLogPath = "crash.log";

    private ApiClient _api = null!;
    private SessionService _session = null!;
    private LocalizationService _localization = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One bad camera stream should show an error, not take the whole app down —
        // without this, an unhandled exception on the UI thread (e.g. from a specific
        // camera's stream) kills the process outright, and Windows briefly shows the
        // dead window's last frame as a blank/white "ghost" before it disappears.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Software rendering: this client is commonly viewed over RDP into a control-room
        // PC, where WPF's default hardware/DirectX compositing doesn't reliably reach the
        // remote frame buffer. (Confirmed NOT the cause of the cam-2/cam-3 blank-video bug —
        // that reproduced identically with this on or off — so it stays on.)
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        LibVLCSharp.Shared.Core.Initialize();

        _api = new ApiClient(BackendBaseAddress);
        _session = new SessionService();
        _localization = new LocalizationService();
        _localization.SetLanguage("ru");

        ShowLogin();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        MessageBox.Show(
            LocalizationService.Get("Crash_MessageFormat", e.Exception.Message, Path.GetFullPath(CrashLogPath)),
            LocalizationService.Get("Crash_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            File.AppendAllText(CrashLogPath, $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch
        {
            // logging must never itself throw
        }
    }

    private void ShowLogin()
    {
        var loginVm = new LoginViewModel(_api, _session, _localization);
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
        var mainVm = new MainViewModel(_api, _session, _localization);
        var mainWindow = new MainWindow(_api) { DataContext = mainVm };
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = mainVm.LoadCamerasCommand.ExecuteAsync(null);
    }
}
