using SharpCoreDB.Viewer.Models;
using System.Text.Json;
using System.Globalization;

namespace SharpCoreDB.Viewer.Services;

/// <summary>
/// Service for persisting and loading application settings
/// </summary>
public class SettingsService
{
    private static SettingsService? _instance;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };
    private readonly string _settingsPath;
    private AppSettings _settings;

    // Supported languages in the application
    private static readonly string[] SupportedLanguages = 
    [
        "en-US",
        "nl-NL",
        "de-DE",
        "fr-FR",
        "es-ES",
        "it-IT"
    ];

    public static SettingsService Instance => _instance ??= new SettingsService();

    public event EventHandler<AppSettings>? SettingsChanged;

    private SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "SharpCoreDB.Viewer");
        Directory.CreateDirectory(appFolder);
        
        _settingsPath = Path.Combine(appFolder, "settings.json");
        _settings = LoadSettings();
    }

    public AppSettings Settings => _settings;

    public void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        
        File.WriteAllText(_settingsPath, json);
        SettingsChanged?.Invoke(this, settings);
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                
                if (settings != null)
                {
                    // Validate that the saved language is still supported
                    if (!SupportedLanguages.Contains(settings.Language))
                    {
                        settings.Language = GetDefaultLanguage();
                    }
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }

        // First time startup - use system language or fallback to English
        return new AppSettings
        {
            Language = GetDefaultLanguage(),
            Theme = "Light"
        };
    }

    /// <summary>
    /// Gets the default language based on system culture.
    /// Falls back to "en-US" if system language is not supported.
    /// </summary>
    private static string GetDefaultLanguage()
    {
        var systemCulture = CultureInfo.CurrentUICulture.Name;
        
#if DEBUG
        Console.WriteLine($"[SettingsService] System culture detected: {systemCulture}");
#endif
        
        // Check if exact match exists (e.g., "nl-NL")
        if (SupportedLanguages.Contains(systemCulture))
        {
#if DEBUG
            Console.WriteLine($"[SettingsService] Exact match found, using: {systemCulture}");
#endif
            return systemCulture;
        }
        
        // Try to match just the language part (e.g., "nl" from "nl-BE")
        var languageOnly = systemCulture.Split('-')[0];
        var match = SupportedLanguages.FirstOrDefault(lang => lang.StartsWith(languageOnly + "-"));
        
        if (match != null)
        {
#if DEBUG
            Console.WriteLine($"[SettingsService] Language match found, using: {match}");
#endif
            return match;
        }
        
#if DEBUG
        Console.WriteLine($"[SettingsService] No match found, falling back to: en-US");
#endif
        
        // Fallback to English (neutral language)
        return "en-US";
    }

    public void ApplySettings()
    {
        // Apply language
        LocalizationService.Instance.SetLanguage(_settings.Language);
        
        // Theme will be applied by App.axaml
    }
}
