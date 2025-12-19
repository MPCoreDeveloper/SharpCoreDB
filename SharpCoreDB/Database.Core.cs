// <copyright file="Database.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Constants;
using SharpCoreDB.Core.Cache;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Database implementation - Core partial class with fields and initialization.
/// Modern C# 14 with collection expressions and primary constructors support.
/// </summary>
public partial class Database : IDatabase, IDisposable
{
    private readonly IStorage storage;
    private readonly IUserService userService;
    private readonly Dictionary<string, ITable> tables = [];
    private readonly string _dbPath;
    
    /// <summary>
    /// Gets the database path.
    /// </summary>
    public string DbPath => _dbPath;
    
    private readonly bool isReadOnly;
    private readonly DatabaseConfig? config;
    private readonly QueryCache? queryCache;
    private readonly PageCache? pageCache;
    private readonly Lock _walLock = new();  // ✅ C# 14: target-typed new
    private readonly ConcurrentDictionary<string, CachedQueryPlan> _preparedPlans = new();
    
    private readonly GroupCommitWAL? groupCommitWal;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    
    private bool _disposed;  // ✅ C# 14: No need for = false initialization

    /// <summary>
    /// Initializes a new instance of the <see cref="Database"/> class.
    /// </summary>
    public Database(IServiceProvider services, string dbPath, string masterPassword, bool isReadOnly = false, DatabaseConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);  // ✅ C# 14: Modern null checking
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        
        _dbPath = dbPath;
        this.isReadOnly = isReadOnly;
        this.config = config ?? DatabaseConfig.Default;
        Directory.CreateDirectory(_dbPath);
        
        var crypto = services.GetRequiredService<ICryptoService>();
        
        // SECURITY: Database-specific salt prevents rainbow table attacks
        var dbSalt = GetOrCreateDatabaseSalt(_dbPath);
        var masterKey = crypto.DeriveKey(masterPassword, dbSalt);
        
        // Initialize PageCache if enabled
        if (this.config.EnablePageCache)
        {
            pageCache = new(this.config.PageCacheCapacity, this.config.PageSize);  // ✅ C# 14: target-typed new
        }
        
        storage = new Services.Storage(crypto, masterKey, this.config, pageCache);
        userService = new UserService(crypto, storage, _dbPath);

        // Initialize query cache if enabled
        if (this.config.EnableQueryCache)
        {
            queryCache = new(this.config.QueryCacheSize);
        }

        // ✅ CRITICAL FIX: Load tables BEFORE initializing GroupCommitWAL
        // This ensures tables dictionary is populated before crash recovery ExecuteSQL calls
        Load();

        // Apply configuration-dependent per-table settings (e.g., compaction threshold)
        try
        {
            if (this.config is not null && this.config.ColumnarAutoCompactionThreshold != default)
            {
                foreach (var table in tables.Values)
                {
                    // Only relevant for columnar append-only tables
                    if (table is DataStructures.Table t)
                    {
                        t.SetCompactionThreshold(this.config.ColumnarAutoCompactionThreshold);
                    }
                }
            }
        }
        catch { /* non-fatal */ }

        // Initialize Group Commit WAL if enabled (AFTER Load)
        if (this.config is not null && this.config.UseGroupCommitWal && !isReadOnly)
        {
            groupCommitWal = new(
                _dbPath,
                this.config.WalDurabilityMode,
                this.config.WalMaxBatchSize,
                this.config.WalMaxBatchDelayMs,
                _instanceId);
                
            // Perform crash recovery (now safe - tables are loaded)
            var recoveredOps = groupCommitWal.CrashRecovery();
            if (recoveredOps.Count > 0)
            {
                foreach (var opData in recoveredOps)
                {
                    try
                    {
                        string sql = Encoding.UTF8.GetString(opData.Span);
                        ExecuteSQL(sql);
                    }
                    catch (Exception ex)
                    {
                        // Silently skip failed recovery operations
                        _ = ex;  // Suppress unused variable warning
                    }
                }
                
                groupCommitWal.ClearAsync().GetAwaiter().GetResult();
            }

            GroupCommitWAL.CleanupOrphanedWAL(_dbPath);
        }
    }

    private void Load()
    {
        var metaPath = Path.Combine(_dbPath, PersistenceConstants.MetaFileName);
        var metaJson = storage.Read(metaPath);
        
        if (metaJson is null) return;  // ✅ C# 14: pattern matching

        var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson);
        if (meta?.TryGetValue(PersistenceConstants.TablesKey, out var tablesObj) != true || tablesObj is null)
            return;

        var tablesObjString = tablesObj.ToString();
        if (string.IsNullOrEmpty(tablesObjString)) return;

        var tablesList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(tablesObjString);
        if (tablesList is null) return;

        foreach (var tableDict in tablesList)
        {
            var table = JsonSerializer.Deserialize<Table>(JsonSerializer.Serialize(tableDict));
            if (table is not null)  // ✅ C# 14: not pattern
            {
                table.SetStorage(storage);
                table.SetReadOnly(isReadOnly);
                tables[table.Name] = table;
            }
        }
    }

    private void SaveMetadata()
    {
        var tablesList = tables.Values.Select(t => new
        {
            t.Name,
            t.Columns,
            t.ColumnTypes,
            t.PrimaryKeyIndex,
            t.DataFile,
        }).ToList();
        
        var meta = new Dictionary<string, object> { [PersistenceConstants.TablesKey] = tablesList };
        storage.Write(Path.Combine(_dbPath, PersistenceConstants.MetaFileName), JsonSerializer.Serialize(meta));
    }

    private static bool IsSchemaChangingCommand(string sql) =>
        sql.TrimStart().ToUpperInvariant() is var upper &&
        (upper.StartsWith("CREATE ") || upper.StartsWith("ALTER ") || upper.StartsWith("DROP "));  // ✅ C# 14: expression-bodied + pattern

    private static string GetOrCreateDatabaseSalt(string dbPath)
    {
        var saltFilePath = Path.Combine(dbPath, ".salt");
        
        try
        {
            if (File.Exists(saltFilePath))
            {
                var saltBytes = File.ReadAllBytes(saltFilePath);
                
                if (saltBytes.Length == CryptoConstants.DATABASE_SALT_SIZE)
                    return Convert.ToBase64String(saltBytes);
            }
            
            var newSalt = new byte[CryptoConstants.DATABASE_SALT_SIZE];
            RandomNumberGenerator.Fill(newSalt);
            
            File.WriteAllBytes(saltFilePath, newSalt);
            File.SetAttributes(saltFilePath, FileAttributes.Hidden);
            
            return Convert.ToBase64String(newSalt);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create database salt file: {ex.Message}. Ensure directory is writable.", ex);
        }
    }

    /// <inheritdoc />
    public void CreateUser(string username, string password) => userService.CreateUser(username, password);

    /// <inheritdoc />
    public bool Login(string username, string password) => userService.Login(username, password);

    /// <inheritdoc />
    public IDatabase Initialize(string dbPath, string masterPassword) => this;  // ✅ Already initialized in constructor

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the database and releases all resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            groupCommitWal?.Dispose();
            pageCache?.Clear(false, null);
            queryCache?.Clear();
        }

        _disposed = true;
    }
}
