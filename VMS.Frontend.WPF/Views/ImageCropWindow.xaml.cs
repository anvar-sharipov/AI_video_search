using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VMS.Frontend.WPF.Views;

/// <summary>
/// Lets the user drag a rough rectangle over a snapshot taken from the archive player
/// ("crop from player" on the Reverse Image search tab) and reports it back in the
/// original image's own pixel coordinates, regardless of how much the preview was
/// scaled down to fit on screen.
/// </summary>
public partial class ImageCropWindow : Window
{
    private const double MaxPreviewDimension = 800;

    private readonly double _scale;
    private Point _dragStart;
    private bool _dragging;

    public (int X, int Y, int Width, int Height)? CropBox { get; private set; }

    public ImageCropWindow(string imagePath)
    {
        InitializeComponent();

        var bitmap = new BitmapImage();
        using (var stream = File.OpenRead(imagePath))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();

        _scale = Math.Min(1.0, MaxPreviewDimension / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        var renderWidth = bitmap.PixelWidth * _scale;
        var renderHeight = bitmap.PixelHeight * _scale;

        ImageCanvas.Width = renderWidth;
        ImageCanvas.Height = renderHeight;
        PhotoImage.Width = renderWidth;
        PhotoImage.Height = renderHeight;
        PhotoImage.Source = bitmap;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ImageCanvas);
        _dragging = true;
        Canvas.SetLeft(SelectionRect, _dragStart.X);
        Canvas.SetTop(SelectionRect, _dragStart.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
        OkButton.IsEnabled = false;
        ImageCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var current = e.GetPosition(ImageCanvas);
        var x = Math.Min(_dragStart.X, current.X);
        var y = Math.Min(_dragStart.Y, current.Y);
        var width = Math.Abs(current.X - _dragStart.X);
        var height = Math.Abs(current.Y - _dragStart.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ImageCanvas.ReleaseMouseCapture();
        OkButton.IsEnabled = SelectionRect.Width > 4 && SelectionRect.Height > 4;
    }

    private void OnUseSelectionClick(object sender, RoutedEventArgs e)
    {
        var x = Canvas.GetLeft(SelectionRect);
        var y = Canvas.GetTop(SelectionRect);
        CropBox = (
            (int)(x / _scale),
            (int)(y / _scale),
            (int)(SelectionRect.Width / _scale),
            (int)(SelectionRect.Height / _scale));
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
