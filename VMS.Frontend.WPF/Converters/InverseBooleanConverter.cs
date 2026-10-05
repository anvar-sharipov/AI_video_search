using System.Globalization;
using System.Windows.Data;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Negates a bool — used to disable a button (e.g. "Query") while its own IsBusy flag is true, so a slow request can't be fired twice.</summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}
