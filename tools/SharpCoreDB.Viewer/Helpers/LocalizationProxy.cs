using System.ComponentModel;
using System.Runtime.CompilerServices;
using SharpCoreDB.Viewer.Services;

namespace SharpCoreDB.Viewer.Helpers;

/// <summary>
/// Proxy class that exposes localized strings as bindable properties
/// </summary>
public class LocalizationProxy : INotifyPropertyChanged
{
    private readonly string _key;
    private readonly LocalizationService _localization = LocalizationService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationProxy(string key)
    {
        _key = key;
        
        // Subscribe to language changes
        _localization.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Value));
        };
    }

    public string Value => _localization[_key];

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
