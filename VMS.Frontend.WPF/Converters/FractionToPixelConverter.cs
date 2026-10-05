using System.Globalization;
using System.Windows.Data;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Multiplies a fractional (0..1) coordinate by a canvas's current ActualWidth/Height — used to position E-map pins and the People Counting line's handles, both stored as fractions so they stay correctly placed regardless of the image's displayed size.</summary>
public class FractionToPixelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [double fraction, double extent])
        {
            return fraction * extent;
        }

        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
