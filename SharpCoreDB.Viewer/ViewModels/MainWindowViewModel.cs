using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Services;
using System.Collections.ObjectModel;

namespace SharpCoreDB.Viewer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = LocalizationService.Instance;

    [ObservableProperty]
    private string _title = "SharpCoreDB Viewer";

    [ObservableProperty]
    private SharpCoreDBConnection? _activeConnection;

    [ObservableProperty]
    private string _connectionStatus;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _tables = [];

    public MainWindowViewModel()
    {
        _connectionStatus = _localization["NotConnected"];
        
        // Subscribe to language changes
        _localization.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(ConnectionStatus));
            if (!IsConnected)
            {
                ConnectionStatus = _localization["NotConnected"];
            }
        };
    }

    [RelayCommand]
    private void Disconnect()
    {
        ActiveConnection?.Close();
        ActiveConnection?.Dispose();
        ActiveConnection = null;
        IsConnected = false;
        ConnectionStatus = _localization["NotConnected"];
        Tables.Clear();
    }

    [RelayCommand]
    private async Task ExecuteQuery()
    {
        if (!IsConnected || ActiveConnection == null)
        {
            return;
        }

        // TODO: Implement query execution
        await Task.CompletedTask;
    }

    public void SetConnection(SharpCoreDBConnection connection)
    {
        ActiveConnection = connection;
        IsConnected = true;
        ConnectionStatus = _localization.Format("ConnectedTo", connection.DataSource);
        
        // Load database schema
        _ = LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        if (ActiveConnection == null) return;

        try
        {
            // Query for tables
            using var command = new SharpCoreDBCommand(
                "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name", 
                ActiveConnection);
            
            using var reader = await command.ExecuteReaderAsync();
            
            Tables.Clear();
            while (await reader.ReadAsync())
            {
                Tables.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            // Show localized error message
            Console.WriteLine(_localization.Format("ErrorTableLoadFailed", ex.Message));
        }
    }
}
