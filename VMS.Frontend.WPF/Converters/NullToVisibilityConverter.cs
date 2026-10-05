using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Visible when the bound value is non-null (e.g. a screenshot's byte[] loaded successfully); pass ConverterParameter="Invert" for the opposite — used to show a "no image" placeholder exactly when the real image isn't there.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is byte[] { Length: 0 });
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var show = invert ? isNull : !isNull;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
