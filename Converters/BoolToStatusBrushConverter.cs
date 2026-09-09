using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TempControl.Converters;

/// <summary>
/// Converts a bool status value directly to a green (true) or red (false) SolidColorBrush.
/// Replaces the unreliable Tag+DataTrigger pattern where WPF compares boxed bool with string "True".
/// </summary>
[ValueConversion(typeof(bool), typeof(SolidColorBrush))]
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x43, 0xA0, 0x47));
    private static readonly SolidColorBrush Red   = new(Color.FromRgb(0xE5, 0x39, 0x35));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Green : Red;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
