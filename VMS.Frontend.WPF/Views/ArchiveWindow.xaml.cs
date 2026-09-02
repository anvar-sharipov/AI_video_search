using System.Windows;
using System.Windows.Input;
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

    private void OnTimelineClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement track && DataContext is ArchiveViewModel vm && track.ActualWidth > 0)
        {
            var x = e.GetPosition(track).X;
            vm.SeekToFraction(x / track.ActualWidth);
        }
    }
}
