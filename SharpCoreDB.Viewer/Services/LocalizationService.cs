using System.Globalization;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Reflection;

namespace SharpCoreDB.Viewer.Services;

/// <summary>
/// Localization service for the SharpCoreDB Viewer with reactive property support
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    private Dictionary<string, string> _strings = [];
    private CultureInfo _currentCulture;

    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private LocalizationService()
    {
        _currentCulture = CultureInfo.CurrentUICulture;
        Debug.WriteLine($"[Localization] Service initialized with culture: {_currentCulture.Name}");
        LoadLanguage(_currentCulture.Name);
    }

    public string this[string key] => _strings.TryGetValue(key, out var value) ? value : key;

    public void SetLanguage(string cultureName)
    {
        Debug.WriteLine($"[Localization] SetLanguage called with: {cultureName}");
        
        _currentCulture = new CultureInfo(cultureName);
        CultureInfo.CurrentUICulture = _currentCulture;
        LoadLanguage(cultureName);
        
        Debug.WriteLine($"[Localization] Language changed to: {cultureName}, firing events...");
        
        // Notify all properties have changed
        OnPropertyChanged(string.Empty);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        
        Debug.WriteLine($"[Localization] LanguageChanged event fired. Subscribers: {LanguageChanged?.GetInvocationList().Length ?? 0}");
    }

    private void LoadLanguage(string cultureName)
    {
        Debug.WriteLine($"[Localization] LoadLanguage called for: {cultureName}");
        
        // Try specific culture first (e.g., nl-NL)
        if (!TryLoadLanguageFile(cultureName))
        {
            // Fall back to language only (e.g., nl)
            var language = cultureName.Split('-')[0];
            Debug.WriteLine($"[Localization] Trying fallback language: {language}");
            if (!TryLoadLanguageFile(language))
            {
                // Fall back to en-US
                Debug.WriteLine("[Localization] Falling back to en-US");
                TryLoadLanguageFile("en-US");
            }
        }
    }

    private bool TryLoadLanguageFile(string cultureName)
    {
        // First try embedded resources
        var resourceName = $"SharpCoreDB.Viewer.Resources.Strings.{cultureName}.json";
        Debug.WriteLine($"[Localization] Attempting to load resource: {resourceName}");
        
        var assembly = typeof(LocalizationService).Assembly;
        
        // List all embedded resources for debugging
        var allResources = assembly.GetManifestResourceNames();
        Debug.WriteLine($"[Localization] Available manifest resources ({allResources.Length}): {string.Join(", ", allResources)}");
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null) 
        {
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
            Debug.WriteLine($"[Localization] ? Loaded {_strings.Count} strings from embedded resource for culture {cultureName}");
            
            var sample = _strings.Take(3).Select(kvp => $"{kvp.Key}={kvp.Value}");
            Debug.WriteLine($"[Localization] Sample strings: {string.Join(", ", sample)}");
            return true;
        }

        // Fallback: Try to load from file system (relative to executable)
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        var filePath = Path.Combine(exeDir, "Resources", $"Strings.{cultureName}.json");
        
        Debug.WriteLine($"[Localization] Trying file system: {filePath}");
        
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            Debug.WriteLine($"[Localization] ? Loaded {_strings.Count} strings from file for culture {cultureName}");
            
            var sample = _strings.Take(3).Select(kvp => $"{kvp.Key}={kvp.Value}");
            Debug.WriteLine($"[Localization] Sample strings: {string.Join(", ", sample)}");
            return true;
        }

        Debug.WriteLine($"[Localization] ? Could not find resource or file for culture: {cultureName}");
        return false;
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        return string.Format(template, args);
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
