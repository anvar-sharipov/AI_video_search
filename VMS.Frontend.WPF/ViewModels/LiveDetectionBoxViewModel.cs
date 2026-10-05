using System.Windows.Media;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>One live bounding box drawn on a camera tile's overlay (see CameraTileViewModel.DetectionBoxes) — X/Y/Width/Height are fractional (0..1) against the source frame; the tile's Canvas overlay converts them to pixels using its own ActualWidth/ActualHeight.</summary>
public class LiveDetectionBoxViewModel(double x, double y, double width, double height, string? personName)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = width;
    public double Height { get; } = height;

    public string Label { get; } = personName ?? LocalizationService.Get("Overlay_UnknownPerson");

    public Brush StrokeBrush { get; } = personName is not null ? Brushes.LimeGreen : Brushes.OrangeRed;
}
