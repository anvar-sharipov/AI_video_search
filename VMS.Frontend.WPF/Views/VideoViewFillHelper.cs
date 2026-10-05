using LibVLCSharp.WPF;

namespace VMS.Frontend.WPF.Views;

/// <summary>
/// LibVLC preserves the source video's aspect ratio by default, letterboxing/pillarboxing
/// inside whatever size the VideoView ends up with — most camera streams don't match a grid
/// tile's aspect ratio, so this shows as black bars down both sides. Telling the player the
/// target is exactly the VideoView's own current pixel size makes it stretch to fill that
/// rectangle exactly instead (trading a slightly distorted image for no wasted space), which is
/// what every video surface in this client is meant to do. Re-applied on every SizeChanged
/// since a tile's size isn't fixed (window resize, grid layout switch).
/// </summary>
public static class VideoViewFillHelper
{
    public static void EnableFill(VideoView view)
    {
        view.SizeChanged += (_, e) =>
        {
            if (view.MediaPlayer is { } player && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                player.AspectRatio = $"{(int)e.NewSize.Width}:{(int)e.NewSize.Height}";
            }
        };
    }
}
