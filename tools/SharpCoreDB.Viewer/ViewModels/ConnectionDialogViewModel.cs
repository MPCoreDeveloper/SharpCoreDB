using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Models;
using SharpCoreDB.Viewer.Services;
using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;

namespace SharpCoreDB.Viewer.ViewModels;

/// <summary>
/// Database format type enumeration
/// </summary>
public enum DatabaseFormatType
{
    /// <summary>Directory-based format (multiple files)</summary>
    Directory = 0,
    
    /// <summary>Single-file format (.scdb)</summary>
    SingleFile = 1
}

/// <summary>
/// ViewModel for the database connection dialog
/// </summary>
public partial class ConnectionDialogViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly SettingsService _settingsService = SettingsService.Instance;

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
    
    [ObservableProperty]
    private DatabaseFormatType _selectedFormat = DatabaseFormatType.Directory;
    
    [ObservableProperty]
    private bool _showFormatSelector = true;

    [ObservableProperty]
    private ObservableCollection<ConnectionProfile> _recentConnections = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveProfileCommand))]
    private ConnectionProfile? _selectedRecentConnection;

    public SharpCoreDBConnection? Connection { get; private set; }
    
    public bool IsConnected { get; private set; }
    
    public bool WasCancelled { get; private set; }
    
    public IStorageProvider? StorageProvider { get; set; }

    public ConnectionDialogViewModel()
    {
        LoadRecentConnections();
    }
    
    /// <summary>
    /// Detects format type from file path
    /// </summary>
    partial void OnDatabasePathChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ShowFormatSelector = true;
            return;
        }
        
        // Auto-detect format from extension
        if (value.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
        {
            SelectedFormat = DatabaseFormatType.SingleFile;
            ShowFormatSelector = false;
        }
        else if (File.Exists(value))
        {
            // File without .scdb extension = invalid
            ShowFormatSelector = true;
        }
        else if (Directory.Exists(value))
        {
            // Existing directory = directory format
            SelectedFormat = DatabaseFormatType.Directory;
            ShowFormatSelector = false;
        }
        else
        {
            // New path - allow format selection
            ShowFormatSelector = true;
        }
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

        // Allow user to select either folder or .scdb file
        var options = new FilePickerOpenOptions
        {
            Title = _localization["ConnectionDialogTitle"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SharpCoreDB Database")
                {
                    Patterns = ["*.scdb"]
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = ["*"]
                }
            }
        };

        var result = await StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);

        if (result.Count > 0)
        {
            DatabasePath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task CreateNewDatabase()
    {
        if (StorageProvider == null) return;

        // When creating new, use format selector to determine save type
        if (SelectedFormat == DatabaseFormatType.SingleFile)
        {
            // Create .scdb file
            var options = new FilePickerSaveOptions
            {
                Title = _localization["SelectNewDatabaseLocation"],
                DefaultExtension = ".scdb",
                SuggestedFileName = "NewDatabase.scdb",
                FileTypeChoices =
                [
                    new FilePickerFileType("SharpCoreDB Single-File Database")
                    {
                        Patterns = ["*.scdb"]
                    }
                ]
            };

            var result = await StorageProvider.SaveFilePickerAsync(options).ConfigureAwait(true);

            if (result != null)
            {
                DatabasePath = result.Path.LocalPath;
            }
        }
        else
        {
            // Create directory
            var options = new FolderPickerOpenOptions
            {
                Title = _localization["SelectNewDatabaseLocation"],
                AllowMultiple = false
            };

            var result = await StorageProvider.OpenFolderPickerAsync(options).ConfigureAwait(true);

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
    }

    [RelayCommand(CanExecute = nameof(CanApplySelectedProfile))]
    private void ApplyProfile()
    {
        if (SelectedRecentConnection is null)
        {
            return;
        }

        DatabasePath = SelectedRecentConnection.DatabasePath;
        SelectedFormat = string.Equals(SelectedRecentConnection.StorageMode, "SingleFile", StringComparison.OrdinalIgnoreCase)
            ? DatabaseFormatType.SingleFile
            : DatabaseFormatType.Directory;
    }

    [RelayCommand(CanExecute = nameof(CanApplySelectedProfile))]
    private void RemoveProfile()
    {
        if (SelectedRecentConnection is null)
        {
            return;
        }

        _settingsService.RemoveRecentConnection(SelectedRecentConnection.DatabasePath);
        LoadRecentConnections();
    }

    private bool CanApplySelectedProfile() => SelectedRecentConnection is not null;

    private void LoadRecentConnections()
    {
        RecentConnections = new ObservableCollection<ConnectionProfile>(_settingsService.GetRecentConnections());
        SelectedRecentConnection = RecentConnections.FirstOrDefault();
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
            // Detect format and create connection string
            var isSingleFile = DatabasePath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase) ||
                               SelectedFormat == DatabaseFormatType.SingleFile;
            
            var isNewDatabase = !Directory.Exists(DatabasePath) && !File.Exists(DatabasePath);
            
            // Ensure correct extension for new single-file databases
            if (isNewDatabase && isSingleFile && !DatabasePath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
            {
                DatabasePath += ".scdb";
            }
            
            if (isNewDatabase)
            {
                if (isSingleFile)
                {
                    // Create parent directory if needed
                    var directory = Path.GetDirectoryName(DatabasePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                else
                {
                    // Create directory for new database
                    Directory.CreateDirectory(DatabasePath);
                }
            }

            // Create connection string with storage mode
            var storageMode = isSingleFile ? "SingleFile" : "Directory";
            var connectionString = $"Path={DatabasePath};Password={Password};StorageMode={storageMode}";

            // Create and test connection
            var connection = new SharpCoreDBConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(true);

            // Success!
            Connection = connection;
            IsConnected = true;
            _settingsService.SaveRecentConnection(DatabasePath, storageMode);
            OnPropertyChanged(nameof(IsConnected));
        }
        catch (SharpCoreDBException ex)
        {
            // Check if error is related to password/decryption
            if (ex.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                (ex.InnerException?.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (ex.InnerException?.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                ErrorMessage = _localization["ErrorIncorrectPassword"];
            }
            else
            {
                ErrorMessage = _localization.Format("ErrorConnectionFailed", ex.Message);
            }
            IsConnected = false;
        }
        catch (Exception ex)
        {
            // Check inner exceptions for password-related errors
            var errorMessage = ex.InnerException?.Message ?? ex.Message;
            
            if (errorMessage.Contains("decrypt", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = _localization["ErrorIncorrectPassword"];
            }
            else
            {
                ErrorMessage = _localization.Format("ErrorConnectionFailed", errorMessage);
            }
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
