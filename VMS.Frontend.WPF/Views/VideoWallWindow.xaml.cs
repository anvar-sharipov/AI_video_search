using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class VideoWallWindow : Window
{
    public VideoWallWindow()
    {
        InitializeComponent();
    }

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

    /// <summary>Builds the layout-mode Grid's RowDefinitions/ColumnDefinitions once from the
    /// display view model's Rows/Columns — WPF has no way to bind a variable number of
    /// definitions directly in XAML.</summary>
    private void LayoutGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid || grid.DataContext is not VideoWallDisplayViewModel vm || grid.RowDefinitions.Count > 0)
        {
            return;
        }

        for (var i = 0; i < vm.Rows; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
        }
        for (var i = 0; i < vm.Columns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
