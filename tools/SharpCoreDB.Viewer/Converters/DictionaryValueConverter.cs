using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converter to extract values from Dictionary<string, object> for DataGrid binding.
/// </summary>
public class DictionaryValueConverter : IValueConverter
{
    public string? ColumnName { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[DictionaryValueConverter] Convert called - value type: {value?.GetType().Name ?? "NULL"}, parameter: {parameter}");
#endif
        
        if (value is Dictionary<string, object> dict)
        {
            var key = parameter as string ?? ColumnName;
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[DictionaryValueConverter] Dictionary has {dict.Count} keys, looking for key: '{key}'");
#endif
            
            if (key != null && dict.TryGetValue(key, out var val))
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[DictionaryValueConverter] Found value: {val} (type: {val?.GetType().Name ?? "NULL"})");
#endif
                
                // Convert DBNull to empty string
                if (val == DBNull.Value || val == null)
                    return string.Empty;
                
                return val.ToString() ?? string.Empty;
            }
            else
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[DictionaryValueConverter] Key '{key}' NOT FOUND in dictionary. Available keys: {string.Join(", ", dict.Keys)}");
#endif
            }
        }
        else
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[DictionaryValueConverter] Value is NOT a Dictionary!");
#endif
        }
        
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
