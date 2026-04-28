using Avalonia.Data.Converters;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converts DB cell values to decimal text and back for numeric editing.
/// </summary>
public sealed class ObjectToDecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || value == DBNull.Value)
        {
            return string.Empty;
        }

        if (value is decimal decimalValue)
        {
            return decimalValue.ToString(culture);
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double)
        {
            return System.Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(culture);
        }

        return value.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Any, culture, out var localizedDecimal))
        {
            return localizedDecimal;
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantDecimal))
        {
            return invariantDecimal;
        }

        return text;
    }
}
