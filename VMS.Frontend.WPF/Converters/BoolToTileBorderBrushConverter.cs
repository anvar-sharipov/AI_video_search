using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Selected tile gets an amber border (active-tile highlight); everything else keeps the neutral grid line color.</summary>
public class BoolToTileBorderBrushConverter : IValueConverter
{
    // #FFB94B — amber selection highlight, matching the client's dark theme accent.
    private static readonly Brush Selected = new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x4B));
    private static readonly Brush Unselected = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x45));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Selected : Unselected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
