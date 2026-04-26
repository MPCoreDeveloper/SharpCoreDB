using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SharpCoreDB.WebViewer.Models;
using SharpCoreDB.WebViewer.Services;

namespace SharpCoreDB.WebViewer.Pages;

/// <summary>
/// Hosts the web viewer landing page and startup state.
/// </summary>
public sealed class IndexModel(
    IRecentConnectionsStore recentConnectionsStore,
    IQueryWorkspaceStore queryWorkspaceStore,
    IViewerConnectionService viewerConnectionService,
    IViewerTransactionService transactionService,
    IMetadataService metadataService,
    IViewerQueryService viewerQueryService,
    IOptions<WebViewerOptions> options) : PageModel
{
    private readonly IRecentConnectionsStore _recentConnectionsStore = recentConnectionsStore;
    private readonly IQueryWorkspaceStore _queryWorkspaceStore = queryWorkspaceStore;
    private readonly IViewerConnectionService _viewerConnectionService = viewerConnectionService;
    private readonly IViewerTransactionService _transactionService = transactionService;
    private readonly IMetadataService _metadataService = metadataService;
    private readonly IViewerQueryService _viewerQueryService = viewerQueryService;
    private readonly WebViewerOptions _options = options.Value;

    [BindProperty]
    public ConnectionRequest Connection { get; set; } = CreateDefaultConnection();

    [BindProperty]
    public QueryExecutionRequest Query { get; set; } = CreateDefaultQuery();

    [BindProperty]
    public string SaveQueryName { get; set; } = string.Empty;

    [BindProperty]
    public string ImportWorkspaceJson { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? SelectedTable { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<ConnectionProfile> RecentConnections { get; private set; } = [];

    public IReadOnlyList<SavedQueryItem> SavedQueries { get; private set; } = [];

    public IReadOnlyList<QueryHistoryItem> QueryHistory { get; private set; } = [];

    public IReadOnlyList<string> Tables { get; private set; } = [];

    public ViewerSessionState? ActiveSession { get; private set; }

    public ViewerTransactionState? ActiveTransaction { get; private set; }

    public TableMetadata? SelectedTableMetadata { get; private set; }

    public QueryExecutionResult? QueryResult { get; private set; }

    public string EndpointDisplay => $"https://{_options.BindAddress}:{_options.HttpsPort}";

    public int QueryTimeoutSeconds => _options.QueryTimeoutSeconds;

    public int ResultRowLimit => _options.ResultRowLimit;

    public bool IsConnected => ActiveSession is not null;

    public bool HasQueryResult => QueryResult is not null;

    public bool IsTransactionActive => ActiveTransaction is not null;

    public string? ActiveTargetKey => BuildTargetKey(
        ActiveSession?.ConnectionMode,
        ActiveSession?.LocalDatabasePath,
        ActiveSession?.ServerHost,
        ActiveSession?.ServerPort,
        ActiveSession?.ServerDatabase);

    public string ActiveTargetDisplay => ActiveSession?.DisplayTarget ?? "Global";

    /// <summary>
    /// Loads local viewer startup data.
    /// </summary>
    /// <returns>A task that completes when page data is prepared.</returns>
    public async Task OnGetAsync()
    {
        await LoadPageStateAsync(HttpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects the current browser session to a local SharpCoreDB database or network server.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostConnectAsync()
    {
        ValidateConnectionForm();

        if (!ModelState.IsValid)
        {
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true).ConfigureAwait(false);
            return Page();
        }

        try
        {
            await _transactionService.ClearAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            var session = await _viewerConnectionService.ConnectAsync(Connection, HttpContext.RequestAborted).ConfigureAwait(false);
            await SaveRecentConnectionAsync(session, HttpContext.RequestAborted).ConfigureAwait(false);
            StatusMessage = $"Connected to {session.DisplayTarget}.";
            Query = CreateDefaultQuery();
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true).ConfigureAwait(false);
            return Page();
        }
    }

    /// <summary>
    /// Disconnects the current browser session from the active database.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        await _transactionService.ClearAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        await _viewerConnectionService.DisconnectAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        StatusMessage = "Disconnected from the active database.";
        return RedirectToPage();
    }

    /// <summary>
    /// Starts a transaction for the active session.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostBeginTransactionAsync()
    {
        ClearConnectionModelState();

        if (!IsConnected)
        {
            ErrorMessage = "Connect to a database before starting a transaction.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
            return Page();
        }

        try
        {
            var state = await _transactionService.BeginAsync(User?.Identity?.Name, HttpContext.RequestAborted).ConfigureAwait(false);
            StatusMessage = $"Transaction started ({state.ConnectionMode}).";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Commits the active transaction.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostCommitTransactionAsync()
    {
        ClearConnectionModelState();

        try
        {
            await _transactionService.CommitAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            StatusMessage = "Transaction committed.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Rolls back the active transaction.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostRollbackTransactionAsync()
    {
        ClearConnectionModelState();

        try
        {
            await _transactionService.RollbackAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            StatusMessage = "Transaction rolled back.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Saves the current SQL editor content as a reusable query template.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostSaveQueryAsync()
    {
        ClearConnectionModelState();

        if (string.IsNullOrWhiteSpace(Query.Sql))
        {
            ErrorMessage = "Enter SQL before saving a query.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
            return Page();
        }

        var queryName = string.IsNullOrWhiteSpace(SaveQueryName)
            ? BuildDefaultQueryName(Query.Sql)
            : SaveQueryName.Trim();

        var workspace = await _queryWorkspaceStore.LoadAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var targetKey = ActiveTargetKey;
        var targetDisplay = IsConnected ? ActiveTargetDisplay : "Global";
        var mode = ActiveSession?.ConnectionMode ?? Connection.ConnectionMode;

        var remaining = workspace.SavedQueries
            .Where(saved => !IsSameSavedQuerySlot(saved, queryName, mode, targetKey))
            .ToList();

        remaining.Add(new SavedQueryItem
        {
            Id = Guid.NewGuid(),
            Name = queryName,
            Sql = Query.Sql,
            ParametersJson = Query.ParametersJson,
            ConnectionMode = mode,
            TargetKey = targetKey,
            TargetDisplay = targetDisplay,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastUsedUtc = DateTimeOffset.UtcNow
        });

        await _queryWorkspaceStore.SaveAsync(new QueryWorkspaceState
        {
            SavedQueries = remaining,
            History = workspace.History
        }, HttpContext.RequestAborted).ConfigureAwait(false);

        SaveQueryName = string.Empty;
        StatusMessage = $"Saved query '{queryName}'.";
        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Loads a saved query into the SQL editor.
    /// </summary>
    /// <param name="queryId">Saved query identifier.</param>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostLoadSavedQueryAsync(Guid queryId)
    {
        ClearConnectionModelState();

        var workspace = await _queryWorkspaceStore.LoadAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var savedQuery = workspace.SavedQueries.FirstOrDefault(item => item.Id == queryId);
        if (savedQuery is null)
        {
            ErrorMessage = "The selected saved query was not found.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
            return Page();
        }

        Query = new QueryExecutionRequest
        {
            Sql = savedQuery.Sql,
            ParametersJson = savedQuery.ParametersJson
        };

        var updatedSaved = workspace.SavedQueries
            .Select(item => item.Id == queryId ? item with { LastUsedUtc = DateTimeOffset.UtcNow } : item)
            .ToArray();

        await _queryWorkspaceStore.SaveAsync(new QueryWorkspaceState
        {
            SavedQueries = updatedSaved,
            History = workspace.History
        }, HttpContext.RequestAborted).ConfigureAwait(false);

        StatusMessage = $"Loaded saved query '{savedQuery.Name}'.";
        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Removes a saved query from the query library.
    /// </summary>
    /// <param name="queryId">Saved query identifier.</param>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostDeleteSavedQueryAsync(Guid queryId)
    {
        ClearConnectionModelState();

        var workspace = await _queryWorkspaceStore.LoadAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var removed = workspace.SavedQueries.FirstOrDefault(item => item.Id == queryId);

        await _queryWorkspaceStore.SaveAsync(new QueryWorkspaceState
        {
            SavedQueries = workspace.SavedQueries.Where(item => item.Id != queryId).ToArray(),
            History = workspace.History
        }, HttpContext.RequestAborted).ConfigureAwait(false);

        StatusMessage = removed is null ? "Saved query removed." : $"Deleted saved query '{removed.Name}'.";
        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Clears all query execution history entries.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostClearHistoryAsync()
    {
        ClearConnectionModelState();

        var workspace = await _queryWorkspaceStore.LoadAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        await _queryWorkspaceStore.SaveAsync(new QueryWorkspaceState
        {
            SavedQueries = workspace.SavedQueries,
            History = []
        }, HttpContext.RequestAborted).ConfigureAwait(false);

        StatusMessage = "Query history cleared.";
        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Exports the query workspace as JSON into the import/export textbox.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostExportWorkspaceAsync()
    {
        ClearConnectionModelState();

        ImportWorkspaceJson = await _queryWorkspaceStore.ExportAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        StatusMessage = "Workspace exported to JSON payload.";

        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        return Page();
    }

    /// <summary>
    /// Imports the query workspace from JSON.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostImportWorkspaceAsync()
    {
        ClearConnectionModelState();

        if (string.IsNullOrWhiteSpace(ImportWorkspaceJson))
        {
            ErrorMessage = "Paste workspace JSON before importing.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
            return Page();
        }

        try
        {
            await _queryWorkspaceStore.ImportAsync(ImportWorkspaceJson, HttpContext.RequestAborted).ConfigureAwait(false);
            StatusMessage = "Workspace imported successfully.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
            return Page();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
            return Page();
        }
    }

    /// <summary>
    /// Loads a saved recent connection profile into the connection form.
    /// </summary>
    /// <param name="name">Profile name key.</param>
    /// <param name="target">Profile target key.</param>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostLoadProfileAsync(string name, string target)
    {
        ClearConnectionModelState();

        RecentConnections = await _recentConnectionsStore.LoadAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var profile = RecentConnections.FirstOrDefault(profile =>
            string.Equals(profile.Name, name, StringComparison.Ordinal)
            && string.Equals(profile.DisplayTarget, target, StringComparison.Ordinal));

        if (profile is null)
        {
            ErrorMessage = "The selected recent connection was not found.";
            await LoadPageStateAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            return Page();
        }

        Connection = new ConnectionRequest
        {
            Name = profile.Name,
            ConnectionMode = profile.ConnectionMode,
            LocalDatabasePath = profile.LocalDatabasePath ?? string.Empty,
            LocalStorageMode = profile.LocalStorageMode,
            LocalReadOnly = profile.LocalReadOnly,
            ServerHost = profile.ServerHost ?? "localhost",
            ServerPort = profile.ServerPort,
            ServerUseSsl = profile.ServerUseSsl,
            ServerPreferHttp3 = profile.ServerPreferHttp3,
            ServerDatabase = profile.ServerDatabase ?? "master",
            ServerUsername = profile.ServerUsername ?? "anonymous"
        };

        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        StatusMessage = $"Loaded recent profile '{profile.Name}'. Enter the password to connect.";
        return Page();
    }

    /// <summary>
    /// Executes SQL from the editor against the active database session.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostExecuteQueryAsync()
    {
        ClearConnectionModelState();

        if (!IsConnected)
        {
            ErrorMessage = "Connect to a database before executing SQL.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true).ConfigureAwait(false);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Query.Sql))
        {
            ErrorMessage = "Enter at least one SQL statement.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true).ConfigureAwait(false);
            return Page();
        }

        try
        {
            QueryResult = await _viewerQueryService.ExecuteAsync(Query, HttpContext.RequestAborted).ConfigureAwait(false);
            StatusMessage = QueryResult.Summary;
            await AppendHistoryAsync(success: true, statusMessage: QueryResult.Summary, HttpContext.RequestAborted).ConfigureAwait(false);
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);

            if (QueryResult.SchemaChanged)
            {
                StatusMessage = $"{QueryResult.Summary} Schema metadata reloaded.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            await AppendHistoryAsync(success: false, statusMessage: ex.Message, HttpContext.RequestAborted).ConfigureAwait(false);
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true).ConfigureAwait(false);
        }

        return Page();
    }

    /// <summary>
    /// Loads a preview query for the selected table into the SQL editor.
    /// </summary>
    /// <returns>The viewer page result.</returns>
    public async Task<IActionResult> OnPostPreviewTableAsync()
    {
        ClearConnectionModelState();

        if (string.IsNullOrWhiteSpace(SelectedTable))
        {
            ErrorMessage = "Select a table before loading a preview query.";
            await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
            return Page();
        }

        Query.Sql = $"SELECT * FROM \"{SelectedTable.Replace("\"", "\"\"", StringComparison.Ordinal)}\" LIMIT {_options.ResultRowLimit};";
        await LoadPageStateAsync(HttpContext.RequestAborted, preserveFormValues: true, keepQueryResult: true).ConfigureAwait(false);
        StatusMessage = $"Loaded preview query for table '{SelectedTable}'.";
        return Page();
    }

    private async Task LoadPageStateAsync(CancellationToken cancellationToken, bool preserveFormValues = false, bool keepQueryResult = false)
    {
        if (!keepQueryResult)
        {
            QueryResult = null;
        }

        RecentConnections = await _recentConnectionsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var workspace = await _queryWorkspaceStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        ActiveSession = _viewerConnectionService.GetCurrentSession();
        ActiveTransaction = _transactionService.GetActiveTransaction();

        var targetKey = ActiveTargetKey;
        SavedQueries = workspace.SavedQueries
            .Where(item => IsVisibleForTarget(item.TargetKey, targetKey))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        QueryHistory = workspace.History
            .Where(item => IsVisibleForTarget(item.TargetKey, targetKey))
            .OrderByDescending(static item => item.ExecutedAtUtc)
            .ToArray();

        if (ActiveSession is null)
        {
            if (!preserveFormValues)
            {
                Connection = CreateDefaultConnection();
                Query = CreateDefaultQuery();
                ImportWorkspaceJson = string.Empty;
            }

            Tables = [];
            SelectedTableMetadata = null;
            SetLayoutViewData();
            return;
        }

        if (!preserveFormValues)
        {
            Connection = new ConnectionRequest
            {
                Name = ActiveSession.Name,
                ConnectionMode = ActiveSession.ConnectionMode,
                LocalDatabasePath = ActiveSession.LocalDatabasePath ?? string.Empty,
                LocalStorageMode = ActiveSession.LocalStorageMode,
                LocalReadOnly = ActiveSession.LocalReadOnly,
                ServerHost = ActiveSession.ServerHost ?? "localhost",
                ServerPort = ActiveSession.ServerPort,
                ServerUseSsl = ActiveSession.ServerUseSsl,
                ServerPreferHttp3 = ActiveSession.ServerPreferHttp3,
                ServerDatabase = ActiveSession.ServerDatabase ?? "master",
                ServerUsername = ActiveSession.ServerUsername ?? "anonymous"
            };
            Query = CreateDefaultQuery();
        }

        try
        {
            Tables = await _metadataService.GetTableNamesAsync(cancellationToken).ConfigureAwait(false);
            if (Tables.Count == 0)
            {
                SelectedTable = null;
                SelectedTableMetadata = null;
                SetLayoutViewData();
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedTable) || !Tables.Contains(SelectedTable, StringComparer.OrdinalIgnoreCase))
            {
                SelectedTable = Tables[0];
            }

            SelectedTableMetadata = string.IsNullOrWhiteSpace(SelectedTable)
                ? null
                : await _metadataService.GetTableMetadataAsync(SelectedTable, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage ??= ex.Message;
            Tables = [];
            SelectedTableMetadata = null;
        }

        SetLayoutViewData();
    }

    /// <summary>
    /// Populates ViewData keys consumed by _Layout.cshtml chrome (menu bar, toolbar, status bar).
    /// Must be called after all model properties are set.
    /// </summary>
    private void SetLayoutViewData()
    {
        ViewData["Title"] = "Query";
        ViewData["IsConnected"] = IsConnected;
        ViewData["ActiveTarget"] = IsConnected ? ActiveTargetDisplay : null;
        ViewData["IsTransactionActive"] = IsTransactionActive;
        ViewData["EndpointDisplay"] = EndpointDisplay;

        if (QueryResult?.Rows is not null)
        {
            ViewData["RowCount"] = QueryResult.Rows.Count;
        }

        if (!string.IsNullOrWhiteSpace(StatusMessage))
        {
            ViewData["StatusMessage"] = StatusMessage;
        }
    }

    private async Task SaveRecentConnectionAsync(ViewerSessionState session, CancellationToken cancellationToken)
    {
        var profiles = (await _recentConnectionsStore.LoadAsync(cancellationToken).ConfigureAwait(false))
            .Where(profile =>
                !string.Equals(profile.Name, session.Name, StringComparison.Ordinal)
                || !string.Equals(profile.DisplayTarget, session.DisplayTarget, StringComparison.Ordinal))
            .ToList();

        profiles.Insert(0, new ConnectionProfile
        {
            Name = session.Name,
            ConnectionMode = session.ConnectionMode,
            LocalDatabasePath = session.LocalDatabasePath,
            LocalStorageMode = session.LocalStorageMode,
            LocalReadOnly = session.LocalReadOnly,
            ServerHost = session.ServerHost,
            ServerPort = session.ServerPort,
            ServerUseSsl = session.ServerUseSsl,
            ServerPreferHttp3 = session.ServerPreferHttp3,
            ServerDatabase = session.ServerDatabase,
            ServerUsername = session.ServerUsername,
            LastUsedUtc = DateTimeOffset.UtcNow
        });

        await _recentConnectionsStore.SaveAsync(profiles, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendHistoryAsync(bool success, string statusMessage, CancellationToken cancellationToken)
    {
        var workspace = await _queryWorkspaceStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var preview = Query.Sql.Length <= 180
            ? Query.Sql
            : Query.Sql[..180] + "...";

        var mode = ActiveSession?.ConnectionMode ?? Connection.ConnectionMode;
        var targetKey = ActiveTargetKey;
        var targetDisplay = IsConnected ? ActiveTargetDisplay : "Global";

        var history = workspace.History.ToList();
        history.Insert(0, new QueryHistoryItem
        {
            Id = Guid.NewGuid(),
            SqlPreview = preview,
            ParametersJson = Query.ParametersJson,
            ConnectionMode = mode,
            TargetKey = targetKey,
            TargetDisplay = targetDisplay,
            Succeeded = success,
            StatusMessage = statusMessage,
            ExecutedAtUtc = DateTimeOffset.UtcNow
        });

        await _queryWorkspaceStore.SaveAsync(new QueryWorkspaceState
        {
            SavedQueries = workspace.SavedQueries,
            History = history
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildDefaultQueryName(string sql)
    {
        var trimmed = sql.Trim();
        if (trimmed.Length <= 40)
        {
            return trimmed;
        }

        return trimmed[..40] + "...";
    }

    private static bool IsSameSavedQuerySlot(SavedQueryItem item, string queryName, ViewerConnectionMode mode, string? targetKey)
    {
        return string.Equals(item.Name, queryName, StringComparison.OrdinalIgnoreCase)
            && item.ConnectionMode == mode
            && string.Equals(item.TargetKey ?? string.Empty, targetKey ?? string.Empty, StringComparison.Ordinal);
    }

    private static bool IsVisibleForTarget(string? itemTargetKey, string? activeTargetKey)
    {
        if (string.IsNullOrWhiteSpace(itemTargetKey))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(activeTargetKey))
        {
            return false;
        }

        return string.Equals(itemTargetKey, activeTargetKey, StringComparison.Ordinal);
    }

    private static string? BuildTargetKey(
        ViewerConnectionMode? mode,
        string? localDatabasePath,
        string? serverHost,
        int? serverPort,
        string? serverDatabase)
    {
        if (mode is null)
        {
            return null;
        }

        return mode switch
        {
            ViewerConnectionMode.Server => $"server:{serverHost}:{serverPort}/{serverDatabase}".ToLowerInvariant(),
            ViewerConnectionMode.Local => string.IsNullOrWhiteSpace(localDatabasePath)
                ? null
                : $"local:{localDatabasePath}".ToLowerInvariant(),
            _ => null
        };
    }

    private void ValidateConnectionForm()
    {
        ClearConnectionModelState();

        if (Connection.ConnectionMode == ViewerConnectionMode.Server)
        {
            if (string.IsNullOrWhiteSpace(Connection.ServerHost))
            {
                ModelState.AddModelError($"{nameof(Connection)}.{nameof(Connection.ServerHost)}", "Server host is required.");
            }

            if (string.IsNullOrWhiteSpace(Connection.ServerDatabase))
            {
                ModelState.AddModelError($"{nameof(Connection)}.{nameof(Connection.ServerDatabase)}", "Server database is required.");
            }

            if (string.IsNullOrWhiteSpace(Connection.ServerUsername))
            {
                ModelState.AddModelError($"{nameof(Connection)}.{nameof(Connection.ServerUsername)}", "Server username is required.");
            }

            if (Connection.ServerPort is < 1 or > 65535)
            {
                ModelState.AddModelError($"{nameof(Connection)}.{nameof(Connection.ServerPort)}", "Server port must be between 1 and 65535.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Connection.LocalDatabasePath))
            {
                ModelState.AddModelError($"{nameof(Connection)}.{nameof(Connection.LocalDatabasePath)}", "Database path is required.");
            }

            if (string.IsNullOrWhiteSpace(Connection.Password))
            {
                ModelState.AddModelError($"{nameof(Connection)}.{nameof(Connection.Password)}", "Password is required.");
            }
        }
    }

    private void ClearConnectionModelState()
    {
        foreach (var key in ModelState.Keys.Where(static key => key.StartsWith(nameof(Connection), StringComparison.Ordinal)).ToArray())
        {
            ModelState.Remove(key);
        }
    }

    private static QueryExecutionRequest CreateDefaultQuery() => new()
    {
        Sql = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;",
        ParametersJson = string.Empty
    };

    private static ConnectionRequest CreateDefaultConnection() => new()
    {
        ConnectionMode = ViewerConnectionMode.Local,
        LocalStorageMode = DatabaseStorageMode.Directory,
        ServerHost = "localhost",
        ServerPort = 5001,
        ServerUseSsl = true,
        ServerPreferHttp3 = true,
        ServerDatabase = "master",
        ServerUsername = "anonymous"
    };
}
