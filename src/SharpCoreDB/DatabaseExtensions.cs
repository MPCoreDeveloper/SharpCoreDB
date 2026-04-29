// <copyright file="DatabaseExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Extension methods for configuring SharpCoreDB services.
/// Modern C# 14 with improved service registration patterns.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Adds SharpCoreDB services to the service collection.
    /// </summary>
    public static IServiceCollection AddSharpCoreDB(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddTransient<DatabaseFactory>();
        services.AddSingleton<SharpCoreDB.Services.WalManager>();
        
        return services;
    }
}

/// <summary>
/// Factory for creating Database instances with dependency injection.
/// Modern C# 14 primary constructor pattern with enhanced storage mode support.
/// </summary>
public class DatabaseFactory(IServiceProvider services)
{
    /// <summary>
    /// Creates a new Database instance (legacy method, backward compatible).
    /// </summary>
    public IDatabase Create(
        string dbPath, 
        string masterPassword, 
        bool isReadOnly = false, 
        DatabaseConfig? config = null, 
        SecurityConfig? securityConfig = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        
        var options = DetectStorageMode(dbPath, config);
        options.IsReadOnly = isReadOnly;
        
        return CreateWithOptions(dbPath, masterPassword, options);
    }

    /// <summary>
    /// Creates a new Database instance with DatabaseOptions (new API).
    /// Supports both directory and single-file storage modes.
    /// </summary>
    public IDatabase CreateWithOptions(string dbPath, string masterPassword, DatabaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        ArgumentNullException.ThrowIfNull(options);
        
        options.Validate();

        if (dbPath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
        {
            options.StorageMode = StorageMode.SingleFile;
        }

        return options.StorageMode switch
        {
            StorageMode.SingleFile => CreateSingleFileDatabase(dbPath, masterPassword, options),
            StorageMode.Directory => CreateDirectoryDatabase(dbPath, masterPassword, options),
            _ => throw new ArgumentException($"Invalid storage mode: {options.StorageMode}")
        };
    }

    private IDatabase CreateDirectoryDatabase(string dbPath, string masterPassword, DatabaseOptions options)
    {
        var config = options.DatabaseConfig ?? DatabaseConfig.Default;
        options.DatabaseConfig = config;
        return new Database(services, dbPath, masterPassword, options.IsReadOnly, config);
    }

    private static IDatabase CreateSingleFileDatabase(string dbPath, string masterPassword, DatabaseOptions options)
    {
        if (options.DatabaseConfig is not null)
        {
            options.EnableMemoryMapping = options.DatabaseConfig.UseMemoryMapping;
        }
        options.WalBufferSizePages = options.WalBufferSizePages > 0 ? options.WalBufferSizePages : 2048;
        options.FileShareMode = System.IO.FileShare.ReadWrite;
        var provider = SingleFileStorageProvider.Open(dbPath, options);
        return new SingleFileDatabase(provider, dbPath, masterPassword, options);
    }

    private static DatabaseOptions DetectStorageMode(string dbPath, DatabaseConfig? config)
    {
        var isSingleFile = dbPath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase) ||
                           File.Exists(dbPath) && !Directory.Exists(dbPath);

        var options = isSingleFile
            ? DatabaseOptions.CreateSingleFileDefault()
            : DatabaseOptions.CreateDirectoryDefault();

        if (config != null)
        {
            options.DatabaseConfig = config;
            options.EnableMemoryMapping = config.UseMemoryMapping;
            options.WalBufferSizePages = config.WalBufferSize / options.PageSize;
        }

        return options;
    }
}

/// <summary>
/// Database implementation for single-file (.scdb) storage.
/// Wraps SingleFileStorageProvider and provides IDatabase interface.
/// <para>
/// <b>⚠️ SQL LIMITATIONS:</b> This class uses a <b>regex-based SQL parser</b>, not the full <c>SqlParser</c> engine.
/// The following SQL features are <b>not supported</b>:
/// <list type="bullet">
///   <item>JOIN (INNER, LEFT, RIGHT, FULL OUTER, CROSS)</item>
///   <item>GROUP BY / HAVING</item>
///   <item>Subqueries</item>
///   <item>Aggregate functions (COUNT, SUM, AVG, MIN, MAX)</item>
///   <item>LIMIT / OFFSET</item>
///   <item>DELETE without WHERE clause</item>
///   <item>UPDATE without WHERE clause</item>
///   <item>Multi-row INSERT</item>
///   <item>ALTER TABLE</item>
/// </list>
/// For full SQL support use the <see cref="Database"/> class (directory mode) which routes through <c>SqlParser</c>.
/// </para>
/// <para>
/// See <c>docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md</c> for the complete support matrix.
/// </para>
/// </summary>
internal sealed class SingleFileDatabase : IDatabase, IDisposable, IAsyncDisposable
{
    private readonly IStorageProvider _storageProvider;
    private readonly string _dbPath;
    private readonly DatabaseOptions _options;
    private readonly Dictionary<string, ITable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly TableDirectoryManager _tableDirectoryManager;
    private readonly Services.QueryCache _queryCache;
    private readonly Dictionary<string, CachedQueryPlan> _preparedPlans = new(StringComparer.Ordinal);
    private readonly Lock _batchUpdateLock = new();
    private bool _isBatchUpdateActive;

    // Shared SqlParser for DML + SELECT in single-file mode.`r`n    // DDL (CREATE / DROP TABLE) is intentionally handled by the regex path in this class because`r`n    // SqlParser.DDL.cs operates on directory-mode Table instances and is not compatible with`r`n    // SingleFileTable.
    private Services.SqlParser? _sqlParser;

    public SingleFileDatabase(IStorageProvider storageProvider, string dbPath, string masterPassword, DatabaseOptions options)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tableDirectoryManager = ((SingleFileStorageProvider)storageProvider).TableDirectoryManager;
        _queryCache = new Services.QueryCache(options.DatabaseConfig?.QueryCacheSize ?? 1024);
        
        LoadTables();
    }

    public Dictionary<string, ITable> Tables => _tables;
    public string DbPath => _dbPath;
    public DatabaseOptions Options => _options;
    public IStorageProvider StorageProvider => _storageProvider;

    private long _lastInsertRowId;
    
    public long GetLastInsertRowId() => _lastInsertRowId;
    internal void SetLastInsertRowId(long rowId) => _lastInsertRowId = rowId;

    /// <summary>
    /// Returns (or lazily creates) the shared <see cref="Services.SqlParser"/> that handles DML and SELECT statements for this single-file database.
    /// </summary>
    private Services.SqlParser GetSqlParser()
    {
        return _sqlParser ??= new Services.SqlParser(
            _tables,
            _dbPath,
            isReadOnly: _options.IsReadOnly,
            queryCache: _queryCache,
            config: _options.DatabaseConfig);
    }

    /// <inheritdoc />
    public bool TryGetTable(string tableName, out ITable table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return _tables.TryGetValue(tableName, out table!);
    }

    /// <inheritdoc />
    public IReadOnlyList<TableInfo> GetTables()
    {
        if (_tables.Count == 0)
            return [];

        List<TableInfo> list = new(_tables.Count);
        foreach (var kvp in _tables)
        {
            list.Add(new TableInfo
            {
                Name = kvp.Key,
                Type = "TABLE"
            });
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<ColumnInfo> GetColumns(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!_tables.TryGetValue(tableName, out var table))
            return [];

        var columns = table.Columns;
        var types = table.ColumnTypes;
        var collations = table.ColumnCollations;
        List<ColumnInfo> list = new(columns.Count);

        for (int i = 0; i < columns.Count; i++)
        {
            var collation = i < collations.Count ? collations[i] : CollationType.Binary;

            list.Add(new ColumnInfo
            {
                Table = tableName,
                Name = columns[i],
                DataType = types[i].ToString(),
                Ordinal = i,
                IsNullable = true,
                Collation = collation == CollationType.Binary ? null : collation.ToString().ToUpperInvariant()
            });
        }

        return list;
    }

    public IDatabase Initialize(string dbPath, string masterPassword) => this;

    public void ExecuteBatchSQL(IEnumerable<string> sqlStatements)
    {
        SingleFileDatabaseBatchExtension.ExecuteBatchSQLOptimized(this, sqlStatements);
    }

    public Task ExecuteBatchSQLAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default)
    {
        SingleFileDatabaseBatchExtension.ExecuteBatchSQLOptimized(this, sqlStatements);
        return Task.CompletedTask;
    }

    public void CreateUser(string username, string password) => throw new NotSupportedException("User management is not supported in single-file mode");
    public bool Login(string username, string password) => false;

    /// <summary>
    /// Prepares a SQL statement for efficient repeated execution in single-file mode.
    /// </summary>
    /// <param name="sql">The SQL statement to prepare.</param>
    /// <returns>A prepared statement instance.</returns>
    public SharpCoreDB.DataStructures.PreparedStatement Prepare(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (!_preparedPlans.TryGetValue(sql, out var plan))
        {
            plan = new CachedQueryPlan(sql, sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            _preparedPlans[sql] = plan;
        }

        CompiledQueryPlan? compiledPlan = null;
        if (sql.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                compiledPlan = QueryCompiler.Compile(sql);
            }
            catch
            {
                compiledPlan = null;
            }
        }

        return new SharpCoreDB.DataStructures.PreparedStatement(sql, plan, compiledPlan);
    }

    /// <summary>
    /// Executes a prepared statement with parameters in single-file mode.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    public void ExecutePrepared(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(stmt);
        ArgumentNullException.ThrowIfNull(parameters);

        ExecuteSQL(BindPreparedSql(stmt.Sql, parameters));
    }

    /// <summary>
    /// Executes a prepared statement asynchronously with parameters in single-file mode.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task ExecutePreparedAsync(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecutePrepared(stmt, parameters);
        return Task.CompletedTask;
    }

    [Obsolete("SingleFileDatabase.ExecuteQuery routes DML/SELECT through SqlParser. DDL (CREATE/DROP TABLE) uses a regex path with full compatibility for standard syntax. For advanced DDL features (STORAGE mode, complex indexes), prefer the directory-mode Database class.")]
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        var upperSql = sql.Trim().ToUpperInvariant();
        
        if (upperSql.Contains("FROM STORAGE") || upperSql.Contains("FROM[STORAGE]"))
        {
            var stats = GetStorageStatistics();
            return
            [
                new Dictionary<string, object>
                {
                    ["TotalSize"] = stats.TotalSize,
                    ["UsedSpace"] = stats.UsedSpace,
                    ["FreeSpace"] = stats.FreeSpace,
                    ["FragmentationPercent"] = stats.FragmentationPercent,
                    ["BlockCount"] = stats.BlockCount
                }
            ];
        }
        else if (upperSql.Contains("SELECT"))
        {
            // ✅ FIX: Bind parameters before executing SELECT to support parameterized queries
            var boundSql = BindPreparedSql(sql, parameters);
            return ExecuteSelectInternal(boundSql, parameters);
        }

        throw new NotSupportedException($"Query not supported in single-file mode: {sql}");
    }

    [Obsolete("SingleFileDatabase.ExecuteQuery routes DML/SELECT through SqlParser. DDL (CREATE/DROP TABLE) uses a regex path. For advanced DDL features prefer the directory-mode Database class.")]
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt) 
        => ExecuteQuery(sql, parameters);

    public bool IsBatchUpdateActive => _isBatchUpdateActive;
    
    public (long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics() 
        => _queryCache.GetStatistics();

    public void ClearQueryCache()
    {
        _queryCache.Clear();
    }

    public void BeginBatchUpdate()
    {
        lock (_batchUpdateLock)
        {
            if (_isBatchUpdateActive)
            {
                throw new InvalidOperationException("Batch update is already active");
            }

            _storageProvider.BeginTransaction();
            _isBatchUpdateActive = true;
        }
    }

    public void EndBatchUpdate()
    {
        lock (_batchUpdateLock)
        {
            if (!_isBatchUpdateActive)
            {
                throw new InvalidOperationException("No active batch update to end");
            }

            try
            {
                _storageProvider.CommitTransactionAsync().GetAwaiter().GetResult();
                _tableDirectoryManager.Flush();
                _isBatchUpdateActive = false;
            }
            catch
            {
                _storageProvider.RollbackTransaction();
                _isBatchUpdateActive = false;
                throw;
            }
        }
    }

    public void CancelBatchUpdate()
    {
        lock (_batchUpdateLock)
        {
            if (!_isBatchUpdateActive)
            {
                throw new InvalidOperationException("No active batch update to cancel");
            }

            _storageProvider.RollbackTransaction();
            _isBatchUpdateActive = false;
        }
    }

    public List<Dictionary<string, object>> ExecuteCompiled(CompiledQueryPlan plan, Dictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if ((parameters is null || parameters.Count == 0) && plan.ParameterNames.Count == 0)
        {
            var executor = new Services.CompiledQueryExecutor(_tables);
            return executor.Execute(plan);
        }

        return ExecuteQuery(BindPreparedSql(plan.Sql, parameters));
    }

    public List<Dictionary<string, object>> ExecuteCompiledQuery(DataStructures.PreparedStatement stmt, Dictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(stmt);

        if (stmt.CompiledPlan is not null && (parameters is null || parameters.Count == 0) && stmt.CompiledPlan.ParameterNames.Count == 0)
        {
            var executor = new Services.CompiledQueryExecutor(_tables);
            return executor.Execute(stmt.CompiledPlan);
        }

        if (stmt.CompiledPlan is not null)
        {
            return ExecuteCompiled(stmt.CompiledPlan, parameters);
        }

        return ExecuteQuery(BindPreparedSql(stmt.Sql, parameters));
    }

    public void Flush()
    {
        // Flush all table row caches to storage before flushing the provider to disk
        foreach (var table in _tables.Values)
        {
            if (table is SingleFileTable sft)
            {
                sft.FlushCache();
            }
        }

        _storageProvider.FlushAsync().GetAwaiter().GetResult();
    }

    public void ForceSave()
    {
        Flush();
    }

    /// <inheritdoc />
    public Dictionary<string, object>? FindByPrimaryKey(string tableName, object key)
    {
        if (!_tables.TryGetValue(tableName, out var table))
            return null;
        return table.FindByPrimaryKey(key);
    }

    /// <inheritdoc />
    public List<Dictionary<string, object>> FindByIndex(string tableName, string column, object value)
    {
        if (!_tables.TryGetValue(tableName, out var table))
            return [];
        return table.FindByIndex(column, value);
    }

    /// <inheritdoc />
    public bool UpdateByPrimaryKey(string tableName, object key, Dictionary<string, object> updates)
    {
        if (!_tables.TryGetValue(tableName, out var table))
            return false;
        return table.UpdateByPrimaryKey(key, updates);
    }

    /// <inheritdoc />
    public bool DeleteByPrimaryKey(string tableName, object key)
    {
        if (!_tables.TryGetValue(tableName, out var table))
            return false;
        return table.DeleteByPrimaryKey(key);
    }

    public Task<VacuumResult> VacuumAsync(VacuumMode mode = VacuumMode.Quick, CancellationToken cancellationToken = default)
        => _storageProvider.VacuumAsync(mode, cancellationToken);

    public StorageMode StorageMode => _storageProvider.Mode;
    public StorageStatistics GetStorageStatistics() => _storageProvider.GetStatistics();

    public void Dispose()
    {
        if (_storageProvider is IAsyncDisposable)
        {
            // Delegate to DisposeAsync to ensure storage provider is properly awaited
            Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
        else if (_storageProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // ✅ FIX: Flush all table caches before disposing storage provider
        // This ensures dirty data is persisted to disk before shutdown
        foreach (var table in _tables.Values)
        {
            if (table is SingleFileTable sft)
            {
                sft.FlushCache();
            }
        }

        if (_storageProvider is IAsyncDisposable asyncProvider)
        {
            await asyncProvider.DisposeAsync().ConfigureAwait(false);
        }
        else if (_storageProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void LoadTables()
    {
        var tableDirManager = ((SingleFileStorageProvider)_storageProvider).TableDirectoryManager;
        
        foreach (var tableName in tableDirManager.GetTableNames())
        {
            var metadata = tableDirManager.GetTableMetadata(tableName);
            if (metadata != null)
            {
                var table = new SingleFileTable(tableName, _storageProvider, metadata.Value);
                _tables[tableName] = table;
            }
        }
    }

    public void ExecuteSQL(string sql)
    {
        ExecuteSQL(sql, null);
    }

    public void ExecuteSQL(string sql, Dictionary<string, object?> parameters)
    {
        var upperSql = sql.Trim().ToUpperInvariant();

        if (upperSql.StartsWith("CREATE TABLE"))
        {
            ExecuteCreateTableInternal(sql);
            return;
        }

        if (upperSql.StartsWith("DROP TABLE"))
        {
            ExecuteDropTableInternal(sql);
            return;
        }

        // SharpCoreDB does not support triggers — silently ignore CREATE/DROP TRIGGER
        if (upperSql.StartsWith("CREATE TRIGGER") || upperSql.StartsWith("DROP TRIGGER"))
        {
            return;
        }

        // CREATE INDEX / DROP INDEX are handled at the storage level
        if (upperSql.StartsWith("CREATE INDEX") || upperSql.StartsWith("DROP INDEX"))
        {
            return;
        }

        ExecuteDMLInternal(sql, parameters);
    }

    public Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default)
    {
        ExecuteSQL(sql);
        return Task.CompletedTask;
    }

    public Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        ExecuteSQL(sql, parameters);
        return Task.CompletedTask;
    }

    private void ExecuteCreateTableInternal(string sql)
    {
        // ✅ Support IF NOT EXISTS and quoted table names (e.g. "__SharpMigrations")
        var ifNotExistsRegex = new Regex(
            @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?[""'`\[]?(\w+)[""'`\]]?\s*\((.*)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var match = ifNotExistsRegex.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid CREATE TABLE syntax: {sql}");
        }

        var tableName = match.Groups[1].Value.Trim();

        // IF NOT EXISTS: skip when table already exists
        if (sql.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase) && _tables.ContainsKey(tableName))
        {
            return;
        }

        var columnDefs = match.Groups[2].Value.Trim();

        var columns = new List<string>();
        var columnTypes = new List<DataType>();
        var isAuto = new List<bool>();
        var isNotNull = new List<bool>();
        var isUnique = new List<bool>();
        var primaryKeyIndex = -1;

        var colIndex = 0;
        // ✅ Use quote-aware splitting to handle DEFAULT values containing commas
        foreach (var colDef in SplitColumnDefinitions(columnDefs))
        {
            var trimmed = colDef.Trim();
            var upper = trimmed.ToUpperInvariant();

            // Skip table-level constraints (FOREIGN KEY, CHECK, PRIMARY KEY as table constraint)
            if (upper.StartsWith("FOREIGN KEY") || upper.StartsWith("CHECK"))
            {
                continue;
            }

            // Handle table-level PRIMARY KEY(col) — extract column name and mark it
            // ✅ Support quoted column names: PRIMARY KEY ("Version")
            if (upper.StartsWith("PRIMARY KEY"))
            {
                var pkMatch = Regex.Match(trimmed, @"PRIMARY\s+KEY\s*\(\s*[""'`\[]?(\w+)[""'`\]]?\s*\)", RegexOptions.IgnoreCase);
                if (pkMatch.Success)
                {
                    var pkColName = pkMatch.Groups[1].Value;
                    var idx = columns.FindIndex(c => c.Equals(pkColName, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        primaryKeyIndex = idx;
                    }
                }
                continue;
            }

            var parts = trimmed.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            // ✅ Strip surrounding quotes from column names (e.g. "Version" → Version)
            var colName = parts[0].Trim('"', '`', '[', ']', '\'');
            var typeStr = parts[1].ToUpperInvariant();

            columns.Add(colName);
            columnTypes.Add(typeStr switch
            {
                "INT" or "INTEGER" => DataType.Integer,
                "BIGINT" or "LONG" => DataType.Long,
                "TEXT" or "VARCHAR" or "CHAR" or "NVARCHAR" => DataType.String,
                "REAL" or "FLOAT" or "DOUBLE" => DataType.Real,
                "DECIMAL" or "NUMERIC" => DataType.Decimal,
                "DATETIME" or "DATE" or "TIMESTAMP" => DataType.DateTime,
                "BLOB" => DataType.Blob,
                "BOOLEAN" or "BOOL" => DataType.Boolean,
                "GUID" or "UUID" => DataType.Guid,
                _ => DataType.String
            });

            // Parse column constraints from the full definition string
            var isPrimary = upper.Contains("PRIMARY") && upper.Contains("KEY");
            var autoInc = upper.Contains("AUTOINCREMENT") || upper.Contains("AUTO_INCREMENT") || upper.Contains("AUTO ");
            var notNull = upper.Contains("NOT NULL") || isPrimary; // PRIMARY KEY implies NOT NULL
            var unique = upper.Contains("UNIQUE") || isPrimary; // PRIMARY KEY implies UNIQUE

            isAuto.Add(autoInc);
            isNotNull.Add(notNull);
            isUnique.Add(unique);

            if (isPrimary)
            {
                primaryKeyIndex = colIndex;
            }

            colIndex++;
        }

        var table = new SingleFileTable(tableName, columns, columnTypes, primaryKeyIndex, isNotNull, isAuto, _storageProvider);
        _tables[tableName] = table;

        // Register table schema with the directory manager so it persists on disk
        var columnEntries = new List<ColumnDefinitionEntry>(columns.Count);
        for (int i = 0; i < columns.Count; i++)
        {
            var flags = ColumnFlags.None;
            if (i == primaryKeyIndex) flags |= ColumnFlags.PrimaryKey;
            if (i < isAuto.Count && isAuto[i]) flags |= ColumnFlags.AutoIncrement;
            if (i < isNotNull.Count && isNotNull[i]) flags |= ColumnFlags.NotNull;
            if (i < isUnique.Count && isUnique[i]) flags |= ColumnFlags.Unique;

            var entry = new ColumnDefinitionEntry
            {
                DataType = (uint)columnTypes[i],
                Flags = (uint)flags,
                DefaultValueLength = 0,
                CheckLength = 0
            };
            SetColumnName(ref entry, columns[i]);
            columnEntries.Add(entry);
        }

        _tableDirectoryManager.CreateTable(table, 0, columnEntries, []);
        _tableDirectoryManager.Flush();
    }

    /// <summary>
    /// Splits a SQL column definition string by commas, respecting quoted strings, brackets, and parentheses.
    /// Handles DEFAULT values containing commas inside string literals correctly.
    /// </summary>
    private static List<string> SplitColumnDefinitions(string definitions)
    {
        var items = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;
        bool inSingleQuotes = false;
        bool inDoubleQuotes = false;
        bool inBrackets = false;
        bool inBackticks = false;

        for (int i = 0; i < definitions.Length; i++)
        {
            char c = definitions[i];

            if (c == '\'' && !inDoubleQuotes && !inBrackets && !inBackticks)
            {
                // Handle escaped single quote ''
                if (inSingleQuotes && i + 1 < definitions.Length && definitions[i + 1] == '\'')
                {
                    current.Append(c);
                    current.Append(definitions[++i]);
                    continue;
                }
                inSingleQuotes = !inSingleQuotes;
                current.Append(c);
                continue;
            }

            if (c == '"' && !inSingleQuotes && !inBrackets && !inBackticks)
            {
                inDoubleQuotes = !inDoubleQuotes;
                current.Append(c);
                continue;
            }

            if (c == '[' && !inSingleQuotes && !inDoubleQuotes && !inBackticks)
            {
                inBrackets = true;
                current.Append(c);
                continue;
            }

            if (c == ']' && inBrackets)
            {
                inBrackets = false;
                current.Append(c);
                continue;
            }

            if (c == '`' && !inSingleQuotes && !inDoubleQuotes && !inBrackets)
            {
                inBackticks = !inBackticks;
                current.Append(c);
                continue;
            }

            if (!inSingleQuotes && !inDoubleQuotes && !inBrackets && !inBackticks)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    var item = current.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(item)) items.Add(item);
                    current.Clear();
                    continue;
                }
            }

            current.Append(c);
        }

        var last = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last)) items.Add(last);
        return items;
    }

    private static unsafe void SetColumnName(ref ColumnDefinitionEntry entry, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var length = Math.Min(nameBytes.Length, ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH);
        fixed (byte* ptr = entry.ColumnName)
        {
            var span = new Span<byte>(ptr, ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH + 1);
            span.Clear();
            nameBytes.AsSpan(0, length).CopyTo(span);
        }
    }

    private void ExecuteDMLInternal(string sql, Dictionary<string, object?>? parameters)
    {
        var parser = GetSqlParser();
        if (parameters is { Count: > 0 })
            parser.Execute(sql, parameters);
        else
            parser.Execute(sql);
    }
    private void ExecuteDropTableInternal(string sql)
    {
        // Support both quoted and unquoted table names: DROP TABLE IF EXISTS "users_tracking"
        var regex = new Regex(
            @"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?[""'`\[\]]?(\w+)[""'`\[\]]?",
            RegexOptions.IgnoreCase);

        var match = regex.Match(sql);
        if (!match.Success)
        {
            // IF EXISTS means we should not throw
            if (sql.Contains("IF EXISTS", StringComparison.OrdinalIgnoreCase))
                return;
            throw new InvalidOperationException($"Invalid DROP TABLE syntax: {sql}");
        }

        var tableName = match.Groups[1].Value.Trim();

        if (_tables.TryGetValue(tableName, out var table))
        {
            if (table is SingleFileTable sft)
            {
                sft.FlushCache(); // Ensure pending data is flushed before removal
            }
            _tables.Remove(tableName);
            _tableDirectoryManager.DeleteTable(tableName);
            _tableDirectoryManager.Flush();
        }
    }

    private void ExecuteDeleteInternal(string sql)
    {
        var regex = new Regex(
            @"DELETE\s+FROM\s+[""'`\[]?(\w+)[""'`\]]?\s+WHERE\s+(.*)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        var match = regex.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid DELETE syntax: {sql}");
        }

        var tableName = match.Groups[1].Value.Trim();
        var whereClause = match.Groups[2].Value.Trim();
        
        if (!_tables.TryGetValue(tableName, out var table))
        {
            throw new InvalidOperationException($"Table '{tableName}' not found");
        }

        table.Delete(whereClause);
    }

    private List<Dictionary<string, object>> ExecuteSelectInternal(string sql, Dictionary<string, object?>? parameters)
    {
        var parser = GetSqlParser();
        return parser.ExecuteQuery(sql, parameters);
    }

    private bool EvaluateWhereClause(Dictionary<string, object> row, string whereClause)
    {
        var condition = whereClause.Trim();

        // ✅ FIX: Support AND/OR logic for complex WHERE clauses
        // Split by AND (case-insensitive)
        var andParts = System.Text.RegularExpressions.Regex.Split(condition, @"\s+AND\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (andParts.Length > 1)
        {
            // All AND conditions must be true
            return andParts.All(part => EvaluateSingleCondition(row, part.Trim()));
        }

        // Split by OR (case-insensitive)
        var orParts = System.Text.RegularExpressions.Regex.Split(condition, @"\s+OR\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (orParts.Length > 1)
        {
            // At least one OR condition must be true
            return orParts.Any(part => EvaluateSingleCondition(row, part.Trim()));
        }

        // Single condition
        return EvaluateSingleCondition(row, condition);
    }

    private bool EvaluateSingleCondition(Dictionary<string, object> row, string condition)
    {
        string[] operators = [">=", "<=", "!=", "<>", "=", ">", "<"];
        string? op = null;
        int opIndex = -1;

        foreach (var testOp in operators)
        {
            opIndex = condition.IndexOf(testOp, StringComparison.Ordinal);
            if (opIndex >= 0)
            {
                op = testOp;
                break;
            }
        }

        if (op == null || opIndex < 0)
        {
            return true;
        }

        var columnName = condition.Substring(0, opIndex).Trim();
        var valueStr = condition.Substring(opIndex + op.Length).Trim();

        if (!row.TryGetValue(columnName, out var rowValue))
        {
            return false;
        }

        if (valueStr.StartsWith('\'') && valueStr.EndsWith('\''))
        {
            valueStr = valueStr[1..^1];
        }

        if (rowValue is int intVal && int.TryParse(valueStr, out var intCompare))
        {
            return op switch
            {
                "=" => intVal == intCompare,
                "!=" or "<>" => intVal != intCompare,
                ">" => intVal > intCompare,
                "<" => intVal < intCompare,
                ">=" => intVal >= intCompare,
                "<=" => intVal <= intCompare,
                _ => true
            };
        }

        if (rowValue is long longVal && long.TryParse(valueStr, out var longCompare))
        {
            return op switch
            {
                "=" => longVal == longCompare,
                "!=" or "<>" => longVal != longCompare,
                ">" => longVal > longCompare,
                "<" => longVal < longCompare,
                ">=" => longVal >= longCompare,
                "<=" => longVal <= longCompare,
                _ => true
            };
        }

        var comparison = string.Compare(rowValue.ToString(), valueStr, StringComparison.Ordinal);
        return op switch
        {
            "=" => comparison == 0,
            "!=" or "<>" => comparison != 0,
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            _ => true
        };
    }

    private static object ParseValue(string valueStr)
    {
        valueStr = valueStr.Trim();
        
        if ((valueStr.StartsWith('\'') && valueStr.EndsWith('\'')) ||
            (valueStr.StartsWith('"') && valueStr.EndsWith('"')))
        {
            return valueStr[1..^1];
        }
        
        if (int.TryParse(valueStr, out var intVal))
            return intVal;
        
        if (decimal.TryParse(valueStr, out var decVal))
            return decVal;
        
        return valueStr;
    }

    private static string BindPreparedSql(string sql, Dictionary<string, object?>? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (parameters is null || parameters.Count == 0)
        {
            return sql;
        }

        var positionalParameterPositions = GetPositionalParameterPositions(sql);
        if (positionalParameterPositions.Length > 0)
        {
            var orderedParameters = new object?[positionalParameterPositions.Length];
            for (var i = 0; i < orderedParameters.Length; i++)
            {
                if (!parameters.TryGetValue(i.ToString(), out var value))
                {
                    throw new ArgumentException($"Missing required positional parameter: {i}", nameof(parameters));
                }

                orderedParameters[i] = value;
            }

            return Services.ParameterBinder.BindPositionalParameters(sql, positionalParameterPositions, orderedParameters);
        }

        var namedParameters = GetNamedParameters(sql);
        return namedParameters.Count == 0
            ? sql
            : Services.ParameterBinder.BindNamedParameters(sql, namedParameters, parameters);
    }

    private static int[] GetPositionalParameterPositions(string sql)
    {
        List<int> positions = [];
        var inString = false;
        var stringChar = '\0';

        for (var i = 0; i < sql.Length; i++)
        {
            var character = sql[i];
            if ((character == '\'' || character == '"') && (i == 0 || sql[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = character;
                }
                else if (character == stringChar)
                {
                    inString = false;
                }
            }

            if (!inString && character == '?')
            {
                positions.Add(i);
            }
        }

        return [.. positions];
    }

    private static Dictionary<string, int> GetNamedParameters(string sql)
    {
        Dictionary<string, int> parameters = [];
        var inString = false;
        var stringChar = '\0';

        for (var i = 0; i < sql.Length; i++)
        {
            var character = sql[i];
            if ((character == '\'' || character == '"') && (i == 0 || sql[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = character;
                }
                else if (character == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            if (!inString && character == '@' && i + 1 < sql.Length && char.IsLetter(sql[i + 1]))
            {
                var nameStart = i + 1;
                var nameEnd = nameStart;
                while (nameEnd < sql.Length && (char.IsLetterOrDigit(sql[nameEnd]) || sql[nameEnd] == '_'))
                {
                    nameEnd++;
                }

                var parameterName = sql[nameStart..nameEnd];
                parameters.TryAdd(parameterName, i);
                i = nameEnd - 1;
            }
        }

        return parameters;
    }
}
