using SharpCoreDB.Viewer.Models;
using System.Text.Json;

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
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    public void ApplySettings()
    {
        // Apply language
        LocalizationService.Instance.SetLanguage(_settings.Language);
        
        // Theme will be applied by App.axaml
    }
}
