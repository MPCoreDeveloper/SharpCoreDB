using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Models;
using SharpCoreDB.Viewer.Services;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpCoreDB.Viewer.ViewModels;

public enum ResultMode
{
    View = 0,
    Edit = 1
}

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
    private ObservableCollection<ExplorerNode> _explorerNodes = [];

    [ObservableProperty]
    private ExplorerNode? _selectedExplorerNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewSelectedTableCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectTop100Command))]
    [NotifyCanExecuteChangedFor(nameof(ScriptSelectedTableCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropSelectedTableCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateInsertTemplateCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateUpdateTemplateCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateDeleteTemplateCommand))]
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
    private ObservableCollection<QueryResultRow> _queryResults = [];

    [ObservableProperty]
    private QueryResultRow? _selectedResultRow;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _isResultEditable;

    [ObservableProperty]
    private ResultMode _resultMode = ResultMode.View;

    [ObservableProperty]
    private bool _canGoPreviousPage;

    [ObservableProperty]
    private bool _canGoNextPage;

    private IAsyncRelayCommand? _previousResultPageCommand;
    private IAsyncRelayCommand? _nextResultPageCommand;
    private IAsyncRelayCommand? _refreshCurrentPreviewPageCommand;

    public IAsyncRelayCommand PreviousResultPageCommand
        => _previousResultPageCommand ??= new AsyncRelayCommand(ExecutePreviousResultPageAsync, CanGoPreviousPageInternal);

    public IAsyncRelayCommand NextResultPageCommand
        => _nextResultPageCommand ??= new AsyncRelayCommand(ExecuteNextResultPageAsync, CanGoNextPageInternal);

    public IAsyncRelayCommand RefreshCurrentPreviewPageCommand
        => _refreshCurrentPreviewPageCommand ??= new AsyncRelayCommand(ExecuteRefreshCurrentPreviewPageAsync);

    [ObservableProperty]
    private int _pageSize = 200;

    [ObservableProperty]
    private string _selectTopNText = "100";

    [ObservableProperty]
    private int _currentOffset;

    [ObservableProperty]
    private long _currentPreviewTotalRows;

    [ObservableProperty]
    private string _newTableName = string.Empty;

    [ObservableProperty]
    private string _newTableColumnsDefinition = "Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NULL";

    [ObservableProperty]
    private string _renameTableName = string.Empty;

    private int ResolveSelectTopNOrDefault()
    {
        if (!int.TryParse(SelectTopNText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return 100;
        }

        return Math.Clamp(value, 1, 10_000);
    }

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
        CurrentOffset = 0;
        CanGoPreviousPage = false;
        CanGoNextPage = false;
        CurrentPreviewTotalRows = 0;
        RenameTableName = value ?? string.Empty;
    }

    partial void OnSelectedExplorerNodeChanged(ExplorerNode? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.NodeType == ExplorerNodeType.Table)
        {
            SelectedTable = value.Name;
            return;
        }

        if (!string.IsNullOrWhiteSpace(value.TableName))
        {
            SelectedTable = value.TableName;
        }
    }

    partial void OnCanGoPreviousPageChanged(bool value)
    {
        _previousResultPageCommand?.NotifyCanExecuteChanged();
    }

    partial void OnCanGoNextPageChanged(bool value)
    {
        _nextResultPageCommand?.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Disconnect()
    {
        CloseActiveConnection();
        IsConnected = false;
        ConnectionStatus = _localization["NotConnected"];
        Tables.Clear();
        ExplorerNodes.Clear();
        _allTables.Clear();
        SelectedExplorerNode = null;
        SelectedTable = null;
        TableColumnsMetadata.Clear();
        TableIndexesMetadata.Clear();
        TableTriggersMetadata.Clear();
        TableMetadataSummary = _localization["NoTableSelected"];
        QueryResults.Clear();
        HasResults = false;
        IsResultEditable = false;
        ResultMode = ResultMode.View;
        CurrentOffset = 0;
        CurrentPreviewTotalRows = 0;
        CanGoPreviousPage = false;
        CanGoNextPage = false;
    }

    [RelayCommand]
    private void EnableResultEditing()
    {
        if (!HasResults)
        {
            return;
        }

        ResultMode = ResultMode.Edit;
        IsResultEditable = true;
    }

    [RelayCommand]
    private void AddResultRow()
    {
        if (!IsResultEditable)
        {
            return;
        }

        var row = QueryResultRow.CreateEmpty(ResultColumns);
        QueryResults.Add(row);
        SelectedResultRow = row;
    }

    [RelayCommand]
    private async Task SaveSelectedResultRow()
    {
        if (!IsResultEditable || ActiveConnection is null || string.IsNullOrWhiteSpace(SelectedTable) || SelectedResultRow is null)
        {
            return;
        }

        try
        {
            var escapedTable = SelectedTable.Replace("\"", "\"\"", StringComparison.Ordinal);
            var nonRowIdColumns = ResultColumns.Where(static c => !c.Equals("__rowid__", StringComparison.OrdinalIgnoreCase)).ToList();

            if (SelectedResultRow.IsNew)
            {
                var insertColumns = new List<string>();
                var insertValues = new List<string>();

                foreach (var column in nonRowIdColumns)
                {
                    var value = SelectedResultRow.GetValue(column, ResultColumns);
                    if (value is null || value == DBNull.Value)
                    {
                        continue;
                    }

                    insertColumns.Add($"\"{column.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
                    insertValues.Add(ToSqlLiteral(value));
                }

                var sql = insertColumns.Count == 0
                    ? $"INSERT INTO \"{escapedTable}\" DEFAULT VALUES;"
                    : $"INSERT INTO \"{escapedTable}\" ({string.Join(", ", insertColumns)}) VALUES ({string.Join(", ", insertValues)});";

                using var insertCommand = new SharpCoreDBCommand(sql, ActiveConnection);
                await insertCommand.ExecuteNonQueryAsync().ConfigureAwait(true);
            }
            else
            {
                var rowId = SelectedResultRow.GetValue("__rowid__", ResultColumns);
                if (rowId is null || rowId == DBNull.Value)
                {
                    return;
                }

                var setters = nonRowIdColumns.Select(column =>
                {
                    var value = SelectedResultRow.GetValue(column, ResultColumns);
                    return $"\"{column.Replace("\"", "\"\"", StringComparison.Ordinal)}\" = {ToSqlLiteral(value)}";
                }).ToList();

                if (setters.Count == 0)
                {
                    return;
                }

                var updateSql = $"UPDATE \"{escapedTable}\" SET {string.Join(", ", setters)} WHERE rowid = {ToSqlLiteral(rowId)};";
                using var updateCommand = new SharpCoreDBCommand(updateSql, ActiveConnection);
                await updateCommand.ExecuteNonQueryAsync().ConfigureAwait(true);
            }

            await LoadSelectedTablePageAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorQueryFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedResultRow()
    {
        if (!IsResultEditable || ActiveConnection is null || string.IsNullOrWhiteSpace(SelectedTable) || SelectedResultRow is null
        )
        {
            return;
        }

        try
        {
            if (SelectedResultRow.IsNew)
            {
                QueryResults.Remove(SelectedResultRow);
                return;
            }

            var rowId = SelectedResultRow.GetValue("__rowid__", ResultColumns);
            if (rowId is null || rowId == DBNull.Value)
            {
                return;
            }

            var escapedTable = SelectedTable.Replace("\"", "\"\"", StringComparison.Ordinal);
            var deleteSql = $"DELETE FROM \"{escapedTable}\" WHERE rowid = {ToSqlLiteral(rowId)};";
            using var deleteCommand = new SharpCoreDBCommand(deleteSql, ActiveConnection);
            await deleteCommand.ExecuteNonQueryAsync().ConfigureAwait(true);

            await LoadSelectedTablePageAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorQueryFailed", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task PreviewSelectedTable(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        CurrentOffset = 0;
        IsResultEditable = false;
        ResultMode = ResultMode.View;
        await LoadSelectedTablePageAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task SelectTop100(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        PageSize = ResolveSelectTopNOrDefault();
        CurrentOffset = 0;
        IsResultEditable = false;
        ResultMode = ResultMode.View;
        await LoadSelectedTablePageAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CreateNewTable()
    {
        if (ActiveConnection is null)
        {
            StatusMessage = _localization["NotConnected"];
            return;
        }

        if (!IsValidSqlIdentifier(NewTableName))
        {
            StatusMessage = "Invalid table name. Use letters, digits and underscore only.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewTableColumnsDefinition))
        {
            StatusMessage = "Column definition is required.";
            return;
        }

        try
        {
            var trimmedName = NewTableName.Trim();
            if (_allTables.Any(t => string.Equals(t, trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = $"Table '{trimmedName}' already exists.";
                return;
            }

            var createSql = $"CREATE TABLE IF NOT EXISTS {trimmedName} ({NewTableColumnsDefinition});";
            using var command = new SharpCoreDBCommand(createSql, ActiveConnection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(true);

            NewTableName = string.Empty;
            await LoadTablesAsync().ConfigureAwait(true);
            SelectedTable = trimmedName;
            await LoadSelectedTableMetadataAsync(SelectedTable).ConfigureAwait(true);
            StatusMessage = $"Table '{trimmedName}' created.";
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorQueryFailed", GetDetailedErrorMessage(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task DropSelectedTable(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (ActiveConnection is null || string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        try
        {
            var escapedTable = SelectedTable.Replace("\"", "\"\"", StringComparison.Ordinal);
            using var command = new SharpCoreDBCommand($"DROP TABLE IF EXISTS \"{escapedTable}\";", ActiveConnection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(true);

            StatusMessage = $"Table '{SelectedTable}' dropped.";
            QueryResults.Clear();
            HasResults = false;
            SelectedTable = null;
            await LoadTablesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorQueryFailed", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task ScriptSelectedTable(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (ActiveConnection is null || string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        try
        {
            var escapedTable = SelectedTable.Replace("'", "''", StringComparison.Ordinal);
            using var command = new SharpCoreDBCommand($"SELECT sql FROM sqlite_master WHERE type='table' AND name='{escapedTable}' LIMIT 1;", ActiveConnection);
            var result = await command.ExecuteScalarAsync().ConfigureAwait(true);
            QueryText = result?.ToString() ?? string.Empty;
            StatusMessage = string.IsNullOrWhiteSpace(QueryText)
                ? "No table script found."
                : "Table script loaded in editor.";
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorQueryFailed", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task RenameSelectedTable(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (ActiveConnection is null || string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        if (!IsValidSqlIdentifier(RenameTableName))
        {
            StatusMessage = "Invalid new table name. Use letters, digits and underscore only.";
            return;
        }

        var newName = RenameTableName.Trim();
        if (string.Equals(newName, SelectedTable, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var escapedCurrent = SelectedTable.Replace("\"", "\"\"", StringComparison.Ordinal);
            var escapedNew = newName.Replace("\"", "\"\"", StringComparison.Ordinal);
            using var command = new SharpCoreDBCommand($"ALTER TABLE \"{escapedCurrent}\" RENAME TO \"{escapedNew}\";", ActiveConnection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(true);

            await LoadTablesAsync().ConfigureAwait(true);
            SelectedTable = newName;
            StatusMessage = $"Table renamed to '{newName}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorQueryFailed", ex.Message);
        }
    }

    private bool IsValidSqlIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        return Regex.IsMatch(identifier.Trim(), "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private async Task ExecutePreviousResultPageAsync()
    {
        if (CurrentOffset <= 0)
        {
            return;
        }

        CurrentOffset = Math.Max(0, CurrentOffset - PageSize);
        await LoadSelectedTablePageAsync().ConfigureAwait(true);
    }

    private async Task ExecuteNextResultPageAsync()
    {
        if (!CanGoNextPage)
        {
            return;
        }

        CurrentOffset += PageSize;
        await LoadSelectedTablePageAsync().ConfigureAwait(true);
    }

    private async Task ExecuteRefreshCurrentPreviewPageAsync()
    {
        await LoadSelectedTablePageAsync().ConfigureAwait(true);
    }

    private bool CanGoPreviousPageInternal() => CanGoPreviousPage;

    private bool CanGoNextPageInternal() => CanGoNextPage;

    private bool CanPreviewSelectedTable() => !string.IsNullOrWhiteSpace(SelectedTable);

    [RelayCommand]
    private async Task RefreshTables()
    {
        await LoadTablesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExecuteQuery()
    {
        if (!IsConnected || ActiveConnection is null)
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
            IsResultEditable = false;
            ResultMode = ResultMode.View;
            CanGoPreviousPage = false;
            CanGoNextPage = false;
            CurrentOffset = 0;
            CurrentPreviewTotalRows = 0;

            var statements = SplitSqlStatements(QueryText);
            if (statements.Count == 0)
            {
                StatusMessage = _localization["ErrorQueryEmpty"];
                return;
            }

            var isSingleFile = string.Equals(ActiveConnection.DbInstance?.StorageMode.ToString(), "SingleFile", StringComparison.OrdinalIgnoreCase);
            var totalAffected = 0;
            var selectResultCount = 0;
            var hasSelectStatement = false;
            var executedStatementsCount = 0;

            QueryResults.Clear();
            HasResults = false;

            var anySchemaChange = false;

            for (var i = 0; i < statements.Count; i++)
            {
                var baseStatement = statements[i];
                var baseUpper = baseStatement.TrimStart().ToUpperInvariant();
                var compatibleStatements = ExpandStatementForCompatibility(baseStatement, isSingleFile).ToList();

                // PERF: For single-file multi-row INSERT, execute as one batch call to avoid per-row flush overhead.
                if (isSingleFile
                    && compatibleStatements.Count > 1
                    && baseUpper.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                    && ActiveConnection.DbInstance is not null)
                {
                    await ExecuteBatchWithoutBlockingUiAsync(compatibleStatements).ConfigureAwait(true);
                    executedStatementsCount += compatibleStatements.Count;
                    continue;
                }

                foreach (var statement in compatibleStatements)
                {
                    var trimmedStatement = statement.TrimStart().ToUpperInvariant();
                    executedStatementsCount++;

                    if (trimmedStatement.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                        || trimmedStatement.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)
                        || trimmedStatement.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase))
                    {
                        using var command = new SharpCoreDBCommand(statement, ActiveConnection);
                        hasSelectStatement = true;
                        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);

                        ObservableCollection<Dictionary<string, object>> results = [];
                        var rowCount = 0;

                        List<string> columnNames = [];
                        for (var j = 0; j < reader.FieldCount; j++)
                        {
                            columnNames.Add(reader.GetName(j));
                        }

                        while (await reader.ReadAsync().ConfigureAwait(true))
                        {
                            Dictionary<string, object> row = [];
                            for (var j = 0; j < reader.FieldCount; j++)
                            {
                                row[reader.GetName(j)] = reader.GetValue(j) ?? DBNull.Value;
                            }

                            results.Add(row);
                            rowCount++;
                        }

                        QueryResults.Clear();
                        foreach (var dictRow in results)
                        {
                            QueryResults.Add(QueryResultRow.FromDictionary(dictRow, columnNames));
                        }

                        ResultColumns = columnNames;
                        HasResults = rowCount > 0;
                        selectResultCount = rowCount;

                        if (columnNames.Count > 0)
                        {
                            GenerateDataGridColumns(columnNames);
                        }
                    }
                    else
                    {
                        var affected = await ExecuteNonQueryWithoutBlockingUiAsync(statement).ConfigureAwait(true);
                        if (affected >= 0)
                        {
                            totalAffected += affected;
                        }
                    }

                    if (trimmedStatement.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                        || trimmedStatement.StartsWith("DROP", StringComparison.OrdinalIgnoreCase)
                        || trimmedStatement.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase))
                    {
                        anySchemaChange = true;
                    }
                }
            }

            if (hasSelectStatement)
            {
                StatusMessage = totalAffected > 0
                    ? _localization.Format("StatusStatementsRowsAndAffected", executedStatementsCount, selectResultCount, totalAffected)
                    : _localization.Format("StatusStatementsRows", executedStatementsCount, selectResultCount);
            }
            else if (totalAffected > 0)
            {
                StatusMessage = _localization.Format("StatusStatementsAffected", executedStatementsCount, totalAffected);
            }
            else
            {
                StatusMessage = _localization.Format("StatusStatementsExecuted", executedStatementsCount);
            }

            if (anySchemaChange)
            {
                await LoadTablesAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = ex.InnerException is not null
                ? $"{ex.Message} → {ex.InnerException.Message}"
                : ex.Message;

            StatusMessage = _localization.Format("ErrorQueryFailed", errorMsg);
            QueryResults.Clear();
            HasResults = false;
        }
    }

    private async Task<int> ExecuteNonQueryWithoutBlockingUiAsync(string statement)
    {
        if (ActiveConnection is null)
        {
            return -1;
        }

        return await Task.Run(() =>
        {
            using var command = new SharpCoreDBCommand(statement, ActiveConnection);
            return command.ExecuteNonQuery();
        }).ConfigureAwait(true);
    }

    private async Task ExecuteBatchWithoutBlockingUiAsync(IReadOnlyList<string> statements)
    {
        if (ActiveConnection?.DbInstance is null || statements.Count == 0)
        {
            return;
        }

        await Task.Run(() =>
        {
            ActiveConnection.DbInstance.ExecuteBatchSQL(statements);
            ActiveConnection.DbInstance.Flush();
        }).ConfigureAwait(true);
    }

    private static List<string> SplitSqlStatements(string sql)
    {
        return sql
            .Split([';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeStatement)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static string NormalizeStatement(string statement)
    {
        var normalized = statement.Trim();

        while (normalized.StartsWith("/*", StringComparison.Ordinal))
        {
            var end = normalized.IndexOf("*/", StringComparison.Ordinal);
            if (end < 0)
            {
                return string.Empty;
            }

            normalized = normalized[(end + 2)..].TrimStart();
        }

        var lines = normalized.Split(['\r', '\n'], StringSplitOptions.None);
        var keptLines = new List<string>(lines.Length);
        var startedSql = false;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (!startedSql && (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal)))
            {
                continue;
            }

            startedSql = true;
            keptLines.Add(rawLine);
        }

        return string.Join(Environment.NewLine, keptLines).Trim();
    }

    private static IEnumerable<string> ExpandStatementForCompatibility(string statement, bool isSingleFile)
    {
        if (!isSingleFile)
        {
            return [statement];
        }

        var upper = statement.TrimStart().ToUpperInvariant();
        if (!upper.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || !upper.Contains("VALUES", StringComparison.OrdinalIgnoreCase))
        {
            return [statement];
        }

        var valuesIndex = upper.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        if (valuesIndex < 0)
        {
            return [statement];
        }

        var prefix = statement[..(valuesIndex + "VALUES".Length)].TrimEnd();
        var valuesPart = statement[(valuesIndex + "VALUES".Length)..].Trim();

        var tuples = ParseInsertValueTuples(valuesPart);
        if (tuples.Count <= 1)
        {
            return [statement];
        }

        return tuples.Select(tuple => $"{prefix} {tuple}");
    }

    private static List<string> ParseInsertValueTuples(string valuesPart)
    {
        var tuples = new List<string>();
        var start = -1;
        var depth = 0;
        var inString = false;

        for (var i = 0; i < valuesPart.Length; i++)
        {
            var ch = valuesPart[i];

            if (ch == '\'')
            {
                if (inString && i + 1 < valuesPart.Length && valuesPart[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '(')
            {
                if (depth == 0)
                {
                    start = i;
                }

                depth++;
            }
            else if (ch == ')')
            {
                if (depth == 0)
                {
                    continue;
                }

                depth--;
                if (depth == 0 && start >= 0)
                {
                    tuples.Add(valuesPart[start..(i + 1)].Trim());
                    start = -1;
                }
            }
        }

        return tuples;
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task GenerateInsertTemplate(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        var columns = await GetTableColumnsAsync(SelectedTable).ConfigureAwait(true);
        var tableRef = BuildTableReference(SelectedTable);

        if (columns.Count == 0)
        {
            QueryText = $"INSERT INTO {tableRef} (column1, column2) VALUES ('value1', 'value2');";
            StatusMessage = $"INSERT template generated for '{SelectedTable}' (fallback template).";
            return;
        }

        var columnList = string.Join(", ", columns.Select(static c => c.Name));
        var valueList = string.Join(", ", columns.Select(static c => "NULL"));

        QueryText = $"INSERT INTO {tableRef} ({columnList}) VALUES ({valueList});";
        StatusMessage = $"INSERT template generated for '{SelectedTable}'.";
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task GenerateUpdateTemplate(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        var columns = await GetTableColumnsAsync(SelectedTable).ConfigureAwait(true);
        var tableRef = BuildTableReference(SelectedTable);

        if (columns.Count == 0)
        {
            QueryText = $"UPDATE {tableRef} SET column1 = 'value' WHERE column2 = 'value';";
            StatusMessage = $"UPDATE template generated for '{SelectedTable}' (fallback template).";
            return;
        }

        var firstKey = columns[0].Name;
        var setters = string.Join(", ", columns.Where(c => !string.Equals(c.Name, firstKey, StringComparison.OrdinalIgnoreCase)).Select(c => $"{c.Name} = NULL"));
        if (string.IsNullOrWhiteSpace(setters))
        {
            setters = $"{firstKey} = NULL";
        }

        QueryText = $"UPDATE {tableRef} SET {setters} WHERE {firstKey} = NULL;";
        StatusMessage = $"UPDATE template generated for '{SelectedTable}'.";
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedTable))]
    private async Task GenerateDeleteTemplate(string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            SelectedTable = tableName;
        }

        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        var columns = await GetTableColumnsAsync(SelectedTable).ConfigureAwait(true);
        var whereColumn = columns.Count > 0 ? columns[0].Name : "rowid";
        var tableRef = BuildTableReference(SelectedTable);

        QueryText = $"DELETE FROM {tableRef} WHERE {whereColumn} = NULL;";
        StatusMessage = $"DELETE template generated for '{SelectedTable}'.";
    }

    private string BuildTableReference(string tableName)
    {
        if (IsValidSqlIdentifier(tableName))
        {
            return tableName;
        }

        var escaped = tableName.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
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

    private async Task LoadTablesAsync()
    {
        if (ActiveConnection is null)
        {
            return;
        }

        try
        {
            var loadedTables = new List<string>();

            using (var command = new SharpCoreDBCommand("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name", ActiveConnection))
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(true))
            {
                while (await reader.ReadAsync().ConfigureAwait(true))
                {
                    var tableName = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(tableName))
                    {
                        loadedTables.Add(tableName);
                    }
                }
            }

            if (loadedTables.Count == 0)
            {
                var schema = ActiveConnection.GetSchema("Tables");
                foreach (DataRow row in schema.Rows)
                {
                    var tableName = row["TABLE_NAME"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(tableName))
                    {
                        loadedTables.Add(tableName);
                    }
                }
            }

            _allTables.Clear();
            foreach (var table in loadedTables.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static t => t, StringComparer.OrdinalIgnoreCase))
            {
                _allTables.Add(table);
            }

            ApplyTableFilter(TableFilterText);
            await RebuildExplorerNodesAsync().ConfigureAwait(true);

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

    private async Task RebuildExplorerNodesAsync()
    {
        ExplorerNodes.Clear();

        foreach (var table in _allTables)
        {
            var tableNode = new ExplorerNode
            {
                NodeType = ExplorerNodeType.Table,
                Name = table,
                TableName = table
            };

            var columns = await GetTableColumnsAsync(table).ConfigureAwait(true);
            foreach (var column in columns)
            {
                tableNode.Children.Add(new ExplorerNode
                {
                    NodeType = ExplorerNodeType.Column,
                    Name = column.Name,
                    DataType = column.Type,
                    TableName = table
                });
            }

            ExplorerNodes.Add(tableNode);
        }

        if (!string.IsNullOrWhiteSpace(SelectedTable))
        {
            SelectedExplorerNode = ExplorerNodes.FirstOrDefault(n => string.Equals(n.Name, SelectedTable, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task<List<(string Name, string Type)>> GetTableColumnsAsync(string tableName)
    {
        if (ActiveConnection is null || string.IsNullOrWhiteSpace(tableName))
        {
            return [];
        }

        var cached = ExplorerNodes.FirstOrDefault(n => n.NodeType == ExplorerNodeType.Table && string.Equals(n.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (cached is not null && cached.Children.Count > 0)
        {
            return cached.Children
                .Where(c => c.NodeType == ExplorerNodeType.Column && !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => (c.Name, string.IsNullOrWhiteSpace(c.DataType) ? "TEXT" : c.DataType!))
                .ToList();
        }

        var columns = new List<(string Name, string Type)>();
        var escapedTableSingleQuote = tableName.Replace("'", "''", StringComparison.Ordinal);
        var tableRef = BuildTableReference(tableName);

        try
        {
            using var command = new SharpCoreDBCommand($"PRAGMA table_info('{escapedTableSingleQuote}')", ActiveConnection);
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);
            while (await reader.ReadAsync().ConfigureAwait(true))
            {
                var name = reader.GetValue(1)?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var type = reader.GetValue(2)?.ToString();
                columns.Add((name, string.IsNullOrWhiteSpace(type) ? "TEXT" : type));
            }
        }
        catch
        {
        }

        if (columns.Count == 0)
        {
            try
            {
                using var selectCommand = new SharpCoreDBCommand($"SELECT * FROM {tableRef}", ActiveConnection);
                using var reader = await selectCommand.ExecuteReaderAsync().ConfigureAwait(true);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        columns.Add((name, "TEXT"));
                    }
                }
            }
            catch
            {
            }
        }

        return columns.GroupBy(static c => c.Name, StringComparer.OrdinalIgnoreCase).Select(static g => g.First()).ToList();
    }

    private async Task LoadSelectedTablePageAsync()
    {
        if (ActiveConnection is null || string.IsNullOrWhiteSpace(SelectedTable))
        {
            return;
        }

        try
        {
            var isSingleFile = string.Equals(ActiveConnection.DbInstance?.StorageMode.ToString(), "SingleFile", StringComparison.OrdinalIgnoreCase);
            var tableRef = BuildTableReference(SelectedTable);

            QueryText = isSingleFile
                ? $"SELECT * FROM {tableRef};"
                : $"SELECT rowid AS __rowid__, * FROM {tableRef} LIMIT {PageSize} OFFSET {CurrentOffset};";

            using var command = new SharpCoreDBCommand(QueryText, ActiveConnection);
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);

            QueryResults.Clear();
            List<string> columnNames = [];
            for (var j = 0; j < reader.FieldCount; j++)
            {
                columnNames.Add(reader.GetName(j));
            }

            while (await reader.ReadAsync().ConfigureAwait(true))
            {
                Dictionary<string, object> row = [];
                for (var j = 0; j < reader.FieldCount; j++)
                {
                    row[reader.GetName(j)] = reader.GetValue(j) ?? DBNull.Value;
                }

                QueryResults.Add(QueryResultRow.FromDictionary(row, columnNames));
            }

            ResultColumns = columnNames;
            HasResults = QueryResults.Count > 0;
            if (columnNames.Count > 0)
            {
                GenerateDataGridColumns(columnNames);
            }

            if (isSingleFile)
            {
                CurrentPreviewTotalRows = QueryResults.Count;
                CanGoPreviousPage = false;
                CanGoNextPage = false;
                StatusMessage = $"Rows: {QueryResults.Count}";
                return;
            }

            using var countCommand = new SharpCoreDBCommand($"SELECT COUNT(*) FROM {tableRef}", ActiveConnection);
            var countObj = await countCommand.ExecuteScalarAsync().ConfigureAwait(true);
            CurrentPreviewTotalRows = countObj is null || countObj == DBNull.Value ? 0 : Convert.ToInt64(countObj, CultureInfo.InvariantCulture);

            CanGoPreviousPage = CurrentOffset > 0;
            CanGoNextPage = CurrentOffset + PageSize < CurrentPreviewTotalRows;
            var shownFrom = CurrentPreviewTotalRows == 0 ? 0 : CurrentOffset + 1;
            var shownTo = Math.Min(CurrentOffset + QueryResults.Count, CurrentPreviewTotalRows);
            StatusMessage = $"Rows {shownFrom}-{shownTo} / {CurrentPreviewTotalRows}";
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ErrorQueryFailed", ex.Message);
            QueryResults.Clear();
            HasResults = false;
            CurrentPreviewTotalRows = 0;
            CanGoPreviousPage = false;
            CanGoNextPage = false;
        }
    }

    private async Task LoadSelectedTableMetadataAsync(string? tableName)
    {
        TableColumnsMetadata.Clear();
        TableIndexesMetadata.Clear();
        TableTriggersMetadata.Clear();

        if (ActiveConnection is null || string.IsNullOrWhiteSpace(tableName))
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

            if (TableColumnsMetadata.Count == 0) TableColumnsMetadata.Add(_localization["MetadataNoneAvailable"]);
            if (TableIndexesMetadata.Count == 0) TableIndexesMetadata.Add(_localization["MetadataNoneAvailable"]);
            if (TableTriggersMetadata.Count == 0) TableTriggersMetadata.Add(_localization["MetadataNoneAvailable"]);

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

    public event EventHandler<List<string>>? ColumnsChanged;
    private List<string>? _lastGeneratedColumns;

    private void GenerateDataGridColumns(List<string> columnNames)
    {
        if (_lastGeneratedColumns is not null && _lastGeneratedColumns.Count == columnNames.Count && _lastGeneratedColumns.SequenceEqual(columnNames))
        {
            return;
        }

        _lastGeneratedColumns = [.. columnNames];
        ColumnsChanged?.Invoke(this, columnNames);
    }

    public void SetConnection(SharpCoreDBConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = _localization["NotConnected"];
            StatusMessage = $"Connection failed: {ex.Message}";
            return;
        }

        if (ActiveConnection is not null && !ReferenceEquals(ActiveConnection, connection))
        {
            CloseActiveConnection();
        }

        ActiveConnection = connection;
        IsConnected = connection.State == ConnectionState.Open;
        ConnectionStatus = IsConnected ? _localization.Format("ConnectedTo", connection.DataSource) : _localization["NotConnected"];
        ResultMode = ResultMode.View;
        IsResultEditable = false;
        CurrentOffset = 0;
        CurrentPreviewTotalRows = 0;
        CanGoPreviousPage = false;
        CanGoNextPage = false;

        if (IsConnected)
        {
            _ = LoadTablesAsync();
        }
    }

    public void CleanupOnWindowClose()
    {
        try
        {
            ActiveConnection?.DbInstance?.ForceSave();
        }
        catch
        {
        }

        CloseActiveConnection(background: false);
        IsConnected = false;
    }

    private void CloseActiveConnection(bool background = false)
    {
        var connection = ActiveConnection;
        ActiveConnection = null;

        if (connection is null)
        {
            return;
        }

        if (!background)
        {
            connection.Close();
            connection.Dispose();
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                connection.Close();
                connection.Dispose();
            }
            catch
            {
            }
        });
    }

    private static string ToSqlLiteral(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return "NULL";
        }

        return value switch
        {
            bool boolValue => boolValue ? "1" : "0",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL",
            DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.fffffff}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset:O}'",
            _ => $"'{value.ToString()?.Replace("'", "''", StringComparison.Ordinal) ?? string.Empty}'"
        };
    }

    private static string GetDetailedErrorMessage(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var messages = new List<string>();
        var current = ex;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                messages.Add(current.Message.Trim());
            }

            current = current.InnerException!;
        }

        return messages.Count == 0 ? "Unknown error." : string.Join(" -> ", messages.Distinct(StringComparer.Ordinal));
    }
}
