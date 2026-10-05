using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class ArchiveWindow : Window
{
    public ArchiveWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveViewModel vm)
        {
            await vm.LoadCamerasCommand.ExecuteAsync(null);
        }
    }

    private void VideoView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is LibVLCSharp.WPF.VideoView videoView)
        {
            VideoViewFillHelper.EnableFill(videoView);
        }

        if (DataContext is ArchiveViewModel vm)
        {
            vm.NotifyViewLoaded();
        }
    }

    private void OnTimelineClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement track && DataContext is ArchiveViewModel vm && track.ActualWidth > 0)
        {
            var x = e.GetPosition(track).X;
            vm.SeekToFraction(x / track.ActualWidth);
        }
    }

    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Subtract the hosting Border's own Padding="8" on each side so the timeline
        // canvas/track never overflows it as the window is resized.
        if (DataContext is ArchiveViewModel vm && e.NewSize.Width > 20)
        {
            vm.TimelineWidth = e.NewSize.Width - 16;
        }
    }

    private void OnResultCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: SearchResultCardViewModel card } && DataContext is ArchiveViewModel vm)
        {
            vm.JumpToResultCommand.Execute(card);
        }
    }

    // --- Reverse Image tab: file picker + drag-and-drop, matching SearchByPhotoWindow's OpenFileDialog pattern ---

    private void OnChoosePhotoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
        if (dialog.ShowDialog() == true && DataContext is ArchiveViewModel vm)
        {
            vm.SetPhoto(dialog.FileName);
            UpdatePhotoPreview(dialog.FileName);
        }
    }

    private void OnPhotoDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is ArchiveViewModel vm)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var path = files.FirstOrDefault();
            if (path is not null)
            {
                vm.SetPhoto(path);
                UpdatePhotoPreview(path);
            }
        }
    }

    private void UpdatePhotoPreview(string path)
    {
        PhotoPreview.Source = new BitmapImage(new Uri(path));
    }

    private void OnPhotoDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // --- Player overlay: real snapshot-to-disk and crop-from-player-into-reverse-image-search ---

    private void OnTakeSnapshotClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveViewModel vm)
        {
            return;
        }

        var dialog = new SaveFileDialog { Filter = "JPEG image|*.jpg", FileName = $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.jpg" };
        if (dialog.ShowDialog() == true && !vm.SaveSnapshot(dialog.FileName))
        {
            vm.StatusMessage = Services.LocalizationService.Get("Archive_NothingToSnapshotError");
        }
    }

    private async void OnCropFromPlayerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveViewModel vm)
        {
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"archive-crop-{Guid.NewGuid():N}.jpg");
        if (!vm.SaveSnapshot(tempPath))
        {
            vm.StatusMessage = Services.LocalizationService.Get("Archive_NothingToSnapshotError");
            return;
        }

        // TakeSnapshot only queues the write on libvlc's own thread — unlike the existing
        // fire-and-forget live-view snapshot (MainViewModel.Snapshot), this flow reads the
        // file back immediately to show it in the crop window, so a short existence/size
        // poll is needed to avoid opening a not-yet-written or partially-written file.
        var ready = await WaitForFileReadyAsync(tempPath);
        if (!ready)
        {
            vm.StatusMessage = Services.LocalizationService.Get("Archive_NothingToSnapshotError");
            return;
        }

        var cropWindow = new ImageCropWindow(tempPath) { Owner = this };
        if (cropWindow.ShowDialog() == true && cropWindow.CropBox is { } box)
        {
            vm.SetPhotoWithCrop(tempPath, box);
            UpdatePhotoPreview(tempPath);
        }
    }

    private static async Task<bool> WaitForFileReadyAsync(string path)
    {
        for (var i = 0; i < 20; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
