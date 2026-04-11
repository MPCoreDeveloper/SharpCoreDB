using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SharpCoreDB.Viewer.Services;
using SharpCoreDB.Viewer.ViewModels;
using SharpCoreDB.Viewer.Views;
using System.Collections;
using System.Reflection;

namespace SharpCoreDB.Viewer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            DisableAvaloniaDataAnnotationValidation();
            
            // Load and apply settings (auto-detects system language on first startup)
            var settingsService = SettingsService.Instance;
            settingsService.ApplySettings();
            ApplyTheme(settingsService.Settings.Theme);
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[App] Starting with language: {settingsService.Settings.Language}");
            System.Diagnostics.Debug.WriteLine($"[App] Starting with theme: {settingsService.Settings.Theme}");
#endif
            
            // Subscribe to settings changes
            settingsService.SettingsChanged += (s, settings) =>
            {
                ApplyTheme(settings.Theme);
            };
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(string themeName)
    {
        RequestedThemeVariant = themeName switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var bindingPluginsType = Type.GetType("Avalonia.Data.Core.Plugins.BindingPlugins, Avalonia.Base", throwOnError: false);
        var dataValidatorsProperty = bindingPluginsType?.GetProperty("DataValidators", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (dataValidatorsProperty?.GetValue(null) is not IList validators)
        {
            return;
        }

        for (var i = validators.Count - 1; i >= 0; i--)
        {
            if (validators[i]?.GetType().Name is "DataAnnotationsValidationPlugin")
            {
                validators.RemoveAt(i);
            }
        }
    }
}
