using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VMS.Frontend.WPF.Services;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class PeopleCountingWindow : Window
{
    private Polyline? _polyline;
    private int _draggingIndex = -1;

    public PeopleCountingWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RedrawLine();
        LineCanvas.SizeChanged += (_, _) => RedrawLine();

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is PeopleCountingViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
                oldVm.Points.CollectionChanged -= OnPointsChanged;
            }

            if (args.NewValue is PeopleCountingViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
                newVm.Points.CollectionChanged += OnPointsChanged;
                UpdatePreviewImage(newVm.SnapshotBytes);
                RedrawLine();
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PeopleCountingViewModel.SnapshotBytes) && DataContext is PeopleCountingViewModel vm)
        {
            UpdatePreviewImage(vm.SnapshotBytes);
        }
    }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawLine();

    private void UpdatePreviewImage(byte[]? bytes)
    {
        if (bytes is null)
        {
            PreviewImage.Source = null;
            return;
        }

        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        PreviewImage.Source = bitmap;
    }

    /// <summary>Draws the counting tripwire (a polyline of 2+ points) plus one draggable handle
    /// per point over the snapshot, converting the ViewModel's fractional (0..1) coordinates to
    /// the canvas's current pixel size. The first point is green, the last is blue, and any points
    /// in between (added via double-click) are orange, purely so the drawn direction is legible at
    /// a glance — LeftToRightIsIn is evaluated per-segment, in point order.</summary>
    private void RedrawLine()
    {
        if (DataContext is not PeopleCountingViewModel vm || LineCanvas.ActualWidth <= 0 || LineCanvas.ActualHeight <= 0)
        {
            return;
        }

        LineCanvas.Children.Clear();

        var w = LineCanvas.ActualWidth;
        var h = LineCanvas.ActualHeight;
        var pixelPoints = vm.Points.Select(p => new System.Windows.Point(p.X * w, p.Y * h)).ToList();

        _polyline = new Polyline { Stroke = Brushes.OrangeRed, StrokeThickness = 3, Points = new PointCollection(pixelPoints) };
        LineCanvas.Children.Add(_polyline);

        for (var i = 0; i < pixelPoints.Count; i++)
        {
            var color = i == 0 ? Brushes.LimeGreen : i == pixelPoints.Count - 1 ? Brushes.DodgerBlue : Brushes.Orange;
            LineCanvas.Children.Add(CreateHandle(pixelPoints[i], i, color));
        }
    }

    private Ellipse CreateHandle(System.Windows.Point center, int index, Brush fill)
    {
        const double radius = 8;
        var ellipse = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = fill,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Cursor = Cursors.Hand,
            ToolTip = LocalizationService.Get("PeopleCounting_RemovePointTooltip")
        };
        Canvas.SetLeft(ellipse, center.X - radius);
        Canvas.SetTop(ellipse, center.Y - radius);
        ellipse.MouseLeftButtonDown += (_, e) =>
        {
            _draggingIndex = index;
            ellipse.CaptureMouse();
            e.Handled = true;
        };
        ellipse.MouseRightButtonDown += (_, e) =>
        {
            // Keep at least 2 points — a 1-point "line" has no direction, and the tracker requires >=2 to save at all.
            if (DataContext is PeopleCountingViewModel vm && vm.Points.Count > 2)
            {
                vm.Points.RemoveAt(index);
            }
            e.Handled = true;
        };
        return ellipse;
    }

    private void LineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // A handle's own MouseLeftButtonDown above runs first and marks the event Handled, so
        // reaching here means the click was on empty canvas — append a new point there, extending
        // the tripwire span by span instead of only ever having exactly 2 ends.
        if (DataContext is not PeopleCountingViewModel vm || LineCanvas.ActualWidth <= 0)
        {
            return;
        }

        var pos = e.GetPosition(LineCanvas);
        var fx = Math.Clamp(pos.X / LineCanvas.ActualWidth, 0, 1);
        var fy = Math.Clamp(pos.Y / LineCanvas.ActualHeight, 0, 1);
        vm.Points.Add((fx, fy));
    }

    private void LineCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingIndex < 0 || DataContext is not PeopleCountingViewModel vm || LineCanvas.ActualWidth <= 0 || _draggingIndex >= vm.Points.Count)
        {
            return;
        }

        var pos = e.GetPosition(LineCanvas);
        var fx = Math.Clamp(pos.X / LineCanvas.ActualWidth, 0, 1);
        var fy = Math.Clamp(pos.Y / LineCanvas.ActualHeight, 0, 1);
        vm.Points[_draggingIndex] = (fx, fy);
    }

    private void LineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingIndex = -1;
        Mouse.Capture(null);
    }
}
