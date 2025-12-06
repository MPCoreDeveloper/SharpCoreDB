// <copyright file="Database.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Constants;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
/// Implementation of IDatabase.
/// </summary>
public class Database : IDatabase
{
    private readonly IStorage storage;
    private readonly IUserService userService;
    private readonly Dictionary<string, ITable> tables = [];
    private readonly string _dbPath;
    private readonly bool isReadOnly;
    private readonly DatabaseConfig? config;
    private readonly SecurityConfig? securityConfig;
    private readonly QueryCache? queryCache;
    private readonly object _walLock = new object();
    private readonly ConcurrentDictionary<string, CachedQueryPlan> _prepared = new();
    private readonly WalManager _walManager;

    // PERFORMANCE FIX: Cache prepared query plans to avoid repeated parsing
    private readonly ConcurrentDictionary<string, CachedQueryPlan> _preparedPlans = new();

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
        this.storage = new Storage(crypto, masterKey, this.config);
        this.userService = new UserService(crypto, this.storage, this._dbPath);

        // Get WalManager from services for pooled streams
        this._walManager = services.GetRequiredService<WalManager>();

        // Initialize query cache if enabled
        if (this.config.EnableQueryCache)
        {
            this.queryCache = new QueryCache(this.config.QueryCacheSize);
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

    private void Save(WAL wal)
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
        wal.Commit();
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
            lock (this._walLock)
            {
                using var wal = new WAL(this._dbPath, this.config, this._walManager);
                var sqlParser = new SqlParser(this.tables, wal, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(sql, wal);
                if (!this.isReadOnly)
                {
                    this.Save(wal);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => this.ExecuteSQL(sql), cancellationToken).ConfigureAwait(false);
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
            lock (this._walLock)
            {
                using var wal = new WAL(this._dbPath, this.config, this._walManager);
                var sqlParser = new SqlParser(this.tables, wal, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(sql, parameters, wal);
                if (!this.isReadOnly)
                {
                    this.Save(wal);
                }
            }
        }
    }

    /// <summary>
    /// Executes a parameterized SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default) => await Task.Run(() => this.ExecuteSQL(sql, parameters), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Executes multiple SQL commands in a batch for improved performance.
    /// Uses a single WAL transaction for all commands.
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
        // Optimized: Use ReadOnlySpan to avoid allocations during check
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

        // Batch all non-SELECT statements in a single WAL transaction
        lock (this._walLock)
        {
            using var wal = new WAL(this._dbPath, this.config, this._walManager);
            foreach (var sql in statements)
            {
                var sqlParser = new SqlParser(this.tables, wal, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(sql, wal);
            }

            if (!this.isReadOnly)
            {
                this.Save(wal);
            }
        }

        // Perform GC.Collect if configured for high-performance mode
        if (this.config.CollectGCAfterBatches)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
        }
    }

    /// <summary>
    /// Executes multiple SQL commands in a batch asynchronously.
    /// </summary>
    /// <param name="sqlStatements">Collection of SQL statements to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteBatchSQLAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => this.ExecuteBatchSQL(sqlStatements), cancellationToken).ConfigureAwait(false);
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
            lock (this._walLock)
            {
                using var wal = new WAL(this._dbPath, this.config, this._walManager);
                var sqlParser = new SqlParser(this.tables, wal, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
                sqlParser.Execute(stmt.Plan, parameters, wal);
                if (!this.isReadOnly)
                {
                    this.Save(wal);
                }
            }
        }
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
        await Task.Run(() => this.ExecutePrepared(stmt, parameters), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a prepared statement asynchronously with parameters.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <returns>A ValueTask representing the execution result.</returns>
    public async ValueTask<object> ExecutePreparedAsync(PreparedStatement stmt, params object[] parameters)
    {
        // PERFORMANCE FIX: Use cached plan without re-parsing
        var plan = _preparedPlans[stmt.Sql];
        // Convert params to dictionary
        var paramDict = new Dictionary<string, object?>();
        for (int i = 0; i < parameters.Length; i++)
        {
            paramDict[i.ToString()] = parameters[i];
        }
        await Task.Run(() => this.ExecutePrepared(stmt, paramDict));
        return new object(); // placeholder
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
            plan = new CachedQueryPlan { Sql = sql, Parts = parts };
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
    /// Executes a query asynchronously and returns the results as a streaming enumerable.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An asynchronous enumerable of rows.</returns>
    public async IAsyncEnumerable<Dictionary<string, object>> ExecuteQueryAsync(string sql, Dictionary<string, object?> parameters = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].ToUpper() != SqlConstants.SELECT)
        {
            yield break;
        }

        var sqlParser = new SqlParser(this.tables, null, this._dbPath, this.storage, this.isReadOnly, this.queryCache);
        var results = await Task.Run(() => sqlParser.ExecuteQuery(sql, parameters), cancellationToken);
        int capacity = EstimateCapacity(sql);
        var buffer = ArrayPool<Dictionary<string, object>>.Shared.Rent(capacity);
        try
        {
            int i = 0;
            foreach (var row in results)
            {
                if (i >= capacity) break;
                buffer[i] = row;
                yield return buffer[i];
                i++;
            }
        }
        finally
        {
            ArrayPool<Dictionary<string, object>>.Shared.Return(buffer);
        }
    }

    private int EstimateCapacity(string sql)
    {
        if (sql.Contains("LIMIT"))
        {
            var parts = sql.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var limitIdx = Array.IndexOf(parts, "LIMIT");
            if (limitIdx >= 0 && limitIdx + 1 < parts.Length)
            {
                if (int.TryParse(parts[limitIdx + 1], out var limit))
                {
                    return limit;
                }
            }
        }
        return 1000;
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
