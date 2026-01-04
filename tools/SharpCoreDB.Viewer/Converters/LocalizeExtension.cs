using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Data;
using SharpCoreDB.Viewer.Services;
using SharpCoreDB.Viewer.Helpers;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Markup extension for localized strings in XAML that supports dynamic language switching
/// Usage: Text="{loc:Localize Connect}"
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Create a proxy that will notify when language changes
        var proxy = new LocalizationProxy(Key);
        
        // Create binding to the proxy's Value property
        var binding = new Avalonia.Data.Binding
        {
            Source = proxy,
            Path = "Value",
            Mode = BindingMode.OneWay
        };

        return binding;
    }
}

/// <summary>
/// Value converter for binding localized strings
/// Usage: Text="{Binding SomeKey, Converter={StaticResource LocalizeConverter}}"
/// </summary>
public class LocalizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            return LocalizationService.Instance[key];
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
