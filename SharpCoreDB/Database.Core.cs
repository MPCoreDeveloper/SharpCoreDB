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

    // ✅ NEW: Batch UPDATE transaction state
    private bool _batchUpdateActive = false;
    
    // ✅ NEW: Track if metadata needs to be flushed
    private bool _metadataDirty = false;

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
        
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[Load] Loading metadata from: {metaPath}");
        System.Diagnostics.Debug.WriteLine($"[Load] File exists: {File.Exists(metaPath)}");
#endif
        
        var metaJson = storage.Read(metaPath);
        
        if (metaJson is null)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[Load] No metadata file found - new database");
#endif
            return;
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[Load] Metadata JSON length: {metaJson.Length}");
#endif

        var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson);
        if (meta?.TryGetValue(PersistenceConstants.TablesKey, out var tablesObj) != true || tablesObj is null)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[Load] No tables in metadata");
#endif
            return;
        }

        var tablesObjString = tablesObj.ToString();
        if (string.IsNullOrEmpty(tablesObjString))
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[Load] Empty tables object");
#endif
            return;
        }

        var tablesList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(tablesObjString);
        if (tablesList is null)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[Load] Failed to deserialize tables list");
#endif
            return;
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[Load] Loading {tablesList.Count} tables...");
#endif

        foreach (var tableDict in tablesList)
        {
            var table = JsonSerializer.Deserialize<Table>(JsonSerializer.Serialize(tableDict));
            if (table is not null)  // ✅ C# 14: not pattern
            {
                table.SetStorage(storage);
                table.SetReadOnly(isReadOnly);
                
                // ✅ CRITICAL FIX: Reinitialize Table after deserialization
                // JSON deserialization doesn't call constructor, so we need to ensure:
                // 1. IsAuto list has correct length (may be missing after deserialization)
                // 2. Column index cache is built
                // 3. Row count cache is refreshed
                // 4. PRIMARY KEY INDEX REBUILT! (most critical - without this, SELECT returns 0 rows!)
                
                // Ensure IsAuto list has same length as Columns
                while (table.IsAuto.Count < table.Columns.Count)
                {
                    table.IsAuto.Add(false); // Default to non-auto for missing entries
                }
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Load] Table {table.Name} reinitialized - Columns: {table.Columns.Count}, IsAuto: {table.IsAuto.Count}");
                System.Diagnostics.Debug.WriteLine($"[Load] PrimaryKeyIndex: {table.PrimaryKeyIndex}");
#endif
                
                // ✅ CRITICAL FIX: Rebuild Primary Key index after deserialization
                // The B-Tree index is NOT serialized, so it's empty after reload
                // Without this, all rows appear as "stale" during SELECT and 0 rows are returned!
                if (table.PrimaryKeyIndex >= 0)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[Load] Primary key column: {table.Columns[table.PrimaryKeyIndex]}");
                    System.Diagnostics.Debug.WriteLine($"[Load] Rebuilding Primary Key B-Tree index...");
#endif
                    
                    try
                    {
                        // Rebuild the index by scanning the data file
                        table.RebuildPrimaryKeyIndexFromDisk();
                        
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[Load] ✅ Primary Key index rebuilt successfully!");
#endif
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[Load] ⚠️  WARNING: Failed to rebuild PK index: {ex.Message}");
#endif
                        // Continue loading - table will work but without index optimization
                    }
                }
                
                tables[table.Name] = table;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Load] Loaded table: {table.Name}");
#endif
            }
        }
        
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[Load] Total tables loaded: {tables.Count}");
#endif
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
        
        // ✅ Reset dirty flag after successful save
        _metadataDirty = false;
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
    public void Flush()
    {
        if (isReadOnly)
            return; // No-op for readonly databases

        // ✅ OPTIMIZATION: Only save if metadata has changed
        if (!_metadataDirty)
            return;

        try
        {
            SaveMetadata();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to flush database changes: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void ForceSave()
    {
        if (isReadOnly)
            return;

        try
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[ForceSave] Saving metadata...");
            System.Diagnostics.Debug.WriteLine($"[ForceSave] Tables count: {tables.Count}");
            
            foreach (var table in tables.Values)
            {
                System.Diagnostics.Debug.WriteLine($"[ForceSave] Table: {table.Name}, DataFile: {table.DataFile}");
            }
#endif
            
            // ✅ CRITICAL FIX: Flush all table data files BEFORE saving metadata!
            // This ensures INSERT/UPDATE/DELETE data is persisted to disk
            foreach (var table in tables.Values)
            {
                try
                {
                    // Call Flush() on each table to persist data
                    table.Flush();
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ForceSave] Flushed table: {table.Name}");
#endif
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ForceSave] WARNING: Failed to flush table {table.Name}: {ex.Message}");
#endif
                    // Continue flushing other tables even if one fails
                }
            }
            
            SaveMetadata();
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[ForceSave] Metadata saved successfully!");
#endif
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[ForceSave] ERROR: {ex.Message}");
#endif
            throw new InvalidOperationException($"Failed to force save database changes: {ex.Message}", ex);
        }
    }

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
            // ✅ FIX: Save metadata before disposing to persist any table changes
            if (!isReadOnly)
            {
                try
                {
                    SaveMetadata();
                }
                catch (Exception ex)
                {
#if DEBUG
                    // Log but don't throw during dispose
                    System.Diagnostics.Debug.WriteLine($"[SharpCoreDB] Failed to save metadata during dispose: {ex.Message}");
#endif
                    // Suppress exception - dispose should not throw
                    _ = ex; // Avoid unused variable warning
                }
            }

            groupCommitWal?.Dispose();
            pageCache?.Clear(false, null);
            queryCache?.Clear();
        }

        _disposed = true;
    }
}
