using System.Windows;
using System.Windows.Media.Imaging;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class SearchByPhotoWindow : Window
{
    public SearchByPhotoWindow()
    {
        InitializeComponent();
    }

    private void OnChooseFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp"
        };

        if (dialog.ShowDialog() != true || DataContext is not SearchByPhotoViewModel vm)
        {
            return;
        }

        vm.SetPhoto(dialog.FileName);
        Preview.Source = new BitmapImage(new Uri(dialog.FileName));
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchByPhotoViewModel vm)
        {
            await vm.SearchCommand.ExecuteAsync(null);
        }
    }
}
