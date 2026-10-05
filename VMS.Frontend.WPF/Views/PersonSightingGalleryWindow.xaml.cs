using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class PersonSightingGalleryWindow : Window
{
    public PersonSightingGalleryWindow()
    {
        InitializeComponent();
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonSightingGalleryViewModel vm)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = (string)FindResource("PersonSightingGallery_ChooseFolderTitle") };
        if (dialog.ShowDialog(this) == true)
        {
            await vm.ExportAsync(dialog.FolderName);
        }
    }
}
