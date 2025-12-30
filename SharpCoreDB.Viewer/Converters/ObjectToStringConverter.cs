using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converter to safely convert any object to string for display in DataGrid.
/// Handles DBNull, null, DateTime, bool, and other types.
/// </summary>
public class ObjectToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[ObjectToStringConverter] Convert called - value type: {value?.GetType().Name ?? "NULL"}, value: {value}");
#endif
        
        if (value == null || value == DBNull.Value)
            return string.Empty;

        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss");

        if (value is bool b)
            return b ? "1" : "0";

        if (value is decimal dec)
            return dec.ToString("N2", culture);

        if (value is double dbl)
            return dbl.ToString("N2", culture);

        if (value is float flt)
            return flt.ToString("N2", culture);

        var result = value.ToString() ?? string.Empty;
        
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[ObjectToStringConverter] Returning: '{result}'");
#endif
        
        return result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
