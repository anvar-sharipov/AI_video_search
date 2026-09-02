using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Selected tile gets a visibly thicker border, on top of the color change from BoolToTileBorderBrushConverter.</summary>
public class BoolToTileBorderThicknessConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new Thickness(3) : new Thickness(1);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
