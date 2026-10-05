using System.Globalization;
using System.Windows.Data;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Combines Camera.IsEnabled + Camera.Running into one human-readable status: Disabled / Online / Offline.</summary>
public class CameraStatusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isEnabled = values.Length > 0 && values[0] is bool b1 && b1;
        var isRunning = values.Length > 1 && values[1] is bool b2 && b2;

        if (!isEnabled)
        {
            return LocalizationService.Get("CameraManagement_StatusDisabled");
        }
        return isRunning
            ? LocalizationService.Get("CameraManagement_StatusOnline")
            : LocalizationService.Get("CameraManagement_StatusOffline");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
