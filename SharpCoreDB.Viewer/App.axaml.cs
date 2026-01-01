using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SharpCoreDB.Viewer.Services;
using SharpCoreDB.Viewer.ViewModels;
using SharpCoreDB.Viewer.Views;

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

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}