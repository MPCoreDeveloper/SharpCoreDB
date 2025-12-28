using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using System.Collections.ObjectModel;

namespace SharpCoreDB.Viewer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "SharpCoreDB Viewer";

    [ObservableProperty]
    private SharpCoreDBConnection? _activeConnection;

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _tables = [];

    [RelayCommand]
    private void Disconnect()
    {
        ActiveConnection?.Close();
        ActiveConnection?.Dispose();
        ActiveConnection = null;
        IsConnected = false;
        ConnectionStatus = "Not connected";
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
        ConnectionStatus = $"Connected to: {connection.DataSource}";
        
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
            // TODO: Show error message
            Console.WriteLine($"Failed to load tables: {ex.Message}");
        }
    }
}
