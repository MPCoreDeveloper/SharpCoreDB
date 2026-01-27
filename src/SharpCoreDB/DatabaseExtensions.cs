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

    public void Flush() => _storageProvider.FlushAsync().GetAwaiter().GetResult();

    public void ForceSave()
    {
        Flush();
        _tableDirectoryManager.Flush();
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
        var regex = new Regex(
            @"CREATE\s+TABLE\s+(\w+)\s*\((.*)\)", 
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        var match = regex.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid CREATE TABLE syntax: {sql}");
        }

        var tableName = match.Groups[1].Value.Trim();
        var columnDefs = match.Groups[2].Value.Trim();
        
        var columns = new List<string>();
        var columnTypes = new List<DataType>();
        
        foreach (var colDef in columnDefs.Split(','))
        {
            var parts = colDef.Trim().Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                columns.Add(parts[0]);
                var typeStr = parts[1].ToUpperInvariant();
                columnTypes.Add(typeStr switch
                {
                    "INT" or "INTEGER" => DataType.Integer,
                    "TEXT" or "VARCHAR" => DataType.String,
                    "REAL" or "FLOAT" or "DECIMAL" => DataType.Decimal,
                    "DATETIME" or "DATE" => DataType.DateTime,
                    _ => DataType.String
                });
            }
        }
        
        var table = new SingleFileTable(tableName, columns, columnTypes, _storageProvider);
        _tables[tableName] = table;
        
        _tableDirectoryManager.Flush();
    }

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

    private void ExecuteInsertInternal(string sql)
    {
        var regex = new Regex(
            @"INSERT\s+INTO\s+(\w+)\s*\((.*?)\)\s*VALUES\s*\((.*?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        var match = regex.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid INSERT syntax: {sql}");
        }

        var tableName = match.Groups[1].Value.Trim();
        var columnNames = match.Groups[2].Value.Split(',').Select(c => c.Trim()).ToList();
        var values = match.Groups[3].Value.Split(',').Select(v => v.Trim()).ToList();
        
        if (!_tables.TryGetValue(tableName, out var table))
        {
            throw new InvalidOperationException($"Table '{tableName}' not found");
        }

        var row = new Dictionary<string, object>();
        for (int i = 0; i < columnNames.Count && i < values.Count; i++)
        {
            var value = ParseValue(values[i]);
            row[columnNames[i]] = value;
        }
        
        table.Insert(row);
    }

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

/// <summary>
/// Table implementation for single-file storage.
/// ✅ CRITICAL FIX: Accumulates rows in a single JSON array block to prevent checksum mismatches.
/// </summary>
internal class SingleFileTable : ITable
{
    private readonly string _tableName;
    private readonly List<string> _columns;
    private readonly List<DataType> _columnTypes;
    private readonly IStorageProvider _storageProvider;
    private readonly string _dataBlockName;
    private readonly Lock _tableLock = new();
    private long _nextId = 1;

    public SingleFileTable(string tableName, List<string> columns, List<DataType> columnTypes, IStorageProvider storageProvider)
    {
        _tableName = tableName;
        _columns = columns;
        _columnTypes = columnTypes;
        _storageProvider = storageProvider;
        _dataBlockName = $"table:{tableName}:data";
    }

    public SingleFileTable(string tableName, IStorageProvider storageProvider, TableMetadataEntry metadata)
    {
        _tableName = tableName;
        _storageProvider = storageProvider;
        _dataBlockName = $"table:{tableName}:data";
        
        var tableDirManager = ((SingleFileStorageProvider)storageProvider).TableDirectoryManager;
        var columnDefs = tableDirManager.GetColumnDefinitions(tableName);
        
        _columns = new List<string>();
        _columnTypes = new List<DataType>();
        
        foreach (var colDef in columnDefs)
        {
            _columns.Add(GetColumnName(colDef));
            _columnTypes.Add((DataType)colDef.DataType);
        }
        
        RebuildNextId();
    }

    public string Name { get => _tableName; set => throw new NotSupportedException(); }
    public List<string> Columns => _columns;
    public List<DataType> ColumnTypes => _columnTypes;
    public int PrimaryKeyIndex => 0;
    public List<bool> IsAuto => new List<bool> { true };
    public List<bool> IsNotNull => new List<bool> { false };
    public List<object?> DefaultValues => new List<object?> { null };
    public List<List<string>> UniqueConstraints => new List<List<string>>();
    public List<ForeignKeyConstraint> ForeignKeys => new List<ForeignKeyConstraint>();
    public string DataFile { get => _dataBlockName; set => throw new NotSupportedException(); }

    public void Insert(Dictionary<string, object> row)
    {
        lock (_tableLock)
        {
            if (!row.ContainsKey(_columns[0]))
            {
                row[_columns[0]] = _nextId++;
            }

            var allRows = ReadAllRowsInternal();
            allRows.Add(row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));
            WriteAllRowsInternal(allRows);
        }
    }

    public long[] InsertBatch(List<Dictionary<string, object>> rows)
    {
        lock (_tableLock)
        {
            var positions = new long[rows.Count];
            var allRows = ReadAllRowsInternal();
            
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].ContainsKey(_columns[0]))
                {
                    rows[i][_columns[0]] = _nextId++;
                }
                allRows.Add(rows[i].ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));
                positions[i] = i;
            }
            
            WriteAllRowsInternal(allRows);
            return positions;
        }
    }

    public long[] InsertBatchFromBuffer(ReadOnlySpan<byte> encodedData, int rowCount) 
        => throw new NotImplementedException();

    public void Update(Dictionary<string, object> row)
    {
        lock (_tableLock)
        {
            if (!row.ContainsKey(_columns[0]))
            {
                row[_columns[0]] = _nextId++;
            }
            
            var allRows = ReadAllRowsInternal();
            var pkColumn = _columns[0];
            var pkValue = row[pkColumn];
            
            var index = allRows.FindIndex(r => 
                r.TryGetValue(pkColumn, out var existing) && Equals(existing, pkValue));
            
            if (index >= 0)
            {
                allRows[index] = row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
            }
            else
            {
                allRows.Add(row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));
            }
            
            WriteAllRowsInternal(allRows);
        }
    }

    public void Delete(Dictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        lock (_tableLock)
        {
            if (PrimaryKeyIndex >= 0 && PrimaryKeyIndex < _columns.Count)
            {
                var pkColumn = _columns[PrimaryKeyIndex];
                
                if (!row.TryGetValue(pkColumn, out var pkValue) || pkValue is null)
                {
                    throw new InvalidOperationException($"Cannot delete row: Primary key column '{pkColumn}' is null");
                }

                var allRows = ReadAllRowsInternal();
                allRows.RemoveAll(r => r.TryGetValue(pkColumn, out var existing) && Equals(existing, pkValue));
                WriteAllRowsInternal(allRows);
            }
        }
    }

    public List<Dictionary<string, object>> Select()
    {
        lock (_tableLock)
        {
            var allRows = ReadAllRowsInternal();
            return allRows.Cast<Dictionary<string, object>>().ToList();
        }
    }

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
            var allRows = ReadAllRowsInternal();

            for (int i = 0; i < allRows.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(whereClause) && !EvaluateWhereClause(allRows[i], whereClause))
                {
                    continue;
                }

                foreach (var kvp in updates)
                {
                    if (allRows[i].ContainsKey(kvp.Key))
                    {
                        allRows[i][kvp.Key] = kvp.Value;
                    }
                }
            }

            WriteAllRowsInternal(allRows);
        }
    }

    private bool EvaluateWhereClause(Dictionary<string, object?> row, string whereClause)
    {
        var condition = whereClause.Trim();
        if (condition.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
        {
            condition = condition.Substring(5).Trim();
        }

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

    public void Delete(string? whereClause) => throw new NotImplementedException();
    public void CreateHashIndex(string columnName) => throw new NotImplementedException();
    public void CreateHashIndex(string indexName, string columnName, bool isUnique = false) => throw new NotImplementedException();
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
    public void CreateBTreeIndex(string columnName) => throw new NotImplementedException();
    public void CreateBTreeIndex(string indexName, string columnName, bool isUnique = false) => throw new NotImplementedException();
    public bool HasBTreeIndex(string columnName) => false;
    public void Flush() { }
    public void AddColumn(ColumnDefinition columnDef) => throw new NotImplementedException();

    private List<Dictionary<string, object?>> ReadAllRowsInternal()
    {
        if (!_storageProvider.BlockExists(_dataBlockName))
        {
            return new List<Dictionary<string, object?>>();
        }

        var data = _storageProvider.ReadBlockAsync(_dataBlockName)
            .GetAwaiter().GetResult();

        if (data == null || data.Length == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        if (BinaryRowSerializer.IsBinaryFormat(data))
        {
            return BinaryRowSerializer.Deserialize(data, _columns, _columnTypes);
        }

        var json = Encoding.UTF8.GetString(data);
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json)
                ?? new List<Dictionary<string, object?>>();
        }
        catch
        {
            return new List<Dictionary<string, object?>>();
        }
    }

    private void WriteAllRowsInternal(List<Dictionary<string, object?>> allRows)
    {
        var data = BinaryRowSerializer.Serialize(allRows, _columns, _columnTypes);
        _storageProvider.WriteBlockAsync(_dataBlockName, data)
            .GetAwaiter().GetResult();
    }

    private static class BinaryRowSerializer
    {
        private const uint Magic = 0x53465431; // 'SFT1'
        private const byte Version = 1;
        private static readonly Encoding Utf8 = Encoding.UTF8;

        public static bool IsBinaryFormat(ReadOnlySpan<byte> data)
        {
            return data.Length >= 9
                && BinaryPrimitives.ReadUInt32LittleEndian(data) == Magic
                && data[4] == Version;
        }

        public static byte[] Serialize(List<Dictionary<string, object?>> rows, List<string> columns, List<DataType> columnTypes)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(columns);
            ArgumentNullException.ThrowIfNull(columnTypes);

            var estimated = Math.Max(256, 16 * columns.Count * Math.Max(1, rows.Count));
            var writer = new PooledBufferWriter(estimated);

            WriteUInt32(ref writer, Magic);
            writer.WriteByte(Version);
            WriteInt32(ref writer, rows.Count);
            WriteInt32(ref writer, columns.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                for (int c = 0; c < columns.Count; c++)
                {
                    var colName = columns[c];
                    var type = columnTypes.Count > c ? columnTypes[c] : DataType.String;
                    row.TryGetValue(colName, out var value);
                    WriteValue(ref writer, type, value);
                }
            }

            return writer.ToArray();
        }

        public static List<Dictionary<string, object?>> Deserialize(ReadOnlySpan<byte> data, List<string> columns, List<DataType> columnTypes)
        {
            var rows = new List<Dictionary<string, object?>>();
            int offset = 0;

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            if (magic != Magic)
            {
                return rows;
            }

            var version = data[offset];
            offset += 1;
            if (version != Version)
            {
                return rows;
            }

            var rowCount = ReadInt32(data, ref offset);
            var columnCount = ReadInt32(data, ref offset);
            var effectiveColumns = Math.Min(columnCount, columns.Count);

            for (int i = 0; i < rowCount; i++)
            {
                var row = new Dictionary<string, object?>(effectiveColumns);
                for (int c = 0; c < effectiveColumns; c++)
                {
                    var type = columnTypes.Count > c ? columnTypes[c] : DataType.String;
                    row[columns[c]] = ReadValue(data, ref offset, type);
                }

                // Skip any extra serialized columns if schema changed
                for (int c = effectiveColumns; c < columnCount; c++)
                {
                    _ = ReadValue(data, ref offset, DataType.String);
                }

                rows.Add(row);
            }

            return rows;
        }

        private static void WriteValue(ref PooledBufferWriter writer, DataType type, object? value)
        {
            if (value is null)
            {
                writer.WriteByte(0);
                return;
            }

            writer.WriteByte(1);

            switch (type)
            {
                case DataType.Integer:
                    WriteInt32(ref writer, Convert.ToInt32(value));
                    break;
                case DataType.Long:
                    WriteInt64(ref writer, Convert.ToInt64(value));
                    break;
                case DataType.Decimal:
                    WriteDecimal(ref writer, Convert.ToDecimal(value));
                    break;
                case DataType.Real:
                    WriteDouble(ref writer, Convert.ToDouble(value));
                    break;
                case DataType.Boolean:
                    writer.WriteByte(Convert.ToBoolean(value) ? (byte)1 : (byte)0);
                    break;
                case DataType.DateTime:
                    WriteDateTime(ref writer, Convert.ToDateTime(value));
                    break;
                case DataType.Guid:
                    WriteGuid(ref writer, (value is Guid g) ? g : Guid.Parse(value.ToString()!));
                    break;
                case DataType.Ulid:
                    WriteString(ref writer, value.ToString() ?? string.Empty);
                    break;
                case DataType.Blob:
                    WriteBytes(ref writer, value as byte[] ?? Array.Empty<byte>());
                    break;
                default:
                    WriteString(ref writer, value.ToString() ?? string.Empty);
                    break;
            }
        }

        private static object? ReadValue(ReadOnlySpan<byte> data, ref int offset, DataType type)
        {
            var hasValue = data[offset] != 0;
            offset += 1;
            if (!hasValue)
            {
                return null;
            }

            return type switch
            {
                DataType.Integer => ReadInt32(data, ref offset),
                DataType.Long => ReadInt64(data, ref offset),
                DataType.Decimal => ReadDecimal(data, ref offset),
                DataType.Real => ReadDouble(data, ref offset),
                DataType.Boolean => ReadBoolean(data, ref offset),
                DataType.DateTime => ReadDateTime(data, ref offset),
                DataType.Guid => ReadGuid(data, ref offset),
                DataType.Ulid => ReadString(data, ref offset),
                DataType.Blob => ReadBytes(data, ref offset),
                _ => ReadString(data, ref offset),
            };
        }

        private static void WriteString(ref PooledBufferWriter writer, string value)
        {
            var byteCount = Utf8.GetByteCount(value);
            WriteInt32(ref writer, byteCount);
            writer.WriteString(Utf8, value, byteCount);
        }

        private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
        {
            var length = ReadInt32(data, ref offset);
            var str = Utf8.GetString(data.Slice(offset, length));
            offset += length;
            return str;
        }

        private static void WriteBytes(ref PooledBufferWriter writer, byte[] bytes)
        {
            WriteInt32(ref writer, bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int offset)
        {
            var length = ReadInt32(data, ref offset);
            var result = new byte[length];
            data.Slice(offset, length).CopyTo(result);
            offset += length;
            return result;
        }

        private static void WriteGuid(ref PooledBufferWriter writer, Guid guid)
        {
            const int size = 16;
            writer.EnsureCapacity(size);
            guid.TryWriteBytes(writer.GetSpan(size));
            writer.Advance(size);
        }

        private static Guid ReadGuid(ReadOnlySpan<byte> data, ref int offset)
        {
            var guid = new Guid(data.Slice(offset, 16));
            offset += 16;
            return guid;
        }

        private static void WriteDateTime(ref PooledBufferWriter writer, DateTime value)
        {
            WriteInt64(ref writer, value.Ticks);
            writer.WriteByte((byte)value.Kind);
        }

        private static DateTime ReadDateTime(ReadOnlySpan<byte> data, ref int offset)
        {
            var ticks = ReadInt64(data, ref offset);
            var kind = (DateTimeKind)data[offset];
            offset += 1;
            return new DateTime(ticks, kind);
        }

        private static void WriteDecimal(ref PooledBufferWriter writer, decimal value)
        {
            var bits = decimal.GetBits(value);
            for (int i = 0; i < bits.Length; i++)
            {
                WriteInt32(ref writer, bits[i]);
            }
        }

        private static decimal ReadDecimal(ReadOnlySpan<byte> data, ref int offset)
        {
            int[] bits = new int[4];
            for (int i = 0; i < 4; i++)
            {
                bits[i] = ReadInt32(data, ref offset);
            }
            return new decimal(bits);
        }

        private static void WriteDouble(ref PooledBufferWriter writer, double value)
        {
            WriteInt64(ref writer, BitConverter.DoubleToInt64Bits(value));
        }

        private static double ReadDouble(ReadOnlySpan<byte> data, ref int offset)
        {
            var bits = ReadInt64(data, ref offset);
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static bool ReadBoolean(ReadOnlySpan<byte> data, ref int offset)
        {
            var value = data[offset] != 0;
            offset += 1;
            return value;
        }

        private static void WriteInt32(ref PooledBufferWriter writer, int value)
        {
            writer.EnsureCapacity(4);
            BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), value);
            writer.Advance(4);
        }

        private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static void WriteUInt32(ref PooledBufferWriter writer, uint value)
        {
            writer.EnsureCapacity(4);
            BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(4), value);
            writer.Advance(4);
        }

        private static void WriteInt64(ref PooledBufferWriter writer, long value)
        {
            writer.EnsureCapacity(8);
            BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), value);
            writer.Advance(8);
        }

        private static long ReadInt64(ReadOnlySpan<byte> data, ref int offset)
        {
            var value = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;
            return value;
        }

        private ref struct PooledBufferWriter
        {
            private byte[] _buffer;
            private int _position;

            public PooledBufferWriter(int initialCapacity)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
                _position = 0;
            }

            public void Write(ReadOnlySpan<byte> source)
            {
                EnsureCapacity(source.Length);
                source.CopyTo(_buffer.AsSpan(_position));
                _position += source.Length;
            }

            public void Write(byte[] source)
            {
                EnsureCapacity(source.Length);
                source.CopyTo(_buffer, _position);
                _position += source.Length;
            }

            public void WriteString(Encoding encoding, string value, int byteCount)
            {
                EnsureCapacity(byteCount);
                encoding.GetBytes(value, _buffer.AsSpan(_position, byteCount));
                _position += byteCount;
            }

            public void WriteByte(byte value)
            {
                EnsureCapacity(1);
                _buffer[_position++] = value;
            }

            public Span<byte> GetSpan(int length)
            {
                EnsureCapacity(length);
                return _buffer.AsSpan(_position, length);
            }

            public void Advance(int count)
            {
                _position += count;
            }

            public void EnsureCapacity(int additional)
            {
                var required = _position + additional;
                if (required <= _buffer.Length)
                {
                    return;
                }

                var newSize = _buffer.Length * 2;
                if (newSize < required)
                {
                    newSize = required * 2;
                }

                var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);
                ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
                _buffer = newBuffer;
            }

            public byte[] ToArray()
            {
                var result = new byte[_position];
                Buffer.BlockCopy(_buffer, 0, result, 0, _position);
                ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
                _buffer = Array.Empty<byte>();
                return result;
            }
        }
    }

    private static string GetColumnName(ColumnDefinitionEntry colDef)
    {
        unsafe
        {
            var span = new ReadOnlySpan<byte>(colDef.ColumnName, ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH + 1);
            var len = span.IndexOf((byte)0);
            if (len < 0)
            {
                len = ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH;
            }
            return Encoding.UTF8.GetString(span.Slice(0, len));
        }
    }

    private void RebuildNextId()
    {
        if (_columns.Count == 0)
        {
            return;
        }

        lock (_tableLock)
        {
            var pkColumn = _columns[0];
            var allRows = ReadAllRowsInternal();
            long maxId = 0;

            foreach (var row in allRows)
            {
                if (row.TryGetValue(pkColumn, out var value) && value != null)
                {
                    var candidate = Convert.ToInt64(value);
                    if (candidate > maxId)
                    {
                        maxId = candidate;
                    }
                }
            }

            _nextId = maxId + 1;
        }
    }
}
