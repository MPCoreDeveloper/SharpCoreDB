using System.Globalization;
using Avalonia.Data.Converters;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converts an enum value to boolean based on parameter match.
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue && parameter is string param)
        {
            return enumValue.ToString() == param;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
