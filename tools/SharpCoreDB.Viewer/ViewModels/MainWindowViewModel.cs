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
            
            // ✅ Split query by semicolons to support multiple statements
            var statements = QueryText
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (statements.Count == 0)
            {
                StatusMessage = _localization["ErrorQueryEmpty"];
                return;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] Executing {statements.Count} statement(s)");
#endif

            int totalAffected = 0;
            int selectResultCount = 0;
            bool hasSelectStatement = false;
            
            // Clear previous results before executing
            QueryResults.Clear();
            HasResults = false;

            // Execute each statement
            for (int i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];
                var trimmedStatement = statement.TrimStart().ToUpperInvariant();
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] Statement {i + 1}/{statements.Count}: {statement.Substring(0, Math.Min(50, statement.Length))}...");
#endif

                using var command = new SharpCoreDBCommand(statement, ActiveConnection);
                
                if (trimmedStatement.StartsWith("SELECT"))
                {
                    hasSelectStatement = true;
                    
                    // Execute SELECT and show results (only last SELECT is shown)
                    using var reader = await command.ExecuteReaderAsync();
                    
                    var results = new ObservableCollection<Dictionary<string, object>>();
                    int rowCount = 0;
                    
                    // Get column names for dynamic column generation
                    var columnNames = new List<string>();
                    for (int j = 0; j < reader.FieldCount; j++)
                    {
                        columnNames.Add(reader.GetName(j));
                    }
                    
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int j = 0; j < reader.FieldCount; j++)
                        {
                            var value = reader.GetValue(j);
                            row[reader.GetName(j)] = value ?? DBNull.Value;
                        }
                        results.Add(row);
                        rowCount++;
                    }

#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] SELECT {i + 1} returned {rowCount} rows with {columnNames.Count} columns");
#endif

                    // ✅ Store results from last SELECT statement
                    if (i == statements.Count - 1 || !statements.Skip(i + 1).Any(s => s.TrimStart().ToUpperInvariant().StartsWith("SELECT")))
                    {
                        QueryResults.Clear();
                        foreach (var dictRow in results)
                        {
                            var resultRow = Models.QueryResultRow.FromDictionary(dictRow, columnNames);
                            QueryResults.Add(resultRow);
                        }
                        
                        ResultColumns = columnNames;
                        HasResults = rowCount > 0;
                        selectResultCount = rowCount;
                        
                        if (HasResults && columnNames.Count > 0)
                        {
                            GenerateDataGridColumns(columnNames);
                        }
                    }
                }
                else
                {
                    // Execute non-query (INSERT, UPDATE, DELETE, CREATE, etc.)
                    int affected = await command.ExecuteNonQueryAsync();
                    if (affected >= 0)
                    {
                        totalAffected += affected;
                    }
                    
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ExecuteQuery] Non-query {i + 1} affected {affected} row(s)");
#endif
                }
            }

            // Build status message based on what was executed
            if (hasSelectStatement && selectResultCount > 0)
            {
                if (totalAffected > 0)
                {
                    StatusMessage = $"✅ {statements.Count} statement(s): {selectResultCount} rows returned, {totalAffected} rows affected";
                }
                else
                {
                    StatusMessage = $"✅ {statements.Count} statement(s): {selectResultCount} rows returned";
                }
            }
            else if (totalAffected > 0)
            {
                StatusMessage = $"✅ {statements.Count} statement(s): {totalAffected} rows affected";
            }
            else
            {
                StatusMessage = $"✅ {statements.Count} statement(s) executed successfully";
            }
            
            // Reload table list in case schema changed
            if (statements.Any(s => s.TrimStart().ToUpperInvariant().StartsWith("CREATE") || 
                                    s.TrimStart().ToUpperInvariant().StartsWith("DROP") ||
                                    s.TrimStart().ToUpperInvariant().StartsWith("ALTER")))
            {
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
