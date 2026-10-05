using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Opposite of the built-in BooleanToVisibilityConverter — true collapses, false shows.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
