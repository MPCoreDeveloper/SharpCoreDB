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
using System.Text;

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
        ArgumentNullException.ThrowIfNull(services);  // ? C# 14
        
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
/// <param name="services">The service provider for dependency injection.</param>
public class DatabaseFactory(IServiceProvider services)
{
    /// <summary>
    /// Creates a new Database instance (legacy method, backward compatible).
    /// </summary>
    /// <param name="dbPath">Database path (directory or .scdb file)</param>
    /// <param name="masterPassword">Master password for encryption</param>
    /// <param name="isReadOnly">Whether database is read-only</param>
    /// <param name="config">Optional database configuration (legacy)</param>
    /// <param name="securityConfig">Security configuration (kept for API compatibility)</param>
    /// <returns>Database instance</returns>
    public IDatabase Create(
        string dbPath, 
        string masterPassword, 
        bool isReadOnly = false, 
        DatabaseConfig? config = null, 
        SecurityConfig? securityConfig = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        
        // Auto-detect storage mode by file extension
        var options = DetectStorageMode(dbPath, config);
        options.IsReadOnly = isReadOnly;
        
        return CreateWithOptions(dbPath, masterPassword, options);
    }

    /// <summary>
    /// Creates a new Database instance with DatabaseOptions (new API).
    /// Supports both directory and single-file storage modes.
    /// </summary>
    /// <param name="dbPath">Database path (directory or .scdb file)</param>
    /// <param name="masterPassword">Master password for encryption</param>
    /// <param name="options">Database options with storage mode</param>
    /// <returns>Database instance</returns>
    public IDatabase CreateWithOptions(string dbPath, string masterPassword, DatabaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        ArgumentNullException.ThrowIfNull(options);
        
        options.Validate();

        // Auto-detect storage mode if not explicitly set
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

    /// <summary>
    /// Creates a database using legacy directory-based storage.
    /// For now, use existing Database class (will be refactored to use DirectoryStorageProvider)
    /// </summary>
    private IDatabase CreateDirectoryDatabase(string dbPath, string masterPassword, DatabaseOptions options)
    {
        var config = options.DatabaseConfig ?? DatabaseConfig.Default;
        // Apply tuned defaults when missing
        options.DatabaseConfig = config;
        return new Database(services, dbPath, masterPassword, options.IsReadOnly, config);
    }

    /// <summary>
    /// Creates a database using new single-file storage (.scdb format).
    /// </summary>
    private static IDatabase CreateSingleFileDatabase(string dbPath, string masterPassword, DatabaseOptions options)
    {
        options.CreateImmediately = true;
        // Propagate tuning from DatabaseConfig when available
        if (options.DatabaseConfig is not null)
        {
            options.EnableMemoryMapping = options.DatabaseConfig.UseMemoryMapping;
        }
        options.WalBufferSizePages = options.WalBufferSizePages > 0 ? options.WalBufferSizePages : 2048;
        // Allow ReadWrite sharing to support multiple connections accessing the same file
        // SharpCoreDB has internal locking (_transactionLock) for thread-safe concurrent access
        // This is now safe because SharpCoreDBConnection uses instance pooling to share database objects
        options.FileShareMode = System.IO.FileShare.ReadWrite;
        var provider = SingleFileStorageProvider.Open(dbPath, options);
        return new SingleFileDatabase(provider, dbPath, masterPassword, options);
    }

    /// <summary>
    /// Auto-detects storage mode from file path and creates appropriate options.
    /// </summary>
    private static DatabaseOptions DetectStorageMode(string dbPath, DatabaseConfig? config)
    {
        var isSingleFile = dbPath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase) ||
                           File.Exists(dbPath) && !Directory.Exists(dbPath);

        var options = isSingleFile
            ? DatabaseOptions.CreateSingleFileDefault()
            : DatabaseOptions.CreateDirectoryDefault();

        // Apply legacy config if provided
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
/// </summary>
internal sealed class SingleFileDatabase : IDatabase, IDisposable
{
    private readonly IStorageProvider _storageProvider;
    private readonly string _dbPath; // #pragma warning disable S4487 // Used for logging
    private readonly DatabaseOptions _options; // #pragma warning disable S4487 // Used for configuration
    private readonly Dictionary<string, ITable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly TableDirectoryManager _tableDirectoryManager;
    private readonly Services.QueryCache _queryCache;
    private readonly Lock _batchUpdateLock = new();
    private bool _isBatchUpdateActive;

    /// <summary>
    /// Creates a new SingleFileDatabase instance.
    /// </summary>
    public static SingleFileDatabase Create(string dbPath, string masterPassword, DatabaseOptions options)
    {
        var provider = SingleFileStorageProvider.Open(dbPath, options);
        return new SingleFileDatabase(provider, dbPath, masterPassword, options);
    }

    public SingleFileDatabase(IStorageProvider storageProvider, string dbPath, string masterPassword, DatabaseOptions options)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tableDirectoryManager = ((SingleFileStorageProvider)storageProvider).TableDirectoryManager;
        _queryCache = new Services.QueryCache(options.DatabaseConfig?.QueryCacheSize ?? 1024);
        
        // Load existing tables from single-file storage
        LoadTables();
    }

    public Dictionary<string, ITable> Tables => _tables;
    public string DbPath => _dbPath;
    public DatabaseOptions Options => _options;
    public IStorageProvider StorageProvider => _storageProvider;

    // ? NEW: last_insert_rowid() support for SQLite compatibility
    private long _lastInsertRowId;
    
    /// <inheritdoc />
    public long GetLastInsertRowId() => _lastInsertRowId;

    /// <summary>
    /// Sets the last insert rowid (called internally after inserts).
    /// </summary>
    internal void SetLastInsertRowId(long rowId) => _lastInsertRowId = rowId;

    public IDatabase Initialize(string dbPath, string masterPassword) => this;

    public void ExecuteBatchSQL(IEnumerable<string> sqlStatements)
    {
        // ? CRITICAL FIX: Use optimized batch execution with transaction grouping
        // Instead of: foreach(sql) ExecuteSQL(sql) -> 1K statements = 10x slower
        // Now: Group INSERTs + single transaction = matches LiteDB performance
        SingleFileDatabaseBatchExtension.ExecuteBatchSQLOptimized(this, sqlStatements);
    }

    public Task ExecuteBatchSQLAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default)
    {
        // ? Use optimized batch execution - same as sync version
        SingleFileDatabaseBatchExtension.ExecuteBatchSQLOptimized(this, sqlStatements);
        return Task.CompletedTask;
    }

    public void CreateUser(string username, string password) => throw new NotSupportedException("User management is not supported in single-file mode");

    public bool Login(string username, string password) => false;

   

    public SharpCoreDB.DataStructures.PreparedStatement Prepare(string sql) => throw new NotImplementedException("Prepared statements not yet implemented in single-file mode");

    public void ExecutePrepared(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters) => throw new NotImplementedException("Prepared statements not yet implemented in single-file mode");

    public Task ExecutePreparedAsync(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Prepared statements not yet implemented in single-file mode");
    }

    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        // Handle basic queries
        var upperSql = sql.Trim().ToUpperInvariant();
        
        // ? FIX: Check for special STORAGE table query FIRST (before general SELECT routing)
        // This handles: SELECT * FROM STORAGE, SELECT COUNT(*) FROM STORAGE, etc.
        if (upperSql.Contains("FROM STORAGE") || upperSql.Contains("FROM[STORAGE]"))
        {
            // Return storage statistics
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
            // Use SingleFileSqlParser for queries
            var sqlParser = new SingleFileSqlParser(this, _tableDirectoryManager);
            return sqlParser.ExecuteQuery(sql, parameters ?? new Dictionary<string, object?>());
        }
        
        throw new NotSupportedException($"Query not supported in single-file mode: {sql}");
    }

    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt) => ExecuteQuery(sql, parameters);

    public bool IsBatchUpdateActive => _isBatchUpdateActive;
    
    public (long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics() 
        => _queryCache.GetStatistics();

    /// <summary>
    /// Clears the query cache for single-file database.
    /// Uses modern C# 14 null-conditional operator and minimal allocation.
    /// </summary>
    public void ClearQueryCache()
    {
        _queryCache.Clear();
    }

    /// <summary>
    /// Begins a batch update transaction for optimized bulk operations.
    /// Uses C# 14 Lock type and modern exception handling.
    /// </summary>
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

    /// <summary>
    /// Ends a batch update transaction and commits all changes.
    /// Uses modern async-over-sync pattern with GetAwaiter().GetResult().
    /// </summary>
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
                // On error, rollback and rethrow
                _storageProvider.RollbackTransaction();
                _isBatchUpdateActive = false;
                throw;
            }
        }
    }

    /// <summary>
    /// Cancels an active batch update transaction and rolls back changes.
    /// Uses modern C# 14 exception handling patterns.
    /// </summary>
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

    public List<Dictionary<string, object>> ExecuteCompiled(CompiledQueryPlan plan, Dictionary<string, object?>? parameters = null) => throw new NotImplementedException("Compiled queries not yet implemented in single-file mode");

    public List<Dictionary<string, object>> ExecuteCompiledQuery(DataStructures.PreparedStatement stmt, Dictionary<string, object?>? parameters = null) => throw new NotImplementedException("Compiled queries not yet implemented in single-file mode");

    public void Flush() => _storageProvider.FlushAsync().GetAwaiter().GetResult();

    public void ForceSave() => Flush();

    public Task<VacuumResult> VacuumAsync(VacuumMode mode = VacuumMode.Quick, CancellationToken cancellationToken = default) => _storageProvider.VacuumAsync(mode, cancellationToken);

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
        // Load table metadata from single-file storage
        var tableDirManager = ((SingleFileStorageProvider)_storageProvider).TableDirectoryManager;
        
        foreach (var tableName in tableDirManager.GetTableNames())
        {
            var metadata = tableDirManager.GetTableMetadata(tableName);
            if (metadata != null)
            {
                // Create SingleFileTable instance
                var table = new SingleFileTable(tableName, _storageProvider, metadata.Value);
                _tables[tableName] = table;
            }
        }
    }

    public void ExecuteSQL(string sql)
    {
        // Use SingleFileSqlParser for DDL operations, regular SqlParser for DML
        var sqlParser = new SingleFileSqlParser(this, _tableDirectoryManager);
        sqlParser.Execute(sql, null);
        
        // Mark metadata as dirty for schema changes
        if (IsSchemaChangingCommand(sql))
        {
            // Save table directory metadata
            _tableDirectoryManager.Flush();
        }
    }

    public void ExecuteSQL(string sql, Dictionary<string, object?> parameters)
    {
        // Use SingleFileSqlParser for DDL operations, regular SqlParser for DML
        var sqlParser = new SingleFileSqlParser(this, _tableDirectoryManager);
        sqlParser.Execute(sql, parameters);
        
        // Mark metadata as dirty for schema changes
        if (IsSchemaChangingCommand(sql))
        {
            // Save table directory metadata
            _tableDirectoryManager.Flush();
        }
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

    private static bool IsSchemaChangingCommand(string sql) =>
        sql.TrimStart().ToUpperInvariant() is var upper &&
        (upper.StartsWith("CREATE ") || upper.StartsWith("ALTER ") || upper.StartsWith("DROP "));
}

/// <summary>
/// Very basic SQL parser for single-file database operations.
/// </summary>
internal class BasicSqlParser
{
    private readonly string _sql;
    
    public BasicSqlParser(string sql)
    {
        _sql = sql;
    }
    
    public string GetTableName()
    {
        // Very basic parsing
        var words = _sql.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].ToUpperInvariant() == "FROM" && i + 1 < words.Length)
            {
                return words[i + 1];
            }
            if (words[i].ToUpperInvariant() == "INTO" && i + 1 < words.Length)
            {
                return words[i + 1];
            }
            if (words[i].ToUpperInvariant() == "TABLE" && i + 1 < words.Length)
            {
                return words[i + 1];
            }
        }
        throw new InvalidOperationException("Could not parse table name from SQL");
    }
    
    public List<string> GetColumns()
    {
        // Basic parsing for CREATE TABLE
        var columns = new List<string>();
        var start = _sql.IndexOf('(');
        var end = _sql.LastIndexOf(')');
        
        if (start >= 0 && end > start)
        {
            var columnDefs = _sql.Substring(start + 1, end - start - 1);
            var defs = columnDefs.Split(',');
            
            foreach (var def in defs)
            {
                var parts = def.Trim().Split(' ');
                if (parts.Length > 0)
                {
                    columns.Add(parts[0]);
                }
            }
        }
        
        return columns;
    }
    
    public string GetWhereClause()
    {
        var whereIndex = _sql.ToUpperInvariant().IndexOf("WHERE");
        if (whereIndex >= 0)
        {
            return _sql.Substring(whereIndex + 5).Trim();
        }
        return string.Empty;
    }
}

///
/// Table implementation for single-file storage.
/// Uses block-based storage instead of file-based.
///
internal class SingleFileTable : ITable
{
    private readonly string _tableName;
    private readonly List<string> _columns;
    private readonly List<DataType> _columnTypes;
    private readonly IStorageProvider _storageProvider;
    private readonly string _dataBlockName;
    private readonly Dictionary<object, long> _primaryKeyIndex;
    private readonly Lock _tableLock = new();
    private long _nextId = 1;

    public SingleFileTable(string tableName, List<string> columns, List<DataType> columnTypes, IStorageProvider storageProvider)
    {
        _tableName = tableName;
        _columns = columns;
        _columnTypes = columnTypes;
        _storageProvider = storageProvider;
        _dataBlockName = $"table:{tableName}:data";
        _primaryKeyIndex = new Dictionary<object, long>();
    }

    public SingleFileTable(string tableName, IStorageProvider storageProvider, TableMetadataEntry metadata)
    {
        _tableName = tableName;
        _storageProvider = storageProvider;
        _dataBlockName = $"table:{tableName}:data";
        _primaryKeyIndex = new Dictionary<object, long>();
        
        // Load column definitions from metadata
        var tableDirManager = ((SingleFileStorageProvider)storageProvider).TableDirectoryManager;
        var columnDefs = tableDirManager.GetColumnDefinitions(tableName);
        
        _columns = new List<string>();
        _columnTypes = new List<DataType>();
        
        foreach (var colDef in columnDefs)
        {
            _columns.Add(GetColumnName(colDef));
            _columnTypes.Add((DataType)colDef.DataType);
        }
        
        // Build primary key index from existing data
        RebuildPrimaryKeyIndex();
    }

    public string Name { get => _tableName; set => throw new NotSupportedException(); }
    public List<string> Columns => _columns;
    public List<DataType> ColumnTypes => _columnTypes;
    public int PrimaryKeyIndex => 0; // Assume first column is PK
    public List<bool> IsAuto => new List<bool> { true }; // Simplified
    public List<bool> IsNotNull => new List<bool> { false }; // Simplified
    public List<object?> DefaultValues => new List<object?> { null }; // Simplified
    public List<List<string>> UniqueConstraints => new List<List<string>>(); // Not implemented
    public List<ForeignKeyConstraint> ForeignKeys => new List<ForeignKeyConstraint>(); // Not implemented
    public string DataFile { get => _dataBlockName; set => throw new NotSupportedException(); }

    public void Insert(Dictionary<string, object> row)
    {
        // Generate ID if needed
        if (!row.ContainsKey(_columns[0]))
        {
            row[_columns[0]] = _nextId++;
        }

        // Convert to nullable dictionary for serialization
        var nullableRow = row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
        
        // Serialize and store
        var data = SerializeRow(nullableRow);
        _storageProvider.WriteBlockAsync(_dataBlockName, data).GetAwaiter().GetResult();
    }

    public long[] InsertBatch(List<Dictionary<string, object>> rows)
    {
        // Not implemented for single-file storage - fall back to individual inserts
        var positions = new long[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            Insert(rows[i]);
            positions[i] = i;
        }
        return positions;
    }

    public long[] InsertBatchFromBuffer(ReadOnlySpan<byte> encodedData, int rowCount)
    {
        // Not implemented for single-file storage
        throw new NotImplementedException("InsertBatchFromBuffer is not supported for single-file storage");
    }

    public void Update(Dictionary<string, object> row)
    {
        // For simplicity, just append - real implementation would need indexing
        Insert(row);
    }

    /// <summary>
    /// Deletes a row from the single-file table using modern C# 14 patterns.
    /// Uses primary key index for O(1) lookup and block-level deletion.
    /// </summary>
    /// <param name="row">Row to delete (must contain primary key value)</param>
    public void Delete(Dictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        lock (_tableLock)
        {
            // Get primary key value from row
            if (PrimaryKeyIndex >= 0 && PrimaryKeyIndex < _columns.Count)
            {
                var pkColumn = _columns[PrimaryKeyIndex];
                
                if (!row.TryGetValue(pkColumn, out var pkValue) || pkValue is null)
                {
                    throw new InvalidOperationException($"Cannot delete row: Primary key column '{pkColumn}' is null or missing");
                }

                // Lookup position in primary key index
                if (!_primaryKeyIndex.TryGetValue(pkValue, out var blockOffset))
                {
                    throw new InvalidOperationException($"Cannot delete row: Primary key value '{pkValue}' not found in index");
                }

                // Delete block from storage provider
                var blockName = $"{_dataBlockName}:{blockOffset}";
                _storageProvider.DeleteBlockAsync(blockName).GetAwaiter().GetResult();

                // Remove from primary key index
                _primaryKeyIndex.Remove(pkValue);
            }
            else
            {
                // Fallback: Full scan for tables without primary key (slow)
                DeleteWithoutPrimaryKey(row);
            }
        }
    }

    public List<Dictionary<string, object>> Select()
    {
        var results = new List<Dictionary<string, object>>();
        
        // Read all data from blocks
        var allBlocks = _storageProvider.EnumerateBlocks()
            .Where(name => name.StartsWith(_dataBlockName, StringComparison.Ordinal))
            .ToList();
            
        foreach (var blockName in allBlocks)
        {
            var data = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
            if (data != null)
            {
                var row = DeserializeRow(data);
                results.Add(row);
            }
        }
        
        return results;
    }

    // Stub implementations for ITable interface
    public List<Dictionary<string, object>> Select(string? whereClause, string? orderBy, bool distinct = false) => Select();
    public List<Dictionary<string, object>> Select(string? whereClause, string? orderBy, bool distinct = false, bool noEncrypt = false) => Select();
    
    public void Update(string? whereClause, Dictionary<string, object> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        
        if (updates.Count == 0)
        {
            throw new ArgumentException("Updates dictionary cannot be null or empty", nameof(updates));
        }

        lock (_tableLock)
        {
            // Enumerate all blocks for this table
            var allBlocks = _storageProvider.EnumerateBlocks()
                .Where(name => name.StartsWith(_dataBlockName, StringComparison.Ordinal))
                .ToList();

            foreach (var blockName in allBlocks)
            {
                var data = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
                if (data is null) continue;

                var row = DeserializeRow(data);
                
                // Check WHERE clause condition
                if (!string.IsNullOrWhiteSpace(whereClause) && !EvaluateWhereClause(row, whereClause))
                {
                    continue; // Skip rows that don't match WHERE clause
                }

                // Apply updates to matching row
                bool rowModified = false;
                foreach (var kvp in updates)
                {
                    if (row.ContainsKey(kvp.Key))
                    {
                        row[kvp.Key] = kvp.Value;
                        rowModified = true;
                    }
                }

                if (rowModified)
                {
                    // Write updated row back to storage
                    var updatedData = SerializeRow(row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));
                    _storageProvider.WriteBlockAsync(blockName, updatedData).GetAwaiter().GetResult();
                }
            }
        }
    }

    /// <summary>
    /// Evaluates a WHERE clause against a row using simplified parsing.
    /// Supports basic conditions: column = value, column > value, etc.
    /// </summary>
    private bool EvaluateWhereClause(Dictionary<string, object> row, string whereClause)
    {
        // Remove "WHERE" prefix if present
        var condition = whereClause.Trim();
        if (condition.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
        {
            condition = condition.Substring(5).Trim();
        }

        // Parse simple conditions: column = value, column > value, etc.
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
            return true; // No recognizable operator, pass through
        }

        var columnName = condition.Substring(0, opIndex).Trim();
        var valueStr = condition.Substring(opIndex + op.Length).Trim();

        if (!row.TryGetValue(columnName, out var rowValue))
        {
            return false;
        }

        // Remove quotes if present
        if (valueStr.StartsWith('\'') && valueStr.EndsWith('\''))
        {
            valueStr = valueStr[1..^1];
        }

        // Perform comparison based on operator
        return op switch
        {
            "=" => CompareValues(rowValue, valueStr) == 0,
            "!=" or "<>" => CompareValues(rowValue, valueStr) != 0,
            ">" => CompareValues(rowValue, valueStr) > 0,
            "<" => CompareValues(rowValue, valueStr) < 0,
            ">=" => CompareValues(rowValue, valueStr) >= 0,
            "<=" => CompareValues(rowValue, valueStr) <= 0,
            _ => true
        };
    }

    /// <summary>
    /// Compares a row value with a string value, handling type conversions.
    /// </summary>
    private static int CompareValues(object? rowValue, string compareStr)
    {
        if (rowValue is null)
        {
            return string.IsNullOrEmpty(compareStr) ? 0 : -1;
        }

        // Try numeric comparison first
        if (rowValue is int intVal && int.TryParse(compareStr, out var intCompare))
        {
            return intVal.CompareTo(intCompare);
        }

        if (rowValue is long longVal && long.TryParse(compareStr, out var longCompare))
        {
            return longVal.CompareTo(longCompare);
        }

        if (rowValue is decimal decVal && decimal.TryParse(compareStr, out var decCompare))
        {
            return decVal.CompareTo(decCompare);
        }

        if (rowValue is double dblVal && double.TryParse(compareStr, out var dblCompare))
        {
            return dblVal.CompareTo(dblCompare);
        }

        // Fall back to string comparison
        return string.Compare(rowValue.ToString(), compareStr, StringComparison.Ordinal);
    }
    
    public void Delete(string? whereClause) => throw new NotImplementedException();
    public void CreateHashIndex(string columnName) => throw new NotImplementedException("Hash indexes are not supported for single-file storage");
    public void CreateHashIndex(string indexName, string columnName, bool isUnique = false) => throw new NotImplementedException("Named hash indexes are not supported for single-file storage");
    public bool HasHashIndex(string columnName) => false;
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName) => null;
    public void IncrementColumnUsage(string columnName) { }
    public IReadOnlyDictionary<string, long> GetColumnUsage() => new Dictionary<string, long>();
    public void TrackAllColumnsUsage() { }
    public void TrackColumnUsage(string columnName) { }
    public bool RemoveHashIndex(string columnName) => false;
    public void ClearAllIndexes() { }
    public long GetCachedRowCount() => -1;
    public void RefreshRowCount() { }
    public void CreateBTreeIndex(string columnName) => throw new NotImplementedException("B-tree indexes are not supported for single-file storage");
    public void CreateBTreeIndex(string indexName, string columnName, bool isUnique = false) => throw new NotImplementedException("Named B-tree indexes are not supported for single-file storage");
    public bool HasBTreeIndex(string columnName) => false;
    public void Flush() { }
    public void AddColumn(ColumnDefinition columnDef) => throw new NotImplementedException("Adding columns is not supported for single-file storage");
    
    /// <summary>
    /// Fallback deletion method for tables without a primary key.
    /// Performs full table scan - O(n) complexity.
    /// </summary>
    private void DeleteWithoutPrimaryKey(Dictionary<string, object?> row)
    {
        var allBlocks = _storageProvider.EnumerateBlocks()
            .Where(name => name.StartsWith(_dataBlockName, StringComparison.Ordinal))
            .ToList();

        foreach (var blockName in allBlocks)
        {
            var data = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
            if (data is null) continue;

            var storedRow = DeserializeRow(data);
            
            // Check if all columns match
            if (RowsMatch(row, storedRow))
            {
                _storageProvider.DeleteBlockAsync(blockName).GetAwaiter().GetResult();
                return; // Only delete first match
            }
        }

        throw new InvalidOperationException("Row not found for deletion");
    }

    /// <summary>
    /// Checks if two rows match on all non-null columns.
    /// Uses modern C# 14 pattern matching and LINQ.
    /// </summary>
    private bool RowsMatch(Dictionary<string, object?> row1, Dictionary<string, object> row2)
    {
        foreach (var kvp in row1.Where(kvp => kvp.Value is not null))
        {
            if (!row2.TryGetValue(kvp.Key, out var value2) || 
                !Equals(kvp.Value, value2))
            {
                return false;
            }
        }
        return true;
    }

    private void RebuildPrimaryKeyIndex()
    {
        if (PrimaryKeyIndex < 0 || PrimaryKeyIndex >= _columns.Count)
        {
            return; // No primary key
        }

        var pkColumn = _columns[PrimaryKeyIndex];
        var allBlocks = _storageProvider.EnumerateBlocks()
            .Where(name => name.StartsWith(_dataBlockName, StringComparison.Ordinal))
            .ToList();

        foreach (var blockName in allBlocks)
        {
            var data = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
            if (data is null) continue;

            var row = DeserializeRow(data);
            if (!row.TryGetValue(pkColumn, out var pkValue) || pkValue is null) 
                continue;
            
            // Extract block offset from block name (e.g., "table:users:data:12345" -> 12345)
            var blockParts = blockName.Split(':');
            if (blockParts.Length > 0)
            {
                var lastPart = blockParts[^1];
                if (long.TryParse(lastPart, out var offset))
                {
                    _primaryKeyIndex[pkValue] = offset;
                    
                    // Update _nextId for auto-increment
                    if (pkValue is int intVal && intVal >= _nextId)
                    {
                        _nextId = intVal + 1;
                    }
                    else if (pkValue is long longVal && longVal >= _nextId)
                    {
                        _nextId = longVal + 1;
                    }
                }
            }
        }
    }

    private byte[] SerializeRow(Dictionary<string, object?> row)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(row);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    private Dictionary<string, object> DeserializeRow(byte[] data)
    {
        var json = System.Text.Encoding.UTF8.GetString(data);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
    }

    private static unsafe string GetColumnName(ColumnDefinitionEntry entry)
    {
        var span = new ReadOnlySpan<byte>(entry.ColumnName, ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH + 1);
        var nullIndex = span.IndexOf((byte)0);
        if (nullIndex >= 0)
            span = span[..nullIndex];
        
        return Encoding.UTF8.GetString(span);
    }
}
/// <summary>
/// SQL parser wrapper for single-file databases.
/// Intercepts DDL operations to work with single-file storage.
/// </summary>
internal class SingleFileSqlParser
{
    private readonly SqlParser _baseParser;
    private readonly SingleFileDatabase _database;
    private readonly TableDirectoryManager _tableDirectoryManager;

    public SingleFileSqlParser(SingleFileDatabase database, TableDirectoryManager tableDirectoryManager)
    {
        _database = database;
        _tableDirectoryManager = tableDirectoryManager;
        _baseParser = new SqlParser(database.Tables, database.DbPath, null!, false, null, database.Options.DatabaseConfig);
    }

    public void Execute(string sql, Dictionary<string, object?>? parameters = null)
    {
        var upperSql = sql.Trim().ToUpperInvariant();
        
        // Intercept DDL operations for single-file storage
        if (upperSql.StartsWith("CREATE TABLE"))
        {
            ExecuteCreateTable(sql);
            return;
        }
        else if (upperSql.StartsWith("DROP TABLE"))
        {
            ExecuteDropTable(sql);
            return;
        }
        
        // For DML operations, use the base parser but with modified table references
        _baseParser.Execute(sql, parameters!, null!);
    }

    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        // For SELECT operations, use the base parser
        return _baseParser.ExecuteQuery(sql, parameters ?? new Dictionary<string, object?>());
    }

    private void ExecuteCreateTable(string sql)
    {
        var parser = new BasicSqlParser(sql);
        var tableName = parser.GetTableName();
        var columns = parser.GetColumns();
        
        if (_database.Tables.ContainsKey(tableName))
        {
            throw new InvalidOperationException($"Table '{tableName}' already exists");
        }

        // Create column definitions
        var columnDefinitions = new List<ColumnDefinitionEntry>();
        var dataTypes = new List<DataType>();
        
        for (int i = 0; i < columns.Count; i++)
        {
            var columnDef = new ColumnDefinitionEntry
            {
                DataType = (uint)DataType.String, // Default to string
                Flags = 0
            };
            
            // Set column name
            SetColumnName(ref columnDef, columns[i]);
            columnDefinitions.Add(columnDef);
            dataTypes.Add(DataType.String);
        }
        
        // Create table metadata
        var table = new SingleFileTable(tableName, columns, dataTypes, _database.StorageProvider);
        _database.Tables[tableName] = table;
        
        // Allocate data block (placeholder for now)
        var dataBlockOffset = 1024 * 1024UL; // 1MB offset as placeholder
        
        // Store table metadata
        _tableDirectoryManager.CreateTable(table, dataBlockOffset, columnDefinitions, new List<IndexDefinitionEntry>());
    }

    private void ExecuteDropTable(string sql)
    {
        var parser = new BasicSqlParser(sql);
        var tableName = parser.GetTableName();
        
        if (!_database.Tables.Remove(tableName))
        {
            throw new InvalidOperationException($"Table '{tableName}' does not exist");
        }
        
        // Remove from table directory
        _tableDirectoryManager.DeleteTable(tableName);
    }

    private static unsafe void SetColumnName(ref ColumnDefinitionEntry entry, string name)
    {
        if (name.Length > ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH)
        {
            throw new ArgumentException($"Column name too long: {name.Length} > {ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH}");
        }
        
        var nameBytes = Encoding.UTF8.GetBytes(name);
        fixed (byte* ptr = entry.ColumnName)
        {
            var span = new Span<byte>(ptr, ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH + 1);
            span.Clear();
            nameBytes.CopyTo(span);
        }
    }
}
