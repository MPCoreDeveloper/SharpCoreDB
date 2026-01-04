using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Viewer.Models;
using SharpCoreDB.Viewer.Services;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;

namespace SharpCoreDB.Viewer.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog with immediate preview
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService = SettingsService.Instance;
    private readonly LocalizationService _localization = LocalizationService.Instance;
    
    // Store original settings for cancel functionality
    private readonly string _originalLanguage;
    private readonly string _originalTheme;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _isDarkTheme;

    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = 
    [
        new LanguageOption { Code = "en-US", DisplayName = "English" },
        new LanguageOption { Code = "nl-NL", DisplayName = "Nederlands" },
        new LanguageOption { Code = "de-DE", DisplayName = "Deutsch" },
        new LanguageOption { Code = "fr-FR", DisplayName = "Français" },
        new LanguageOption { Code = "es-ES", DisplayName = "Español" },
        new LanguageOption { Code = "it-IT", DisplayName = "Italiano" }
    ];

    public bool IsSaved { get; private set; }

    public SettingsViewModel()
    {
        var settings = _settingsService.Settings;
        
        // Store original settings
        _originalLanguage = settings.Language;
        _originalTheme = settings.Theme;
        
        // Set selected language (will auto-select based on saved/system language)
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == settings.Language) 
                           ?? AvailableLanguages.FirstOrDefault(l => l.Code == "en-US")
                           ?? AvailableLanguages[0];
        
        // Set theme toggle (true = dark, false = light)
        IsDarkTheme = settings.Theme == "Dark";
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value != null)
        {
            // Apply language change immediately for preview
            _localization.SetLanguage(value.Code);
        }
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        // Apply theme change immediately for preview
        if (Application.Current is App app)
        {
            app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedLanguage == null) return;

        // Save settings to persist for next startup
        var settings = new AppSettings
        {
            Language = SelectedLanguage.Code,
            Theme = IsDarkTheme ? "Dark" : "Light"
        };

        _settingsService.SaveSettings(settings);
        
        IsSaved = true;
        OnPropertyChanged(nameof(IsSaved));
    }

    [RelayCommand]
    private void Cancel()
    {
        // Revert to original settings
        var originalLangOption = AvailableLanguages.FirstOrDefault(l => l.Code == _originalLanguage);
        if (originalLangOption != null)
        {
            SelectedLanguage = originalLangOption;
            _localization.SetLanguage(_originalLanguage);
        }
        
        IsDarkTheme = _originalTheme == "Dark";
        if (Application.Current is App app)
        {
            app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }
        
        IsSaved = false;
        OnPropertyChanged(nameof(IsSaved));
    }
}

public class LanguageOption
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
