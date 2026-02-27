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
/// <b>⚠️ LEGACY:</b> This class uses regex-based SQL parsing with limited support.
/// It does NOT support ORDER BY, LIMIT, JOIN, subqueries, or aggregate functions.
/// For full SQL support, use the <see cref="Database"/> class (directory mode) which
/// routes through <c>SqlParser.ExecuteSelectQuery</c>.
/// </para>
/// </summary>
internal sealed class SingleFileDatabase : IDatabase, IDisposable
{
    private readonly IStorageProvider _storageProvider;
    private readonly string _dbPath;
    private readonly DatabaseOptions _options;
    private readonly Dictionary<string, ITable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly TableDirectoryManager _tableDirectoryManager;
    private readonly Services.QueryCache _queryCache;
    private readonly Lock _batchUpdateLock = new();
    private bool _isBatchUpdateActive;

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

    public SharpCoreDB.DataStructures.PreparedStatement Prepare(string sql) => throw new NotImplementedException();
    public void ExecutePrepared(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters) => throw new NotImplementedException();
    public Task ExecutePreparedAsync(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    [Obsolete("SingleFileDatabase uses regex-based SQL parsing with limited support (no ORDER BY, LIMIT, JOIN, subqueries). For full SQL support, use the Database class which routes through SqlParser.")]
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
            return ExecuteSelectInternal(sql, parameters);
        }
        
        throw new NotSupportedException($"Query not supported in single-file mode: {sql}");
    }

    [Obsolete("SingleFileDatabase uses regex-based SQL parsing with limited support. For full SQL support, use the Database class which routes through SqlParser.")]
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

    public List<Dictionary<string, object>> ExecuteCompiled(CompiledQueryPlan plan, Dictionary<string, object?>? parameters = null) => throw new NotImplementedException();
    public List<Dictionary<string, object>> ExecuteCompiledQuery(DataStructures.PreparedStatement stmt, Dictionary<string, object?>? parameters = null) => throw new NotImplementedException();

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

    public Task<VacuumResult> VacuumAsync(VacuumMode mode = VacuumMode.Quick, CancellationToken cancellationToken = default) 
        => _storageProvider.VacuumAsync(mode, cancellationToken);

    public StorageMode StorageMode => _storageProvider.Mode;
    public StorageStatistics GetStorageStatistics() => _storageProvider.GetStatistics();

    public void Dispose()
    {
        if (_storageProvider is IDisposable disposable)
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
        // ✅ Support IF NOT EXISTS
        var ifNotExistsRegex = new Regex(
            @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(\w+)\s*\((.*)\)",
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
        foreach (var colDef in columnDefs.Split(','))
        {
            var trimmed = colDef.Trim();
            var upper = trimmed.ToUpperInvariant();

            // Skip table-level constraints (FOREIGN KEY, CHECK, PRIMARY KEY as table constraint)
            if (upper.StartsWith("FOREIGN KEY") || upper.StartsWith("CHECK"))
            {
                continue;
            }

            // Handle table-level PRIMARY KEY(col) — extract column name and mark it
            if (upper.StartsWith("PRIMARY KEY"))
            {
                var pkMatch = Regex.Match(trimmed, @"PRIMARY\s+KEY\s*\(\s*(\w+)\s*\)", RegexOptions.IgnoreCase);
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

            var colName = parts[0];
            var typeStr = parts[1].ToUpperInvariant();

            columns.Add(colName);
            columnTypes.Add(typeStr switch
            {
                "INT" or "INTEGER" or "BIGINT" => DataType.Integer,
                "TEXT" or "VARCHAR" or "CHAR" or "NVARCHAR" => DataType.String,
                "REAL" or "FLOAT" or "DOUBLE" => DataType.Real,
                "DECIMAL" or "NUMERIC" => DataType.Decimal,
                "DATETIME" or "DATE" or "TIMESTAMP" => DataType.DateTime,
                "BLOB" => DataType.Blob,
                "BOOLEAN" or "BOOL" => DataType.Boolean,
                "LONG" => DataType.Long,
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

    [Obsolete("Regex-based DML parsing. Migrate to SqlParser-based execution for full SQL support.")]
    private void ExecuteDMLInternal(string sql, Dictionary<string, object?>? parameters)
    {
        var upperSql = sql.Trim().ToUpperInvariant();
        
        if (upperSql.StartsWith("INSERT"))
        {
            ExecuteInsertInternal(sql);
        }
        else if (upperSql.StartsWith("UPDATE"))
        {
            ExecuteUpdateInternal(sql);
        }
        else if (upperSql.StartsWith("DELETE"))
        {
            ExecuteDeleteInternal(sql);
        }
        else if (upperSql.StartsWith("SELECT"))
        {
            ExecuteQuery(sql, parameters ?? new Dictionary<string, object?>());
        }
    }

    [Obsolete("Regex-based INSERT parsing. Migrate to SqlParser-based execution for full SQL support.")]
    private void ExecuteInsertInternal(string sql)
    {
        var regex = new Regex(
            @"INSERT\s+INTO\s+(\w+)\s*(?:\((.*?)\))?\s*VALUES\s*\((.*?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        var match = regex.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid INSERT syntax: {sql}");
        }

        var tableName = match.Groups[1].Value.Trim();
        var columnNames = string.IsNullOrWhiteSpace(match.Groups[2].Value) 
            ? null
            : match.Groups[2].Value.Split(',').Select(c => c.Trim()).ToList();
        var values = match.Groups[3].Value.Split(',').Select(v => v.Trim()).ToList();
        
        if (!_tables.TryGetValue(tableName, out var table))
        {
            throw new InvalidOperationException($"Table '{tableName}' not found");
        }

        var row = new Dictionary<string, object>();
        
        if (columnNames == null)
        {
            // No column list provided: use table columns in order
            for (int i = 0; i < table.Columns.Count && i < values.Count; i++)
            {
                var value = ParseValue(values[i]);
                row[table.Columns[i]] = value;
            }
        }
        else
        {
            // Column list provided
            for (int i = 0; i < columnNames.Count && i < values.Count; i++)
            {
                var value = ParseValue(values[i]);
                row[columnNames[i]] = value;
            }
        }
        
        table.Insert(row);
    }

    [Obsolete("Regex-based UPDATE parsing. Migrate to SqlParser-based execution for full SQL support.")]
    private void ExecuteUpdateInternal(string sql)
    {
        var regex = new Regex(
            @"UPDATE\s+(\w+)\s+SET\s+(.*?)\s+WHERE\s+(.*)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        var match = regex.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid UPDATE syntax: {sql}");
        }

        var tableName = match.Groups[1].Value.Trim();
        var setClause = match.Groups[2].Value.Trim();
        var whereClause = match.Groups[3].Value.Trim();
        
        if (!_tables.TryGetValue(tableName, out var table))
        {
            throw new InvalidOperationException($"Table '{tableName}' not found");
        }

        var updates = new Dictionary<string, object>();
        foreach (var assignment in setClause.Split(','))
        {
            var parts = assignment.Split('=');
            if (parts.Length == 2)
            {
                updates[parts[0].Trim()] = ParseValue(parts[1].Trim());
            }
        }
        
        table.Update($"WHERE {whereClause}", updates);
    }

    [Obsolete("Regex-based DROP TABLE parsing. Migrate to SqlParser-based execution for full SQL support.")]
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

    [Obsolete("Regex-based DELETE parsing. Migrate to SqlParser-based execution for full SQL support.")]
    private void ExecuteDeleteInternal(string sql)
    {
        var regex = new Regex(
            @"DELETE\s+FROM\s+(\w+)\s+WHERE\s+(.*)",
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

        table.Delete($"WHERE {whereClause}");
    }

    [Obsolete("Regex-based SELECT with limited SQL support (no ORDER BY, LIMIT, JOIN, subqueries). Migrate to SqlParser-based execution.")]
    private List<Dictionary<string, object>> ExecuteSelectInternal(string sql, Dictionary<string, object?>? parameters)
    {
        var regex = new Regex(
            @"SELECT\s+(.*?)\s+FROM\s+(\w+)\s*(?:WHERE\s+(.*))?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        var match = regex.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid SELECT syntax: {sql}");
        }

        var columns = match.Groups[1].Value.Trim();
        var tableName = match.Groups[2].Value.Trim();
        var whereClause = match.Groups[3].Value.Trim();
        
        if (!_tables.TryGetValue(tableName, out var table))
        {
            throw new InvalidOperationException($"Table '{tableName}' not found");
        }

        var rows = table.Select();
        
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            rows = rows.Where(row => EvaluateWhereClause(row, whereClause)).ToList();
        }
        
        return rows;
    }

    private bool EvaluateWhereClause(Dictionary<string, object> row, string whereClause)
    {
        var condition = whereClause.Trim();
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
}
