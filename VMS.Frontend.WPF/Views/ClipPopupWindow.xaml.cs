using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class ClipPopupWindow : Window
{
    public ClipPopupWindow()
    {
        InitializeComponent();
    }

    private void VideoView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is LibVLCSharp.WPF.VideoView videoView)
        {
            VideoViewFillHelper.EnableFill(videoView);
        }

        if (DataContext is ClipPopupViewModel vm)
        {
            vm.NotifyViewLoaded();
        }
    }
}
