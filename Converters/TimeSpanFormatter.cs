using System.Globalization;
using System.Windows.Data;

namespace TempControl.Converters;

/// <summary>
/// Converts TimeSpan to a formatted string (HH:MM:SS).
/// WPF's StringFormat binding doesn't support TimeSpan's format specifiers,
/// so this converter provides the necessary formatting.
/// </summary>
public class TimeSpanFormatter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan timeSpan)
        {
            return timeSpan.ToString(@"hh\:mm\:ss");
        }
        return "00:00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
