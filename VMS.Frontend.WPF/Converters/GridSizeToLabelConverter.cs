using System.Globalization;
using System.Windows.Data;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.Converters;

/// <summary>MainViewModel.GridSize is 0 (Auto) or 1..8 (an NxN fixed layout) — this renders it as the grid-size dropdown's item labels.</summary>
public class GridSizeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int size)
        {
            return string.Empty;
        }

        return size == 0 ? LocalizationService.Get("Layout_Auto") : $"{size}x{size}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
