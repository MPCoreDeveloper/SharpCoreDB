using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Viewer.Models;
using SharpCoreDB.Viewer.Services;
using System.Collections.ObjectModel;

namespace SharpCoreDB.Viewer.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService = SettingsService.Instance;
    private readonly LocalizationService _localization = LocalizationService.Instance;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = 
    [
        new LanguageOption { Code = "en-US", DisplayName = "English" },
        new LanguageOption { Code = "nl-NL", DisplayName = "Nederlands" }
    ];

    public ObservableCollection<ThemeOption> AvailableThemes { get; } =
    [
        new ThemeOption { Name = "Light", DisplayName = "Light" },
        new ThemeOption { Name = "Dark", DisplayName = "Dark" }
    ];

    public bool IsSaved { get; private set; }

    public SettingsViewModel()
    {
        var settings = _settingsService.Settings;
        
        // Set selected language
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == settings.Language) 
                           ?? AvailableLanguages[0];
        
        // Set selected theme
        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.Name == settings.Theme) 
                        ?? AvailableThemes[0];
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedLanguage == null || SelectedTheme == null) return;

        var settings = new AppSettings
        {
            Language = SelectedLanguage.Code,
            Theme = SelectedTheme.Name
        };

        _settingsService.SaveSettings(settings);
        _settingsService.ApplySettings();
        
        IsSaved = true;
        OnPropertyChanged(nameof(IsSaved));
    }

    [RelayCommand]
    private void Cancel()
    {
        IsSaved = false;
        OnPropertyChanged(nameof(IsSaved));
    }
}

public class LanguageOption
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class ThemeOption
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
