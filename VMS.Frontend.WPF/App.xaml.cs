using System.IO;
using System.Linq;
using System.Text.Json;
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
    // Where the backend lives. Defaults to this same machine, which is what the full installer
    // produces (backend service + client on one PC). An operator workstation that talks to a
    // server elsewhere on the LAN overrides it in vms-client.json next to the exe, so pointing
    // the client at another server never needs a rebuild.
    private const string DefaultBackendBaseAddress = "http://localhost:5080";
    private const string ClientConfigFileName = "vms-client.json";
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

        _api = new ApiClient(ResolveBackendBaseAddress());
        _api.SessionExpired += OnSessionExpired;
        _api.TokenRefreshed += OnTokenRefreshed;
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

    /// <summary>
    /// Reads BackendBaseAddress out of vms-client.json next to the exe, falling back to
    /// localhost. Read from AppContext.BaseDirectory rather than the working directory: a
    /// Start-menu/desktop shortcut launches the exe with an arbitrary working directory, so
    /// anything relative to it would be found in development and missed once installed.
    /// </summary>
    private static string ResolveBackendBaseAddress()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ClientConfigFileName);
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("BackendBaseAddress", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!.TrimEnd('/');
                }
            }
        }
        catch (Exception ex)
        {
            // A corrupt config file must not block login entirely — log it and use the default.
            LogCrash(ex);
        }

        return DefaultBackendBaseAddress;
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
    /// ApiClient.SessionExpired firing means the session is truly over — the refresh token
    /// itself is missing/expired/revoked, or the account was deactivated — not just that the
    /// short-lived access token happened to expire (ApiClient refreshes that silently in the
    /// background well before it would). Every open window (the main one and any secondary
    /// Archive/User Management/etc. windows) becomes useless the moment this happens, since they
    /// all share the one ApiClient whose token just got cleared, so this drops straight back to
    /// the login screen instead of leaving whichever window happened to make that call stuck
    /// showing a raw "Unauthorized" error.
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

    /// <summary>Raised off a background timer thread (ApiClient's silent refresh), not the UI
    /// thread — Dispatcher.Invoke here, same as OnSessionExpired above, since SessionService's
    /// ObservableProperty setters must run on the UI thread.</summary>
    private void OnTokenRefreshed(LoginResponseDto result) =>
        Dispatcher.Invoke(() => _session.ApplyLogin(result));

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
