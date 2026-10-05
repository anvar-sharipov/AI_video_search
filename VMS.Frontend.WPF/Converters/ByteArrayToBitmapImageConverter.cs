using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace VMS.Frontend.WPF.Converters;

/// <summary>Decodes a byte[] (e.g. a downloaded thumbnail) into a frozen BitmapImage for binding to Image.Source in an ItemsControl, where the code-behind-sets-Source pattern used elsewhere in this client (EMapWindow, PeopleCountingWindow) doesn't scale to many list items.</summary>
public class ByteArrayToBitmapImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
