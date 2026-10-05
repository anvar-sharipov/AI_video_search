using System.IO;
using System.Windows;
using Microsoft.Win32;
using VMS.Frontend.WPF.Services;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class AudioWindow : Window
{
    public AudioWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as AudioViewModel)?.Dispose();
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioViewModel vm)
        {
            return;
        }

        var dialog = new SaveFileDialog { Filter = "MP4|*.mp4", FileName = $"audio_{vm.ArchiveDate:yyyyMMdd}_{vm.ArchiveFromTime.Replace(':', '-')}.mp4" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var bytes = await vm.PrepareArchiveDownloadAsync();
        if (bytes is null)
        {
            return;
        }

        await File.WriteAllBytesAsync(dialog.FileName, bytes);
        vm.StatusMessage = LocalizationService.Get("Audio_DownloadSaved", dialog.FileName);
    }
}
