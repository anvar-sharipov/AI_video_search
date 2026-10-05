using System.IO;
using System.Linq;
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
    private ThemeService _theme = null!;

    private LoginWindow? _loginWindow;
    private MainWindow? _mainWindow;
    private bool _handlingSessionExpiry;

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
        _api.SessionExpired += OnSessionExpired;
        _session = new SessionService();
        _localization = new LocalizationService();
        _localization.SetLanguage("ru");
        _theme = new ThemeService();
        _theme.ThemeChanged += OnThemeChanged;

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

    private void ShowLogin(string? initialErrorMessage = null)
    {
        var loginVm = new LoginViewModel(_api, _session, _localization, _theme);
        if (initialErrorMessage is not null)
        {
            loginVm.ErrorMessage = initialErrorMessage;
        }

        var loginWindow = new LoginWindow { DataContext = loginVm };
        _loginWindow = loginWindow;
        _mainWindow = null;

        loginVm.LoginSucceeded += () =>
        {
            ShowMain();
            _loginWindow?.Close();
        };

        loginWindow.Show();
    }

    /// <summary>
    /// The app has no refresh-token flow, so a 401 on any authenticated call (see
    /// ApiClient.SessionExpired) means the session is simply over — expired, or the account was
    /// deactivated mid-session. Every open window (the main one and any secondary Archive/User
    /// Management/etc. windows) becomes useless the moment that happens, since they all share the
    /// one ApiClient whose token just got cleared, so this drops straight back to the login screen
    /// instead of leaving whichever window happened to make that call stuck showing a raw
    /// "Unauthorized" error.
    /// </summary>
    private void OnSessionExpired()
    {
        if (_handlingSessionExpiry)
        {
            return;
        }

        _handlingSessionExpiry = true;
        Dispatcher.Invoke(() =>
        {
            try
            {
                _api.SetToken(null);
                _session.LogOut();

                // Show the new LoginWindow BEFORE closing the stale ones: ShutdownMode defaults to
                // OnLastWindowClose, so closing every window first — even just for the instant
                // before the new one opens — would tear the whole app down instead of returning to
                // the login screen.
                var staleWindows = Windows.OfType<Window>().ToList();
                ShowLogin(LocalizationService.Get("Api_SessionExpired"));
                foreach (var window in staleWindows)
                {
                    window.Close();
                }
            }
            finally
            {
                _handlingSessionExpiry = false;
            }
        });
    }

    private void ShowMain()
    {
        var mainVm = new MainViewModel(_api, _session, _localization, _theme);
        var mainWindow = new MainWindow(_api) { DataContext = mainVm };
        _mainWindow = mainWindow;
        _loginWindow = null;
        MainWindow = mainWindow;

        mainVm.LogOutRequested += () =>
        {
            _api.SetToken(null);
            _session.LogOut();
            ShowLogin();
            _mainWindow?.Close();
        };

        mainWindow.Show();
        _ = mainVm.LoadCamerasCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// A theme swap only restyles elements resolved through a Style (see ThemeService's remarks) —
    /// so the currently visible top-level window is recreated against the same, still-live
    /// ViewModel (no camera reload, no re-login) purely to force every StaticResource in its XAML
    /// to re-resolve against the newly active dictionary. Already-open secondary windows are left
    /// alone and pick up the new theme next time they're opened.
    /// </summary>
    private void OnThemeChanged()
    {
        if (_mainWindow is { DataContext: MainViewModel mainVm })
        {
            var old = _mainWindow;
            var newWindow = new MainWindow(_api)
            {
                DataContext = mainVm,
                Left = old.Left,
                Top = old.Top,
                Width = old.Width,
                Height = old.Height,
                WindowState = old.WindowState == WindowState.Maximized ? WindowState.Normal : old.WindowState,
            };
            _mainWindow = newWindow;
            MainWindow = newWindow;
            newWindow.Show();
            if (old.WindowState == WindowState.Maximized)
            {
                newWindow.WindowState = WindowState.Maximized;
            }

            old.DataContext = null;
            old.Close();
        }
        else if (_loginWindow is { DataContext: LoginViewModel loginVm })
        {
            var old = _loginWindow;
            var newWindow = new LoginWindow { DataContext = loginVm };
            _loginWindow = newWindow;
            newWindow.Show();

            old.DataContext = null;
            old.Close();
        }
    }
}
