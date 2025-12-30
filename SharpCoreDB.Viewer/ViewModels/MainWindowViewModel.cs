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

    [ObservableProperty]
    private ObservableCollection<Models.QueryResultRow> _queryResults = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasResults;
    
    /// <summary>
    /// Lijst met kolom namen voor de huidige query result.
    /// Gebruikt door de View om dynamisch DataGrid columns te genereren.
    /// </summary>
    [ObservableProperty]
    private List<string> _resultColumns = [];

    public MainWindowViewModel()
    {
        _connectionStatus = _localization["NotConnected"];
        _statusMessage = _localization["Ready"];
        
        // Subscribe to language changes
        _localization.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(ConnectionStatus));
            if (!IsConnected)
            {
                ConnectionStatus = _localization["NotConnected"];
                StatusMessage = _localization["Ready"];
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
            StatusMessage = _localization["NotConnected"];
            return;
        }

        if (string.IsNullOrWhiteSpace(QueryText))
        {
            StatusMessage = _localization["ErrorQueryEmpty"];
            return;
        }

        try
        {
            StatusMessage = _localization["ExecutingQuery"];
            QueryResults.Clear();
            HasResults = false;

            using var command = new SharpCoreDBCommand(QueryText, ActiveConnection);
            
            // Check if this is a SELECT query
            var trimmedQuery = QueryText.Trim().ToUpperInvariant();
            if (trimmedQuery.StartsWith("SELECT"))
            {
                // Execute SELECT and show results
                using var reader = await command.ExecuteReaderAsync();
                
                var results = new ObservableCollection<Dictionary<string, object>>();
                int rowCount = 0;
                
                // Get column names for dynamic column generation
                var columnNames = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columnNames.Add(reader.GetName(i));
                }
                
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.GetValue(i);
                        row[reader.GetName(i)] = value ?? DBNull.Value;
                    }
                    results.Add(row);
                    rowCount++;
                }

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] SELECT returned {rowCount} rows with {columnNames.Count} columns");
                if (rowCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] First row keys: {string.Join(", ", results[0].Keys)}");
                    System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] First row values: {string.Join(", ", results[0].Values.Select(v => v?.ToString() ?? "NULL"))}");
                }
#endif

                // ✅ Convert Dictionary rows to array-based QueryResultRow objects  
                QueryResults.Clear();
                foreach (var dictRow in results)
                {
                    // Convert Dictionary to array in correct column order
                    var resultRow = Models.QueryResultRow.FromDictionary(dictRow, columnNames);
                    QueryResults.Add(resultRow);
                }
                
                // ✅ CRITICAL: Store column names for DataGrid column generation
                ResultColumns = columnNames;
                
                HasResults = rowCount > 0;
                
                // Generate columns AFTER setting HasResults so DataGrid is visible
                if (HasResults && columnNames.Count > 0)
                {
                    GenerateDataGridColumns(columnNames);
                }
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] QueryResults now contains {QueryResults.Count} items");
                System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] ResultColumns: {string.Join(", ", ResultColumns)}");
                System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] HasResults set to: {HasResults}");
                System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] Row count: {rowCount}");
                
                // Verify first row array structure
                if (QueryResults.Count > 0)
                {
                    var firstRow = QueryResults[0];
                    System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] First row Values.Length: {firstRow.Values.Length}");
                    System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] First row values: {string.Join(", ", firstRow.Values.Select((v, i) => $"[{i}]={v}"))}");
                }
#endif
                
                StatusMessage = $"✅ {_localization.Format("QueryExecutedSuccess", rowCount)}";
            }
            else
            {
                // Execute non-query (INSERT, UPDATE, DELETE, CREATE, etc.)
                int affected = await command.ExecuteNonQueryAsync();
                
                // ✅ IMPROVED: Better status message for non-query commands
                StatusMessage = affected >= 0 
                    ? $"✅ {_localization.Format("QueryExecutedAffected", affected)}"
                    : $"✅ {_localization["QueryExecutedSuccess"]}";
                
                // Reload table list in case schema changed
                await LoadTablesAsync();
            }
        }
        catch (Exception ex)
        {
            // ✅ IMPROVED: Show full error with inner exception
            var errorMsg = ex.InnerException != null 
                ? $"{ex.Message} → {ex.InnerException.Message}"
                : ex.Message;
            
            StatusMessage = $"❌ {_localization.Format("ErrorQueryFailed", errorMsg)}";
            QueryResults.Clear();
            HasResults = false;
            
#if DEBUG
            // Also log to debug output
            System.Diagnostics.Debug.WriteLine($"[Query Error] {errorMsg}");
            System.Diagnostics.Debug.WriteLine($"[Query Error] Stack: {ex.StackTrace}");
#endif
        }
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
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[LoadTablesAsync] Loading table list...");
#endif
            
            // Query for tables
            using var command = new SharpCoreDBCommand(
                "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name", 
                ActiveConnection);
            
            using var reader = await command.ExecuteReaderAsync();
            
            Tables.Clear();
            while (await reader.ReadAsync())
            {
                var tableName = reader.GetString(0);
                Tables.Add(tableName);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[LoadTablesAsync] Found table: {tableName}");
#endif
            }
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[LoadTablesAsync] Loaded {Tables.Count} tables");
#endif
        }
        catch (Exception ex)
        {
            // Show localized error message
            Console.WriteLine(_localization.Format("ErrorTableLoadFailed", ex.Message));
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[LoadTablesAsync] ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[LoadTablesAsync] Stack: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadTablesAsync] Inner: {ex.InnerException.Message}");
            }
#endif
        }
    }

    /// <summary>
    /// Event to notify UI that columns need to be regenerated.
    /// The MainWindow will handle this by regenerating DataGrid columns.
    /// </summary>
    public event EventHandler<List<string>>? ColumnsChanged;

    private List<string>? _lastGeneratedColumns;

    private void GenerateDataGridColumns(List<string> columnNames)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[ViewModel] GenerateDataGridColumns called with {columnNames.Count} columns");
        foreach (var col in columnNames)
        {
            System.Diagnostics.Debug.WriteLine($"[ViewModel]   - {col}");
        }
#endif
        
        // ✅ FIX: Prevent duplicate column generation
        // Only regenerate if columns actually changed
        if (_lastGeneratedColumns != null && 
            _lastGeneratedColumns.Count == columnNames.Count &&
            _lastGeneratedColumns.SequenceEqual(columnNames))
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[ViewModel] Skipping duplicate column generation - columns unchanged");
#endif
            return;
        }
        
        _lastGeneratedColumns = new List<string>(columnNames);
        
        // Notify the View to regenerate DataGrid columns
        ColumnsChanged?.Invoke(this, columnNames);
        
#if DEBUG
        if (ColumnsChanged == null)
        {
            System.Diagnostics.Debug.WriteLine("[ViewModel] WARNING: ColumnsChanged event has no subscribers!");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ViewModel] ColumnsChanged event fired to {ColumnsChanged.GetInvocationList().Length} subscriber(s)");
        }
#endif
    }
}
