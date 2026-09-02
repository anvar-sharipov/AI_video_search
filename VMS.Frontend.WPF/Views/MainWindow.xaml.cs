using System.Windows;
using System.Windows.Input;
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
                oldVm.AddCameraRequested -= OnAddCameraRequested;
                oldVm.ExpandRequested -= OnExpandRequested;
                oldVm.SearchByPhotoRequested -= OnSearchByPhotoRequested;
                oldVm.OperationLogRequested -= OnOperationLogRequested;
                oldVm.ArchiveRequested -= OnArchiveRequested;
            }

            if (args.NewValue is MainViewModel newVm)
            {
                newVm.ClipRequested += OnClipRequested;
                newVm.AddCameraRequested += OnAddCameraRequested;
                newVm.ExpandRequested += OnExpandRequested;
                newVm.SearchByPhotoRequested += OnSearchByPhotoRequested;
                newVm.OperationLogRequested += OnOperationLogRequested;
                newVm.ArchiveRequested += OnArchiveRequested;
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

    private void OnAddCameraRequested()
    {
        var addVm = new AddCameraViewModel(_api);
        var window = new AddCameraWindow { Owner = this, DataContext = addVm };

        addVm.Created += async () =>
        {
            window.Close();
            if (DataContext is MainViewModel mainVm)
            {
                await mainVm.LoadCamerasCommand.ExecuteAsync(null);
            }
        };

        window.ShowDialog();
    }
}
