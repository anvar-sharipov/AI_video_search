using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>DataContext for one open VideoWallWindow. Two modes: mirror mode just shows whatever
/// Live View currently shows (the original Video Wall behavior, kept as the default/fallback for
/// anyone who hasn't created a saved layout yet); layout mode shows a saved VideoWallLayoutDto's
/// cells, each resolved to a live camera by Code — never a new RTSP connection, only borrowing
/// the same CameraTileViewModel/Player MainViewModel.Cameras already has playing.</summary>
public partial class VideoWallDisplayViewModel : ObservableObject
{
    public MainViewModel MainViewModel { get; }

    [ObservableProperty] private bool _isMirrorMode;
    [ObservableProperty] private int _rows;
    [ObservableProperty] private int _columns;

    public ObservableCollection<VideoWallCellViewModel> Cells { get; } = [];

    public VideoWallDisplayViewModel(MainViewModel mainViewModel)
    {
        MainViewModel = mainViewModel;
        IsMirrorMode = true;
    }

    public VideoWallDisplayViewModel(MainViewModel mainViewModel, VideoWallLayoutDto layout)
    {
        MainViewModel = mainViewModel;
        IsMirrorMode = false;
        Rows = layout.Rows;
        Columns = layout.Columns;

        foreach (var cell in layout.Cells)
        {
            var cellVm = new VideoWallCellViewModel(cell.Id)
            {
                Row = cell.Row,
                Column = cell.Column,
                RowSpan = cell.RowSpan,
                ColumnSpan = cell.ColumnSpan,
                CameraId = cell.CameraId,
                CameraCode = cell.CameraCode,
                CameraName = cell.CameraName,
                Tile = cell.CameraCode is { } code ? mainViewModel.Cameras.FirstOrDefault(c => c.Code == code) : null
            };
            Cells.Add(cellVm);
        }
    }
}
