using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using Avalonia.Platform.Storage;

namespace SharpCoreDB.Viewer.ViewModels;

/// <summary>
/// ViewModel for the database connection dialog
/// </summary>
public partial class ConnectionDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isConnecting;

    public SharpCoreDBConnection? Connection { get; private set; }
    
    public bool IsConnected { get; private set; }
    
    public bool WasCancelled { get; private set; }
    
    public IStorageProvider? StorageProvider { get; set; }

    [RelayCommand]
    private async Task BrowseForDatabase()
    {
        if (StorageProvider == null) return;

        var options = new FolderPickerOpenOptions
        {
            Title = "Select SharpCoreDB Database Directory",
            AllowMultiple = false
        };

        var result = await StorageProvider.OpenFolderPickerAsync(options);

        if (result.Count > 0)
        {
            DatabasePath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task Connect()
    {
        ErrorMessage = string.Empty;
        
        // Validate inputs
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            ErrorMessage = "Please enter a database path";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter a password";
            return;
        }

        // Check if path exists
        if (!Directory.Exists(DatabasePath) && !File.Exists(DatabasePath))
        {
            ErrorMessage = "Database path does not exist";
            return;
        }

        IsConnecting = true;

        try
        {
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
            ErrorMessage = $"Connection failed: {ex.Message}";
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
