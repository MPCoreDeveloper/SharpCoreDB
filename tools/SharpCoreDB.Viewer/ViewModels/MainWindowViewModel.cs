using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Services;
using System.Collections.ObjectModel;

namespace SharpCoreDB.Viewer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly List<string> _allTables = [];

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
    [NotifyCanExecuteChangedFor(nameof(PreviewSelectedTableCommand))]
    private string? _selectedTable;

    [ObservableProperty]
    private string _tableFilterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _tableColumnsMetadata = [];

    [ObservableProperty]
    private ObservableCollection<string> _tableIndexesMetadata = [];

    [ObservableProperty]
    private ObservableCollection<string> _tableTriggersMetadata = [];

    [ObservableProperty]
    private string _tableMetadataSummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Models.QueryResultRow> _queryResults = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    /// <summary>
    /// List with column names for the current query result.
    /// Used by the view for dynamic DataGrid column generation.
    /// </summary>
    [ObservableProperty]
    private List<string> _resultColumns = [];

    public MainWindowViewModel()
    {
        _connectionStatus = _localization["NotConnected"];
        _statusMessage = _localization["Ready"];
        _tableMetadataSummary = _localization["NoTableSelected"];

        // Subscribe to language changes
        _localization.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(ConnectionStatus));
            if (!IsConnected)
            {
                ConnectionStatus = _localization["NotConnected"];
                StatusMessage = _localization["Ready"];
                TableMetadataSummary = _localization["NoTableSelected"];
            }
        };
    }

    partial void OnTableFilterTextChanged(string value)
    {
        ApplyTableFilter(value);
    }

    partial void OnSelectedTableChanged(string? value)
    {
        _ = LoadSelectedTableMetadataAsync(value);
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
        _allTables.Clear();
        SelectedTable = null;
        TableColumnsMetadata.Clear();
        TableIndexesMetadata.Clear();
        TableTriggersMetadata.Clear();
        TableMetadataSummary = _localization["NoTableSelected"];
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task PreviewSelectedTable()
    {
        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        QueryText = $"SELECT * FROM \"{SelectedTable.Replace("\"", "\"\"")}\" LIMIT 200;";
        await ExecuteQuery().ConfigureAwait(true);
    }

    private bool CanPreviewSelectedTable() => !string.IsNullOrWhiteSpace(SelectedTable);

    [RelayCommand]
    private async Task RefreshTables()
    {
        await LoadTablesAsync().ConfigureAwait(true);
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

            var statements = QueryText
                .Split([';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static s => s.Trim())
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (statements.Count == 0)
            {
                StatusMessage = _localization["ErrorQueryEmpty"];
                return;
            }

            int totalAffected = 0;
            int selectResultCount = 0;
            bool hasSelectStatement = false;

            QueryResults.Clear();
            HasResults = false;

            for (int i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];
                var trimmedStatement = statement.TrimStart().ToUpperInvariant();

                using var command = new SharpCoreDBCommand(statement, ActiveConnection);

                if (trimmedStatement.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    hasSelectStatement = true;

                    using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);

                    ObservableCollection<Dictionary<string, object>> results = [];
                    int rowCount = 0;

                    List<string> columnNames = [];
                    for (int j = 0; j < reader.FieldCount; j++)
                    {
                        columnNames.Add(reader.GetName(j));
                    }

                    while (await reader.ReadAsync().ConfigureAwait(true))
                    {
                        Dictionary<string, object> row = [];
                        for (int j = 0; j < reader.FieldCount; j++)
                        {
                            var value = reader.GetValue(j);
                            row[reader.GetName(j)] = value ?? DBNull.Value;
                        }
                        results.Add(row);
                        rowCount++;
                    }

                    if (i == statements.Count - 1 || !statements.Skip(i + 1).Any(static s => s.TrimStart().ToUpperInvariant().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)))
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
                    int affected = await command.ExecuteNonQueryAsync().ConfigureAwait(true);
                    if (affected >= 0)
                    {
                        totalAffected += affected;
                    }
                }
            }

            if (hasSelectStatement && selectResultCount > 0)
            {
                StatusMessage = totalAffected > 0
                    ? _localization.Format("StatusStatementsRowsAndAffected", statements.Count, selectResultCount, totalAffected)
                    : _localization.Format("StatusStatementsRows", statements.Count, selectResultCount);
            }
            else if (totalAffected > 0)
            {
                StatusMessage = _localization.Format("StatusStatementsAffected", statements.Count, totalAffected);
            }
            else
            {
                StatusMessage = _localization.Format("StatusStatementsExecuted", statements.Count);
            }

            // Reload table list in case schema changed
            if (statements.Any(static s => s.TrimStart().ToUpperInvariant().StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                                     || s.TrimStart().ToUpperInvariant().StartsWith("DROP", StringComparison.OrdinalIgnoreCase)
                                     || s.TrimStart().ToUpperInvariant().StartsWith("ALTER", StringComparison.OrdinalIgnoreCase)))
            {
                await LoadTablesAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            // ✅ IMPROVED: Show full error with inner exception
            var errorMsg = ex.InnerException != null
                ? $"{ex.Message} → {ex.InnerException.Message}"
                : ex.Message;

            StatusMessage = _localization.Format("ErrorQueryFailed", errorMsg);
            QueryResults.Clear();
            HasResults = false;
        }
    }

    [RelayCommand]
    private void OpenTools()
    {
        // This will be called from MainWindow code-behind since we need the Window reference
    }

    public void SetConnection(SharpCoreDBConnection connection)
    {
        ActiveConnection = connection;
        IsConnected = true;
        ConnectionStatus = _localization.Format("ConnectedTo", connection.DataSource);

        _ = LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        if (ActiveConnection == null)
        {
            return;
        }

        try
        {
            using var command = new SharpCoreDBCommand(
                "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
                ActiveConnection);

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);

            _allTables.Clear();
            while (await reader.ReadAsync().ConfigureAwait(true))
            {
                _allTables.Add(reader.GetString(0));
            }

            ApplyTableFilter(TableFilterText);

            if (SelectedTable is null || !_allTables.Contains(SelectedTable, StringComparer.OrdinalIgnoreCase))
            {
                SelectedTable = Tables.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorTableLoadFailed", ex.Message);
        }
    }

    private void ApplyTableFilter(string filter)
    {
        var normalizedFilter = filter?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(normalizedFilter)
            ? _allTables
            : _allTables.Where(table => table.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        Tables.Clear();
        foreach (var table in filtered)
        {
            Tables.Add(table);
        }
    }

    private async Task LoadSelectedTableMetadataAsync(string? tableName)
    {
        TableColumnsMetadata.Clear();
        TableIndexesMetadata.Clear();
        TableTriggersMetadata.Clear();

        if (ActiveConnection == null || string.IsNullOrWhiteSpace(tableName))
        {
            TableMetadataSummary = _localization["NoTableSelected"];
            return;
        }

        var escapedTable = tableName.Replace("'", "''", StringComparison.Ordinal);

        try
        {
            using (var command = new SharpCoreDBCommand($"PRAGMA table_info('{escapedTable}')", ActiveConnection))
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(true))
            {
                while (await reader.ReadAsync().ConfigureAwait(true))
                {
                    var name = reader.GetValue(1)?.ToString() ?? string.Empty;
                    var type = reader.GetValue(2)?.ToString() ?? "TEXT";
                    var notNull = reader.GetValue(3)?.ToString() == "1" ? "NOT NULL" : "NULL";
                    TableColumnsMetadata.Add($"{name} ({type}, {notNull})");
                }
            }

            using (var command = new SharpCoreDBCommand($"SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='{escapedTable}' ORDER BY name", ActiveConnection))
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(true))
            {
                while (await reader.ReadAsync().ConfigureAwait(true))
                {
                    TableIndexesMetadata.Add(reader.GetValue(0)?.ToString() ?? string.Empty);
                }
            }

            using (var command = new SharpCoreDBCommand($"SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name='{escapedTable}' ORDER BY name", ActiveConnection))
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(true))
            {
                while (await reader.ReadAsync().ConfigureAwait(true))
                {
                    TableTriggersMetadata.Add(reader.GetValue(0)?.ToString() ?? string.Empty);
                }
            }

            if (TableColumnsMetadata.Count == 0)
            {
                TableColumnsMetadata.Add(_localization["MetadataNoneAvailable"]);
            }

            if (TableIndexesMetadata.Count == 0)
            {
                TableIndexesMetadata.Add(_localization["MetadataNoneAvailable"]);
            }

            if (TableTriggersMetadata.Count == 0)
            {
                TableTriggersMetadata.Add(_localization["MetadataNoneAvailable"]);
            }

            var columnCount = TableColumnsMetadata.Count == 1 && TableColumnsMetadata[0] == _localization["MetadataNoneAvailable"] ? 0 : TableColumnsMetadata.Count;
            var indexCount = TableIndexesMetadata.Count == 1 && TableIndexesMetadata[0] == _localization["MetadataNoneAvailable"] ? 0 : TableIndexesMetadata.Count;
            var triggerCount = TableTriggersMetadata.Count == 1 && TableTriggersMetadata[0] == _localization["MetadataNoneAvailable"] ? 0 : TableTriggersMetadata.Count;

            TableMetadataSummary = _localization.Format("TableMetadataSummary", tableName, columnCount, indexCount, triggerCount);
        }
        catch (Exception ex)
        {
            TableMetadataSummary = _localization.Format("TableMetadataLoadFailed", ex.Message);
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
        // ✅ FIX: Prevent duplicate column generation
        // Only regenerate if columns actually changed
        if (_lastGeneratedColumns != null
            && _lastGeneratedColumns.Count == columnNames.Count
            && _lastGeneratedColumns.SequenceEqual(columnNames))
        {
            return;
        }

        _lastGeneratedColumns = [.. columnNames];
        ColumnsChanged?.Invoke(this, columnNames);
    }
}
