using Avalonia.Data.Converters;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converts DB cell values to DateTime text and back for date/time editing.
/// </summary>
public sealed class ObjectToDateTimeConverter : IValueConverter
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || value == DBNull.Value)
        {
            return string.Empty;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
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

        if (DateTime.TryParseExact(text, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var localized))
        {
            return localized;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant))
        {
            return invariant;
        }

        return text;
    }
}
