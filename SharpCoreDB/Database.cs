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
                using var wal = new WAL(this._dbPath, this.config);
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
                using var wal = new WAL(this._dbPath, this.config);
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
            using var wal = new WAL(this._dbPath, this.config);
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

    /// <inheritdoc />
    public (long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics()
    {
        if (this.queryCache == null)
        {
            return (0, 0, 0, 0);
        }

        return this.queryCache.GetStatistics();
    }

    /// <inheritdoc />
    public List<Dictionary<string, object>> ExecuteQuery(string sql)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].ToUpper() != SqlConstants.SELECT)
        {
            throw new InvalidOperationException("ExecuteQuery only supports SELECT statements");
        }

        // Parse the SELECT statement
        var fromIdx = Array.IndexOf(parts, SqlConstants.FROM);
        if (fromIdx < 0)
        {
            throw new InvalidOperationException("SELECT statement must have FROM clause");
        }

        string[] keywords = ["WHERE", "ORDER", "LIMIT", "GROUP"];
        var fromParts = parts.Skip(fromIdx + 1).TakeWhile(p => !keywords.Contains(p.ToUpper())).ToArray();
        var whereIdx = Array.IndexOf(parts, SqlConstants.WHERE);
        var orderIdx = Array.IndexOf(parts, SqlConstants.ORDER);
        var limitIdx = Array.IndexOf(parts, "LIMIT");
        var groupIdx = Array.IndexOf(parts, "GROUP");

        string? whereStr = null;
        if (whereIdx > 0)
        {
            var endIdx = orderIdx > 0 ? orderIdx : limitIdx > 0 ? limitIdx : groupIdx > 0 ? groupIdx : parts.Length;
            whereStr = string.Join(" ", parts.Skip(whereIdx + 1).Take(endIdx - whereIdx - 1));
        }

        string? orderBy = null;
        bool asc = true;
        if (orderIdx > 0 && parts.Length > orderIdx + 3 && parts[orderIdx + 1].ToUpper() == SqlConstants.BY)
        {
            orderBy = parts[orderIdx + 2];
            asc = parts[orderIdx + 3].ToUpper() != SqlConstants.DESC;
        }

        int? limit = null;
        int? offset = null;
        if (limitIdx > 0)
        {
            var limitParts = parts.Skip(limitIdx + 1).ToArray();
            if (limitParts.Length > 0)
            {
                limit = int.Parse(limitParts[0]);
                if (limitParts.Length > 2 && limitParts[1].ToUpper() == "OFFSET")
                {
                    offset = int.Parse(limitParts[2]);
                }
            }
        }

        // Get table name
        var tableName = fromParts[0];
        if (!this.tables.ContainsKey(tableName))
        {
            throw new InvalidOperationException($"Table '{tableName}' does not exist");
        }

        // Execute the query
        var results = this.tables[tableName].Select(whereStr, orderBy, asc);

        // Apply limit and offset
        if (offset.HasValue)
        {
            results = results.Skip(offset.Value).ToList();
        }

        if (limit.HasValue)
        {
            results = results.Take(limit.Value).ToList();
        }

        return results;
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
