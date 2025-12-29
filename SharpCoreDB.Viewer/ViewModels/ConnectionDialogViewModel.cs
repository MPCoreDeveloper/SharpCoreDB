using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Services;
using Avalonia.Platform.Storage;

namespace SharpCoreDB.Viewer.ViewModels;

/// <summary>
/// ViewModel for the database connection dialog
/// </summary>
public partial class ConnectionDialogViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = LocalizationService.Instance;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isPasswordVisible;

    public SharpCoreDBConnection? Connection { get; private set; }
    
    public bool IsConnected { get; private set; }
    
    public bool WasCancelled { get; private set; }
    
    public IStorageProvider? StorageProvider { get; set; }

    public ConnectionDialogViewModel()
    {
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    [RelayCommand]
    private async Task BrowseForDatabase()
    {
        if (StorageProvider == null) return;

        var options = new FolderPickerOpenOptions
        {
            Title = _localization["ConnectionDialogTitle"],
            AllowMultiple = false
        };

        var result = await StorageProvider.OpenFolderPickerAsync(options);

        if (result.Count > 0)
        {
            DatabasePath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task CreateNewDatabase()
    {
        if (StorageProvider == null) return;

        var options = new FolderPickerOpenOptions
        {
            Title = _localization["SelectNewDatabaseLocation"],
            AllowMultiple = false
        };

        var result = await StorageProvider.OpenFolderPickerAsync(options);

        if (result.Count > 0)
        {
            var basePath = result[0].Path.LocalPath;
            
            // Suggest a default name
            var suggestedName = "NewDatabase";
            var counter = 1;
            var newDbPath = Path.Combine(basePath, suggestedName);
            
            // Find unique name
            while (Directory.Exists(newDbPath))
            {
                newDbPath = Path.Combine(basePath, $"{suggestedName}{counter}");
                counter++;
            }
            
            DatabasePath = newDbPath;
        }
    }

    [RelayCommand]
    private async Task Connect()
    {
        ErrorMessage = string.Empty;
        
        // Validate inputs
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            ErrorMessage = _localization["ErrorDatabasePathRequired"];
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = _localization["ErrorPasswordRequired"];
            return;
        }

        IsConnecting = true;

        try
        {
            // Check if database exists
            bool isNewDatabase = !Directory.Exists(DatabasePath) && !File.Exists(DatabasePath);
            
            if (isNewDatabase)
            {
                // Create directory for new database
                Directory.CreateDirectory(DatabasePath);
            }

            // Create connection string
            var connectionString = $"Path={DatabasePath};Password={Password}";

            // Create and test connection
            var connection = new SharpCoreDBConnection(connectionString);
            await connection.OpenAsync();

            // Success!
            Connection = connection;
            IsConnected = true;
            OnPropertyChanged(nameof(IsConnected));
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ErrorConnectionFailed", ex.Message);
            IsConnected = false;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsConnected = false;
        WasCancelled = true;
        OnPropertyChanged(nameof(IsConnected));
    }
}
