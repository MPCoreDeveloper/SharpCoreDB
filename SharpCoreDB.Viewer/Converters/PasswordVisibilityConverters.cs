using Avalonia.Data.Converters;
using Avalonia.Media;
using SharpCoreDB.Viewer.Services;
using System;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converters for password visibility toggle functionality
/// </summary>
public static class BoolConverters
{
    private static readonly LocalizationService _localization = LocalizationService.Instance;

    /// <summary>
    /// Converts boolean to PasswordChar: empty string (visible) or bullet (hidden)
    /// </summary>
    public static readonly IValueConverter PasswordCharConverter = 
        new FuncValueConverter<bool, char>(isVisible => isVisible ? '\0' : '?');

    /// <summary>
    /// Converts boolean to Eye icon: EyeIcon (visible) or EyeOffIcon (hidden)
    /// </summary>
    public static readonly IValueConverter EyeIconConverter = 
        new FuncValueConverter<bool, StreamGeometry?>(isVisible =>
        {
            var key = isVisible ? "EyeOffIcon" : "EyeIcon";
            if (Avalonia.Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
            {
                return resource as StreamGeometry;
            }
            return null;
        });

    /// <summary>
    /// Converts boolean to localized tooltip text
    /// </summary>
    public static readonly IValueConverter ShowHidePasswordTooltip = 
        new FuncValueConverter<bool, string>(isVisible => 
            isVisible ? _localization["HidePassword"] : _localization["ShowPassword"]);
}
