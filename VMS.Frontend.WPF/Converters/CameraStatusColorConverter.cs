using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Same three-state logic as CameraStatusConverter, but returns the status dot's color instead of its text.</summary>
public class CameraStatusColorConverter : IMultiValueConverter
{
    private static readonly Brush Online = new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64));
    private static readonly Brush Offline = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush Disabled = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x69));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isEnabled = values.Length > 0 && values[0] is bool b1 && b1;
        var isRunning = values.Length > 1 && values[1] is bool b2 && b2;

        if (!isEnabled)
        {
            return Disabled;
        }
        return isRunning ? Online : Offline;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
