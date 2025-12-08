// <copyright file="Database.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Constants;
using SharpCoreDB.Core.Cache;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Implementation of IDatabase.
/// </summary>
public class Database : IDatabase, IDisposable
{
    private readonly IStorage storage;
    private readonly IUserService userService;
    private readonly Dictionary<string, ITable> tables = [];
    private readonly string _dbPath;
    public string DbPath => _dbPath;
    private readonly bool isReadOnly;
    private readonly DatabaseConfig? config;
    private readonly SecurityConfig? securityConfig;
    private readonly QueryCache? queryCache;
    private readonly PageCache? pageCache;
    private readonly object _walLock = new object();
    private readonly ConcurrentDictionary<string, CachedQueryPlan> _prepared = new();
    private readonly ConcurrentDictionary<string, CachedQueryPlan> _preparedPlans = new();
    private readonly WalManager _walManager;
    
    // NEW: Group Commit WAL instance (long-lived)
    private readonly GroupCommitWAL? groupCommitWal;
    
    // NEW: Unique instance ID for this Database (prevents WAL file conflicts)
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    
    // Dispose flag
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="Database"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="dbPath">The database path.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <param name="isReadOnly">Whether the database is readonly.</param>
    /// <param name="config">Optional database configuration.</param>
    /// <param name="securityConfig">Optional security configuration.</param>
    public Database(IServiceProvider services, string dbPath, string masterPassword, bool isReadOnly = false, DatabaseConfig? config = null, SecurityConfig? securityConfig = null)
    {
        this._dbPath = dbPath;
        this.isReadOnly = isReadOnly;
        this.config = config ?? DatabaseConfig.Default;
        this.securityConfig = securityConfig ?? SecurityConfig.Default;
        Directory.CreateDirectory(this._dbPath);
        
        var crypto = services.GetRequiredService<ICryptoService>();
        var masterKey = crypto.DeriveKey(masterPassword, "salt");
        
        // Initialize PageCache if enabled
        if (this.config.EnablePageCache)
        {
            this.pageCache = new PageCache(
                capacity: this.config.PageCacheCapacity,
                pageSize: this.config.PageSize);
        }
        
        this.storage = new Storage(crypto, masterKey, this.config, this.pageCache);
        this.userService = new UserService(crypto, this.storage, this._dbPath);

        // Get WalManager from services for pooled streams
        this._walManager = services.GetRequiredService<WalManager>();

        // Initialize query cache if enabled
        if (this.config.EnableQueryCache)
        {
            this.queryCache = new QueryCache(this.config.QueryCacheSize);
        }

        // NEW: Initialize Group Commit WAL if enabled
        if (this.config.UseGroupCommitWal && !isReadOnly)
        {
            // Pass instance ID to prevent file locking conflicts
            this.groupCommitWal = new GroupCommitWAL(
                this._dbPath,
                this.config.WalDurabilityMode,
                this.config.WalMaxBatchSize,
                this.config.WalMaxBatchDelayMs,
                this._instanceId);  // NEW: Pass instance ID
                
            // Perform crash recovery on startup from this instance's WAL
            var recoveredOps = this.groupCommitWal.CrashRecovery();
            if (recoveredOps.Count > 0)
            {
                Console.WriteLine($"[GroupCommitWAL:{_instanceId.Substring(0, 8)}] Recovering {recoveredOps.Count} operations from WAL");
                foreach (var opData in recoveredOps)
                {
                    try
                    {
                        string sql = Encoding.UTF8.GetString(opData.Span);
                        this.ExecuteSQL(sql);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GroupCommitWAL:{_instanceId.Substring(0, 8)}] Recovery error: {ex.Message}");
                    }
                }
                
                // Clear WAL after successful recovery
                this.groupCommitWal.ClearAsync().GetAwaiter().GetResult();
                Console.WriteLine($"[GroupCommitWAL:{_instanceId.Substring(0, 8)}] Recovery complete, WAL cleared");
            }
            
            // Clean up old orphaned WAL files (older than 1 hour)
            GroupCommitWAL.CleanupOrphanedWAL(this._dbPath);
        }

        this.Load();
    }

    private void Load()
    {
        var metaPath = Path.Combine(this._dbPath, PersistenceConstants.MetaFileName);
        var metaJson = this.storage.Read(metaPath);
        if (metaJson != null)
        {
            var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson);
            if (meta != null && meta.TryGetValue(PersistenceConstants.TablesKey, out var tablesObj) && tablesObj != null)
            {
                var tablesObjString = tablesObj.ToString();
                if (!string.IsNullOrEmpty(tablesObjString))
                {
                    var tablesList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(tablesObjString);
                    if (tablesList != null)
                    {
                        foreach (var tableDict in tablesList)
                        {
                            var table = JsonSerializer.Deserialize<Table>(JsonSerializer.Serialize(tableDict));
                            if (table != null)
                            {
                                table.SetStorage(this.storage);
                                table.SetReadOnly(this.isReadOnly);
                                this.tables[table.Name] = table;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public void ExecuteSQL(string sql)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
            sqlParser.Execute(sql, null);
        }
        else
        {
            if (this.groupCommitWal != null)
            {
                // NEW: Use GroupCommitWAL with async commit
                ExecuteSQLWithGroupCommit(sql).GetAwaiter().GetResult();
            }
            else
            {
                // Fallback: Legacy synchronous execution (for backward compatibility if GroupCommit disabled)
                lock (this._walLock)
                {
                    // NOTE: Legacy WAL removed - operations are not crash-safe without GroupCommitWAL!
                    var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                    sqlParser.Execute(sql, null);
                    
                    if (!this.isReadOnly)
                    {
                        this.SaveMetadata();
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            await Task.Run(() =>
            {
                var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(sql, null);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (this.groupCommitWal != null)
            {
                // NEW: Use GroupCommitWAL with true async
                await ExecuteSQLWithGroupCommit(sql, cancellationToken);
            }
            else
            {
                // Fallback: Legacy synchronous execution
                await Task.Run(() => this.ExecuteSQL(sql), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes SQL with Group Commit WAL (async operation).
    /// </summary>
    private async Task ExecuteSQLWithGroupCommit(string sql, CancellationToken cancellationToken = default)
    {
        lock (this._walLock)
        {
            // Execute the SQL operation
            var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
            sqlParser.Execute(sql, null);
            
            // Save metadata
            if (!this.isReadOnly)
            {
                this.SaveMetadata();
            }
        }
        
        // Commit to WAL asynchronously (outside lock for better concurrency)
        byte[] walData = Encoding.UTF8.GetBytes(sql);
        await this.groupCommitWal!.CommitAsync(walData, cancellationToken);
    }

    /// <summary>
    /// Saves database metadata without WAL parameter.
    /// </summary>
    private void SaveMetadata()
    {
        var tablesList = this.tables.Values.Select(t => new
        {
            t.Name,
            t.Columns,
            t.ColumnTypes,
            t.PrimaryKeyIndex,
            t.DataFile,
        }).ToList();
        var meta = new Dictionary<string, object> { [PersistenceConstants.TablesKey] = tablesList };
        this.storage.Write(Path.Combine(this._dbPath, PersistenceConstants.MetaFileName), JsonSerializer.Serialize(meta));
    }

    /// <summary>
    /// Executes a parameterized SQL command.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    public void ExecuteSQL(string sql, Dictionary<string, object?> parameters)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
            sqlParser.Execute(sql, parameters, null);
        }
        else
        {
            if (this.groupCommitWal != null)
            {
                // NEW: Use GroupCommitWAL
                ExecuteSQLWithGroupCommit(sql, parameters).GetAwaiter().GetResult();
            }
            else
            {
                // Fallback: Legacy execution
                lock (this._walLock)
                {
                    var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                    sqlParser.Execute(sql, parameters, null);
                    
                    if (!this.isReadOnly)
                    {
                        this.SaveMetadata();
                    }
                }
            }
        }
    }

    private async Task ExecuteSQLWithGroupCommit(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        lock (this._walLock)
        {
            var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
            sqlParser.Execute(sql, parameters, null);
            
            if (!this.isReadOnly)
            {
                this.SaveMetadata();
            }
        }
        
        // Serialize SQL with parameters for WAL
        var walEntry = new { Sql = sql, Parameters = parameters };
        byte[] walData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(walEntry));
        await this.groupCommitWal!.CommitAsync(walData, cancellationToken);
    }

    /// <summary>
    /// Executes a parameterized SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            await Task.Run(() =>
            {
                var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(sql, parameters, null);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (this.groupCommitWal != null)
            {
                await ExecuteSQLWithGroupCommit(sql, parameters, cancellationToken);
            }
            else
            {
                await Task.Run(() => this.ExecuteSQL(sql, parameters), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes a prepared statement with parameters.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    public void ExecutePrepared(PreparedStatement stmt, Dictionary<string, object?> parameters)
    {
        var parts = stmt.Plan.Parts;
        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
            sqlParser.Execute(stmt.Plan, parameters, null);
        }
        else
        {
            if (this.groupCommitWal != null)
            {
                ExecutePreparedWithGroupCommit(stmt, parameters).GetAwaiter().GetResult();
            }
            else
            {
                lock (this._walLock)
                {
                    var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                    sqlParser.Execute(stmt.Plan, parameters, null);
                    
                    if (!this.isReadOnly)
                    {
                        this.SaveMetadata();
                    }
                }
            }
        }
    }

    private async Task ExecutePreparedWithGroupCommit(PreparedStatement stmt, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        lock (this._walLock)
        {
            var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
            sqlParser.Execute(stmt.Plan, parameters, null);
            
            if (!this.isReadOnly)
            {
                this.SaveMetadata();
            }
        }
        
        // Commit to WAL
        var walEntry = new { Sql = stmt.Sql, Parameters = parameters };
        byte[] walData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(walEntry));
        await this.groupCommitWal!.CommitAsync(walData, cancellationToken);
    }

    /// <summary>
    /// Executes a prepared statement asynchronously with parameters.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecutePreparedAsync(PreparedStatement stmt, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        var parts = stmt.Plan.Parts;
        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            await Task.Run(() =>
            {
                var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(stmt.Plan, parameters, null);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (this.groupCommitWal != null)
            {
                await ExecutePreparedWithGroupCommit(stmt, parameters, cancellationToken);
            }
            else
            {
                await Task.Run(() => this.ExecutePrepared(stmt, parameters), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes a prepared statement asynchronously with parameters.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <returns>A ValueTask representing the execution result.</returns>
    public async ValueTask<object> ExecutePreparedAsync(PreparedStatement stmt, params object[] parameters)
    {
        var plan = _preparedPlans[stmt.Sql];
        var paramDict = new Dictionary<string, object?>();
        for (int i = 0; i < parameters.Length; i++)
        {
            paramDict[i.ToString()] = parameters[i];
        }
        await this.ExecutePreparedAsync(stmt, paramDict);
        return new object();
    }

    /// <inheritdoc />
    public void CreateUser(string username, string password) => this.userService.CreateUser(username, password);

    /// <inheritdoc />
    public bool Login(string username, string password)
    {
        return this.userService.Login(username, password);
    }

    /// <inheritdoc />
    public IDatabase Initialize(string dbPath, string masterPassword)
    {
        // Already initialized in constructor
        return this;
    }

    /// <summary>
    /// Prepares a SQL statement for efficient repeated execution.
    /// </summary>
    /// <param name="sql">The SQL statement to prepare.</param>
    /// <returns>A prepared statement instance.</returns>
    public PreparedStatement Prepare(string sql)
    {
        // PERFORMANCE FIX: Parse and cache query plan once per unique SQL string
        if (!_preparedPlans.TryGetValue(sql, out var plan))
        {
            var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            plan = new CachedQueryPlan(sql, parts);
            _preparedPlans[sql] = plan;
        }
        return new PreparedStatement(sql, plan);
    }

    /// <summary>
    /// Gets query cache statistics.
    /// </summary>
    /// <returns>A tuple containing cache hits, misses, hit rate, and total cached queries.</returns>
    public (long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics()
    {
        if (this.queryCache == null)
        {
            return (0, 0, 0, 0);
        }

        return this.queryCache.GetStatistics();
    }

    /// <summary>
    /// Clears the query cache.
    /// </summary>
    public void ClearQueryCache()
    {
        this.queryCache?.Clear();
    }

    /// <summary>
    /// Gets page cache statistics.
    /// </summary>
    /// <returns>A tuple containing cache hits, misses, hit rate, evictions, and current size.</returns>
    public (long Hits, long Misses, double HitRate, long Evictions, int CurrentSize, int Capacity) GetPageCacheStatistics()
    {
        if (this.pageCache == null)
        {
            return (0, 0, 0, 0, 0, 0);
        }

        var stats = this.pageCache.Statistics;
        return (stats.Hits, stats.Misses, stats.HitRate, stats.Evictions, this.pageCache.Count, this.pageCache.Capacity);
    }

    /// <summary>
    /// Clears the page cache, optionally flushing dirty pages.
    /// </summary>
    /// <param name="flushDirty">Whether to flush dirty pages before clearing.</param>
    public void ClearPageCache(bool flushDirty = false)
    {
        if (this.pageCache != null)
        {
            this.pageCache.Clear(flushDirty, (id, data) => 
            {
                // Flush logic would go here if needed
                // For now, dirty pages are handled by WAL
            });
        }
    }

    /// <summary>
    /// Gets database statistics including performance metrics.
    /// </summary>
    /// <returns>A dictionary of statistics.</returns>
    public Dictionary<string, object> GetDatabaseStatistics()
    {
        var stats = new Dictionary<string, object>
        {
            ["TablesCount"] = this.tables.Count,
            ["IsReadOnly"] = this.isReadOnly,
            ["QueryCacheEnabled"] = this.config.EnableQueryCache,
            ["NoEncryptMode"] = this.config.NoEncryptMode,
            ["UseMemoryMapping"] = this.config.UseMemoryMapping,
            ["WalBufferSize"] = this.config.WalBufferSize,
            ["PageCacheEnabled"] = this.config.EnablePageCache,
        };

        if (this.queryCache != null)
        {
            var cacheStats = this.queryCache.GetStatistics();
            stats["QueryCacheHits"] = cacheStats.Hits;
            stats["QueryCacheMisses"] = cacheStats.Misses;
            stats["QueryCacheHitRate"] = cacheStats.HitRate;
            stats["QueryCacheCount"] = cacheStats.Count;
        }

        if (this.pageCache != null)
        {
            var pageCacheStats = this.pageCache.Statistics;
            stats["PageCacheHits"] = pageCacheStats.Hits;
            stats["PageCacheMisses"] = pageCacheStats.Misses;
            stats["PageCacheHitRate"] = pageCacheStats.HitRate;
            stats["PageCacheEvictions"] = pageCacheStats.Evictions;
            stats["PageCacheSize"] = this.pageCache.Count;
            stats["PageCacheCapacity"] = this.pageCache.Capacity;
            stats["PageCacheLatchFailures"] = pageCacheStats.LatchFailures;
        }

        // Add table-specific stats
        foreach (var kv in this.tables)
        {
            var table = kv.Value;
            stats[$"Table_{kv.Key}_Columns"] = table.Columns.Count;
            stats[$"Table_{kv.Key}_Rows"] = table.Select().Count; // Rough estimate
            var usage = table.GetColumnUsage();
            stats[$"Table_{kv.Key}_ColumnUsage"] = usage;
        }

        return stats;
    }

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters = null)
    {
        var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
        return sqlParser.ExecuteQuery(sql, parameters);
    }

    /// <summary>
    /// Executes a query and returns the results with optional encryption bypass.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this query.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt)
    {
        var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache, noEncrypt);
        return sqlParser.ExecuteQuery(sql, parameters, noEncrypt);
    }

    /// <summary>
    /// Executes multiple SQL commands in a batch for improved performance.
    /// Uses GroupCommitWAL for crash safety with batching.
    /// </summary>
    /// <param name="sqlStatements">Collection of SQL statements to execute.</param>
    public void ExecuteBatchSQL(IEnumerable<string> sqlStatements)
    {
        var statements = sqlStatements as string[] ?? sqlStatements.ToArray();
        if (statements.Length == 0)
        {
            return;
        }

        // Check if any statement is a SELECT - if so, process individually
        var hasSelect = false;
        foreach (var sql in statements)
        {
            var trimmed = sql.AsSpan().Trim();
            if (trimmed.Length >= 6 && trimmed[..6].Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                hasSelect = true;
                break;
            }
        }

        if (hasSelect)
        {
            // Process individually if there are SELECTs
            foreach (var sql in statements)
            {
                this.ExecuteSQL(sql);
            }
            return;
        }

        // Batch all non-SELECT statements
        if (this.groupCommitWal != null)
        {
            // NEW: Use GroupCommitWAL for batched operations
            ExecuteBatchSQLWithGroupCommit(statements).GetAwaiter().GetResult();
        }
        else
        {
            // Fallback: Legacy batch execution
            lock (this._walLock)
            {
                foreach (var sql in statements)
                {
                    var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                    sqlParser.Execute(sql, null);
                }

                if (!this.isReadOnly)
                {
                    this.SaveMetadata();
                }
            }
        }

        // Perform GC.Collect if configured for high-performance mode
        if (this.config.CollectGCAfterBatches)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
        }
    }

    private async Task ExecuteBatchSQLWithGroupCommit(string[] statements, CancellationToken cancellationToken = default)
    {
        // Execute all statements in a single lock
        lock (this._walLock)
        {
            foreach (var sql in statements)
            {
                var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(sql, null);
            }

            if (!this.isReadOnly)
            {
                this.SaveMetadata();
            }
        }
        
        // Commit entire batch to WAL asynchronously (benefits from group commit batching)
        var tasks = new List<Task>();
        foreach (var sql in statements)
        {
            byte[] walData = Encoding.UTF8.GetBytes(sql);
            tasks.Add(this.groupCommitWal!.CommitAsync(walData, cancellationToken));
        }
        
        // Wait for all commits (they will be batched together by GroupCommitWAL)
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Executes multiple SQL commands in a batch asynchronously.
    /// </summary>
    /// <param name="sqlStatements">Collection of SQL statements to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteBatchSQLAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default)
    {
        var statements = sqlStatements as string[] ?? sqlStatements.ToArray();
        if (statements.Length == 0)
        {
            return;
        }

        // Check if any statement is a SELECT
        var hasSelect = false;
        foreach (var sql in statements)
        {
            var trimmed = sql.AsSpan().Trim();
            if (trimmed.Length >= 6 && trimmed[..6].Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                hasSelect = true;
                break;
            }
        }

        if (hasSelect)
        {
            // Process individually if there are SELECTs
            foreach (var sql in statements)
            {
                await this.ExecuteSQLAsync(sql, cancellationToken);
            }
            return;
        }

        // Batch all non-SELECT statements
        if (this.groupCommitWal != null)
        {
            await ExecuteBatchSQLWithGroupCommit(statements, cancellationToken);
        }
        else
        {
            await Task.Run(() => this.ExecuteBatchSQL(sqlStatements), cancellationToken).ConfigureAwait(false);
        }

        // Perform GC.Collect if configured
        if (this.config.CollectGCAfterBatches)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
        }
    }

    /// <summary>
    /// Disposes the database and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for cleanup.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Dispose managed resources
            groupCommitWal?.Dispose();
            pageCache?.Clear(false, null);
            queryCache?.Clear();
        }

        _disposed = true;
    }
}

/// <summary>
/// Extension methods for configuring SharpCoreDB services.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Adds SharpCoreDB services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection with SharpCoreDB services added.</returns>
    public static IServiceCollection AddSharpCoreDB(this IServiceCollection services)
    {
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddTransient<DatabaseFactory>();
        services.AddSingleton<WalManager>();
        return services;
    }
}

/// <summary>
/// Factory for creating Database instances.
/// </summary>
public class DatabaseFactory(IServiceProvider services)
{
    private readonly IServiceProvider services = services;

    /// <summary>
    /// Creates a new Database instance and initializes it.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <param name="isReadOnly">Whether the database is readonly.</param>
    /// <param name="config">Optional database configuration.</param>
    /// <param name="securityConfig">Optional security configuration.</param>
    /// <returns>The initialized database.</returns>
    public IDatabase Create(string dbPath, string masterPassword, bool isReadOnly = false, DatabaseConfig? config = null, SecurityConfig? securityConfig = null)
    {
        return new Database(this.services, dbPath, masterPassword, isReadOnly, config, securityConfig);
    }
}
