using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class MainWindow : Window
{
    private readonly ApiClient _api;

    public MainWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainViewModel oldVm)
            {
                oldVm.ClipRequested -= OnClipRequested;
                oldVm.ExpandRequested -= OnExpandRequested;
                oldVm.SearchByPhotoRequested -= OnSearchByPhotoRequested;
                oldVm.OperationLogRequested -= OnOperationLogRequested;
                oldVm.ArchiveRequested -= OnArchiveRequested;
                oldVm.CameraManagementRequested -= OnCameraManagementRequested;
                oldVm.UserManagementRequested -= OnUserManagementRequested;
                oldVm.VideoWallRequested -= OnVideoWallRequested;
                oldVm.AudioRequested -= OnAudioRequested;
                oldVm.EMapRequested -= OnEMapRequested;
                oldVm.PeopleCountingRequested -= OnPeopleCountingRequested;
                oldVm.PersonSightingReportRequested -= OnPersonSightingReportRequested;
                oldVm.AlarmRecordsRequested -= OnAlarmRecordsRequested;
                oldVm.FaceRecognitionRequested -= OnFaceRecognitionRequested;
                oldVm.CameraGroupsRequested -= OnCameraGroupsRequested;
                oldVm.QuickGroupSwitchRequested -= OnQuickGroupSwitchRequested;
            }

            if (args.NewValue is MainViewModel newVm)
            {
                newVm.ClipRequested += OnClipRequested;
                newVm.ExpandRequested += OnExpandRequested;
                newVm.SearchByPhotoRequested += OnSearchByPhotoRequested;
                newVm.OperationLogRequested += OnOperationLogRequested;
                newVm.ArchiveRequested += OnArchiveRequested;
                newVm.CameraManagementRequested += OnCameraManagementRequested;
                newVm.UserManagementRequested += OnUserManagementRequested;
                newVm.VideoWallRequested += OnVideoWallRequested;
                newVm.AudioRequested += OnAudioRequested;
                newVm.EMapRequested += OnEMapRequested;
                newVm.PeopleCountingRequested += OnPeopleCountingRequested;
                newVm.PersonSightingReportRequested += OnPersonSightingReportRequested;
                newVm.AlarmRecordsRequested += OnAlarmRecordsRequested;
                newVm.FaceRecognitionRequested += OnFaceRecognitionRequested;
                newVm.CameraGroupsRequested += OnCameraGroupsRequested;
                newVm.QuickGroupSwitchRequested += OnQuickGroupSwitchRequested;
            }
        };

        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    private void OnClipRequested(SearchResultViewModel result)
    {
        var clipVm = new ClipPopupViewModel(_api, result);
        var popup = new ClipPopupWindow { Owner = this, DataContext = clipVm };
        popup.Closed += (_, _) => clipVm.Dispose();
        popup.Show();
    }

    /// <summary>
    /// Sized and positioned to exactly cover the camera grid area — not the whole screen — so
    /// the top bar and the search side panel both stay visible while a camera is expanded, per
    /// the user's request. Still a separate top-level Window (not an overlay inside this one):
    /// that's what a fresh VideoView/MediaPlayer needs to avoid the cam-02/cam-03 white-screen
    /// bug this session fixed (see ExpandedCameraViewModel) — only the sizing/position changed.
    /// </summary>
    private void OnExpandRequested(CameraTileViewModel tile)
    {
        if (DataContext is not MainViewModel mainVm)
        {
            return;
        }

        var expandVm = mainVm.CreateExpandedViewModel(tile);
        var window = new ExpandedCameraWindow { Owner = this, DataContext = expandVm };

        var topLeftDevice = CameraGrid.PointToScreen(new Point(0, 0));
        var source = PresentationSource.FromVisual(this);
        var topLeftDip = source is not null
            ? source.CompositionTarget.TransformFromDevice.Transform(topLeftDevice)
            : topLeftDevice;

        window.Left = topLeftDip.X;
        window.Top = topLeftDip.Y;
        window.Width = CameraGrid.ActualWidth;
        window.Height = CameraGrid.ActualHeight;

        window.Closed += (_, _) => expandVm.Dispose();
        window.Show();
    }

    /// <summary>Fires once a grid tile's VideoView is realized (native Hwnd exists) — only then is
    /// it safe to start decoding into it; see CameraTileViewModel's _viewReady field comment.</summary>
    private void TileVideoView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is LibVLCSharp.WPF.VideoView videoView)
        {
            VideoViewFillHelper.EnableFill(videoView);
        }

        if (sender is FrameworkElement { DataContext: CameraTileViewModel tile })
        {
            tile.NotifyViewLoaded();
        }
    }

    /// <summary>Double-click a tile's header to expand it — the header, not the video, per the
    /// airspace note on the header Button in MainWindow.xaml (a native HwndHost video surface
    /// swallows mouse input meant for WPF content drawn over it).</summary>
    private void CameraTileHeader_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CameraTileViewModel tile } && DataContext is MainViewModel mainVm)
        {
            mainVm.ToggleExpandCommand.Execute(tile);
        }
    }

    /// <summary>Click the video body itself (not just the header): selects the tile like the
    /// header does, and a double-click also expands it — same behavior as the header, just
    /// reachable from the video area too.</summary>
    private void CameraTileVideo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CameraTileViewModel tile } || DataContext is not MainViewModel mainVm)
        {
            return;
        }

        mainVm.SelectTileCommand.Execute(tile);
        if (e.ClickCount == 2)
        {
            mainVm.ToggleExpandCommand.Execute(tile);
        }
    }

    /// <summary>Double-click a camera in the sidebar tree: assigns it into the currently-selected
    /// grid cell (fixed layouts only — see MainViewModel.AssignCameraToSelectedSlot).</summary>
    private void TreeCameraItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CameraTileViewModel tile } && DataContext is MainViewModel mainVm)
        {
            mainVm.AssignCameraToSelectedSlotCommand.Execute(tile);
        }
    }

    private void OnSearchByPhotoRequested()
    {
        var searchVm = new SearchByPhotoViewModel(_api);
        var window = new SearchByPhotoWindow { Owner = this, DataContext = searchVm };

        searchVm.Found += results =>
        {
            window.Close();
            if (DataContext is MainViewModel mainVm)
            {
                mainVm.ApplySearchByFaceResults(results);
            }
        };

        window.ShowDialog();
    }

    private void OnArchiveRequested()
    {
        var archiveVm = new ArchiveViewModel(_api);
        var window = new ArchiveWindow { Owner = this, DataContext = archiveVm };
        window.Closed += (_, _) => archiveVm.Dispose();
        window.Show();
    }

    private void OnOperationLogRequested()
    {
        var logVm = new OperationLogViewModel(_api);
        var window = new OperationLogWindow { Owner = this, DataContext = logVm };
        window.ShowDialog();
    }

    /// <summary>Closes the search-results dropdown on any click outside it or the search box —
    /// both are plain elements in this window's own visual tree (see SearchResultsPanel's XAML
    /// comment for why it's not a Popup), so a click on either of them reaches this handler like
    /// any other click; they're excluded below so using the search UI doesn't self-close the
    /// dropdown it just opened.</summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm || !mainVm.IsSearchResultsOpen)
        {
            return;
        }

        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element == SearchAreaPanel || element == SearchResultsPanel)
            {
                return;
            }
            element = VisualTreeHelper.GetParent(element);
        }

        mainVm.IsSearchResultsOpen = false;
    }

    private void OnVideoWallRequested()
    {
        if (DataContext is not MainViewModel mainVm)
        {
            return;
        }

        var wallVm = new VideoWallViewModel(mainVm, _api);
        var window = new VideoWallLauncherWindow(_api) { Owner = this, DataContext = wallVm };
        window.Show();
    }

    private void OnAudioRequested()
    {
        var audioVm = new AudioViewModel(_api);
        var window = new AudioWindow { Owner = this, DataContext = audioVm };
        window.Show();
    }

    private void OnEMapRequested()
    {
        var mapVm = new EMapViewModel(_api);
        var window = new EMapWindow { Owner = this, DataContext = mapVm };
        mapVm.CameraPinActivated += cameraCode =>
        {
            if (DataContext is MainViewModel mainVm)
            {
                var tile = mainVm.Cameras.FirstOrDefault(c => c.Code == cameraCode);
                if (tile is not null)
                {
                    var expandVm = mainVm.CreateExpandedViewModel(tile);
                    var expandWindow = new ExpandedCameraWindow { Owner = window, DataContext = expandVm };
                    expandWindow.Closed += (_, _) => expandVm.Dispose();
                    expandWindow.Show();
                }
            }
        };
        window.Show();
    }

    private void OnPeopleCountingRequested()
    {
        var countingVm = new PeopleCountingViewModel(_api);
        var window = new PeopleCountingWindow { Owner = this, DataContext = countingVm };
        window.Show();
    }

    private void OnPersonSightingReportRequested()
    {
        var reportVm = new PersonSightingReportViewModel(_api);
        var window = new PersonSightingReportWindow(_api) { Owner = this, DataContext = reportVm };
        window.Show();
    }

    private void OnAlarmRecordsRequested()
    {
        var alarmsVm = new AlarmRecordsViewModel(_api);
        var window = new AlarmRecordsWindow { Owner = this, DataContext = alarmsVm };
        window.Show();
    }

    private void OnFaceRecognitionRequested()
    {
        var faceVm = new FaceRecognitionViewModel(_api);
        var window = new FaceRecognitionWindow(_api) { Owner = this, DataContext = faceVm };
        window.Show();
    }

    private void OnCameraManagementRequested()
    {
        var mgmtVm = new CameraManagementViewModel(_api);
        var window = new CameraManagementWindow(_api) { Owner = this, DataContext = mgmtVm };

        mgmtVm.CamerasChanged += async () =>
        {
            if (DataContext is MainViewModel mainVm)
            {
                await mainVm.LoadCamerasCommand.ExecuteAsync(null);
            }
        };

        window.Show();
    }

    private void OnUserManagementRequested()
    {
        if (DataContext is not MainViewModel mainVm)
        {
            return;
        }

        var usersVm = new UserManagementViewModel(_api);
        var window = new UserManagementWindow(_api, mainVm.Session) { Owner = this, DataContext = usersVm };
        window.Show();
    }

    private void OnCameraGroupsRequested()
    {
        if (DataContext is not MainViewModel mainVm)
        {
            return;
        }

        var groupsVm = new CameraGroupsViewModel(_api, mainVm.Session);
        var window = new CameraGroupsWindow(_api) { Owner = this, DataContext = groupsVm };
        window.Show();
    }

    private void OnQuickGroupSwitchRequested()
    {
        if (DataContext is not MainViewModel mainVm)
        {
            return;
        }

        var window = new QuickGroupSwitchWindow(_api, mainVm) { Owner = this };
        window.ShowDialog();
    }
}
