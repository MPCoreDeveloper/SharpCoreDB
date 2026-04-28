using Avalonia.Data.Converters;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converts arbitrary DB cell values to bool and back for checkbox editors.
/// </summary>
public sealed class ObjectToBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || value == DBNull.Value)
        {
            return false;
        }

        if (value is bool booleanValue)
        {
            return booleanValue;
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
        {
            return System.Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (bool.TryParse(text, out var parsedBool))
        {
            return parsedBool;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
        {
            return parsedInt != 0;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        return false;
    }
}
