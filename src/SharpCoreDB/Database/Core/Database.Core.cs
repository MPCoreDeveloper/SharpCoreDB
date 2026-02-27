// <copyright file="Database.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

// ✅ RELOCATED: This file was moved from root SharpCoreDB/ to Database/Core/ for better organization
// Original path: SharpCoreDB/Database.Core.cs
// New path: SharpCoreDB/Database/Core/Database.Core.cs
// Date: December 2025

namespace SharpCoreDB;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Core.Cache;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Engines;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Database implementation - Core partial class with fields and initialization.
/// Modern C# 14 with collection expressions, primary constructors, and async patterns.
/// 
/// Location: Database/Core/Database.Core.cs
/// Purpose: Core initialization, field declarations, Load/Save metadata, Dispose pattern
/// Dependencies: IStorage, IUserService, tables dictionary, caches
/// </summary>
public partial class Database : IDatabase, IDisposable
{
    private readonly IStorage storage;
    private readonly IUserService userService;
    private readonly Dictionary<string, ITable> tables = new(StringComparer.OrdinalIgnoreCase);  // Case-insensitive for SQL compatibility
    private readonly string _dbPath;
    
    /// <summary>
    /// Gets the database path.
    /// </summary>
    public string DbPath => _dbPath;
    
    private readonly bool isReadOnly;
    private readonly DatabaseConfig? config;
    private readonly QueryCache? queryCache;
    private readonly PageCache? pageCache;
    private readonly Lock _walLock = new();  // ✅ C# 14: Lock type + target-typed new
    private readonly ConcurrentDictionary<string, CachedQueryPlan> _preparedPlans = new();
    private QueryPlanCache? planCache;  // ✅ Lazy-initialized query plan cache
    private SqlParser? _sharedSqlParser;  // ✅ Reusable SqlParser for compiled queries
    
    // ✅ SCDB Phase 1: Storage provider abstraction
    // Null when using legacy directory-based storage (IStorage)
    // Non-null when using modern SCDB single-file storage
    private readonly IStorageProvider? _storageProvider;
    
    // ✅ UNIFIED: Replace GroupCommitWAL with IStorageEngine
    // This single abstraction handles all data persistence including WAL
    private readonly IStorageEngine? storageEngine;
    
    // ✅ OPTIONAL: GroupCommitWAL instance when UseGroupCommitWal is enabled
    private readonly Services.GroupCommitWAL? groupCommitWal;
    
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    
    private bool _disposed;  // ✅ C# 14: No explicit = false needed

    // Batch UPDATE transaction state
    private bool _batchUpdateActive;
    
    // Track if metadata needs to be flush
    private bool _metadataDirty;

    // ✅ NEW: Thread-safe last insert rowid tracking (SQLite compatibility)
    private readonly AsyncLocal<long> _lastInsertRowId = new();

    /// <inheritdoc />
    public long GetLastInsertRowId() => _lastInsertRowId.Value;

    /// <summary>
    /// Sets the last insert rowid (called by insert operations).
    /// Internal method - not part of public API.
    /// </summary>
    internal void SetLastInsertRowId(long rowId) => _lastInsertRowId.Value = rowId;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="Database"/> class.
    /// ✅ REFACTORED: Uses IStorageEngine abstraction instead of hardcoded GroupCommitWAL
    /// ✅ SCDB Phase 1: Optionally accepts IStorageProvider for modern SCDB storage
    /// </summary>
    /// <param name="services">The service provider for dependency injection.</param>
    /// <param name="dbPath">The database directory path.</param>
    /// <param name="masterPassword">The master encryption password.</param>
    /// <param name="isReadOnly">Whether the database is readonly.</param>
    /// <param name="config">Optional database configuration.</param>
    /// <param name="storageProvider">Optional storage provider (for SCDB mode). If null, uses legacy IStorage.</param>
    public Database(
        IServiceProvider services, 
        string dbPath, 
        string masterPassword, 
        bool isReadOnly = false, 
        DatabaseConfig? config = null,
        IStorageProvider? storageProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);  // ✅ C# 14: Modern validation
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        
        _dbPath = dbPath;
        this.isReadOnly = isReadOnly;
        this.config = config ?? DatabaseConfig.Default;
        _storageProvider = storageProvider;  // ✅ SCDB Phase 1: Store storage provider
        
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
        
        // ✅ UNIFIED: Create IStorage instance (underlying low-level storage with WAL)
        storage = new Services.Storage(crypto, masterKey, this.config, pageCache);
        userService = new UserService(crypto, storage, _dbPath);

        // Initialize query cache if enabled
        if (this.config.EnableQueryCache)
        {
            queryCache = new(this.config.QueryCacheSize);
        }

        // ✅ CRITICAL FIX: Load tables BEFORE initializing StorageEngine
        // This ensures tables dictionary is populated before any operations
        Load();

        // ✅ UNIFIED: Create unified IStorageEngine that handles ALL persistence
        // StorageEngineFactory selects the optimal engine based on config
        // This engine internally uses IStorage and handles WAL properly
        if (!isReadOnly)
        {
            storageEngine = StorageEngineFactory.CreateEngine(
                this.config.StorageEngineType,
                this.config,
                storage,
                _dbPath);
            
            // ✅ OPTIONAL: Create GroupCommitWAL instance if UseGroupCommitWal is enabled
            // This provides instance-specific WAL files for concurrent database instances
            if (this.config.UseGroupCommitWal)
            {
                groupCommitWal = new Services.GroupCommitWAL(
                    _dbPath,
                    this.config.WalDurabilityMode,
                    this.config.WalMaxBatchSize,
                    this.config.WalMaxBatchDelayMs,
                    _instanceId,
                    this.config.EnableAdaptiveWalBatching);
            }
        }
        
        // ✅ NOTE: Crash recovery is handled internally by each IStorageEngine implementation
        // AppendOnlyEngine and PageBasedEngine validate data integrity on startup
    }

    /// <summary>
    /// Loads database metadata from disk and initializes tables.
    /// ✅ SCDB Phase 1: Uses IStorageProvider when available, falls back to IStorage for legacy mode
    /// </summary>
    private void Load()
    {
        string? metaJson;
        bool metaExists;
        
        if (_storageProvider is not null)
        {
            // ✅ SCDB Phase 1: Use modern storage provider (block-based)
            metaExists = _storageProvider.BlockExists("sys:metadata");
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[Load] Loading metadata from storage provider");
            System.Diagnostics.Debug.WriteLine($"[Load] Block exists: {metaExists}");
#endif
            
            if (metaExists)
            {
                var metaBytes = _storageProvider.ReadBlockAsync("sys:metadata").GetAwaiter().GetResult();
                metaJson = metaBytes is not null ? System.Text.Encoding.UTF8.GetString(metaBytes) : null;
            }
            else
            {
                metaJson = null;
            }
        }
        else
        {
            // ✅ Legacy: Use IStorage (file-based)
            var metaPath = Path.Combine(_dbPath, PersistenceConstants.MetaFileName);
            metaExists = File.Exists(metaPath);
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[Load] Loading metadata from: {metaPath}");
            System.Diagnostics.Debug.WriteLine($"[Load] File exists: {metaExists}");
#endif
            
            metaJson = storage.Read(metaPath);
        }

        // ✅ CRITICAL: If metadata file exists but cannot be decrypted, abort
        if (metaExists && metaJson is null)  // ✅ C# 14: is null pattern
        {
            throw new InvalidOperationException(
                "Failed to decrypt database metadata. The master password may be incorrect or the database file is corrupted.");
        }
        
        if (metaJson is null)  // ✅ C# 14: is null pattern
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[Load] No metadata file found - new database");
#endif
            return;
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[Load] Metadata JSON length: {metaJson.Length}");
#endif

        Dictionary<string, object>? meta;
        try
        {
            meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to read database metadata. The master password may be incorrect or the metadata file is corrupted.", ex);
        }
        
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
            if (table is not null)  // ✅ C# 14: is not null pattern
            {
                table.SetStorage(storage);
                table.SetReadOnly(isReadOnly);
                
                // ✅ Phase 2: Set storage provider for delta-update optimization
                table.SetStorageProvider(_storageProvider);
                
                // ✅ NEW: Set database reference for last_insert_rowid() tracking
                table.SetDatabase(this);

                
                // ✅ CRITICAL FIX: Complete initialization of new DDL properties
                // Ensure lists have correct length
                while (table.IsAuto.Count < table.Columns.Count)
                {
                    table.IsAuto.Add(false);
                }
                while (table.IsNotNull.Count < table.Columns.Count)
                {
                    table.IsNotNull.Add(false);
                }
                while (table.DefaultValues.Count < table.Columns.Count)
                {
                    table.DefaultValues.Add(null);
                }
                // ✅ COLLATE Phase 1: Backward compatible — missing collations default to Binary
                while (table.ColumnCollations.Count < table.Columns.Count)
                {
                    table.ColumnCollations.Add(CollationType.Binary);
                }
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Load] Table {table.Name} reinitialized - Columns: {table.Columns.Count}, IsAuto: {table.IsAuto.Count}, IsNotNull: {table.IsNotNull.Count}");
                System.Diagnostics.Debug.WriteLine($"[Load] PrimaryKeyIndex: {table.PrimaryKeyIndex}");
#endif
                
                // ✅ CRITICAL FIX: Rebuild Primary Key index after deserialization
                if (table.PrimaryKeyIndex >= 0)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[Load] Primary key column: {table.Columns[table.PrimaryKeyIndex]}");
                    System.Diagnostics.Debug.WriteLine($"[Load] Rebuilding Primary Key B-Tree index...");
#endif
                    
                    try
                    {
                        table.RebuildPrimaryKeyIndexFromDisk();
                        
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("[Load] ✅ Primary Key index rebuilt successfully!");
#endif
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[Load] ⚠️  WARNING: Failed to rebuild PK index: {ex.Message}");
#endif
                        // Continue loading - table will work but without index optimization
                        // Suppress unused variable warning for release builds
                        _ = ex;
                    }
                }

                // ✅ AUTO INCREMENT: Initialize counters from existing data if not in metadata
                table.InitializeAutoIncrementCountersFromData();

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

    /// <summary>
    /// Saves database metadata to disk.
    /// ✅ SCDB Phase 1: Uses IStorageProvider when available, falls back to IStorage for legacy mode
    /// </summary>
    private void SaveMetadata()
    {
        var tablesList = tables.Values.OfType<Table>().Select(t => new
        {
            t.Name,
            t.Columns,
            t.ColumnTypes,
            t.PrimaryKeyIndex,
            t.DataFile,
            t.IsAuto,
            t.IsNotNull,
            t.DefaultValues,
            t.UniqueConstraints,
            t.ForeignKeys,  // Added for Phase 1.2
            t.ColumnCollations,  // ✅ COLLATE Phase 1: Persist per-column collation
            t.AutoIncrementCounters,  // ✅ AUTO INCREMENT: Persist counter state
        }).ToList();
        
        var meta = new Dictionary<string, object> { [PersistenceConstants.TablesKey] = tablesList };
        var metaJson = JsonSerializer.Serialize(meta);
        
        if (_storageProvider is not null)
        {
            // ✅ SCDB Phase 1: Use modern storage provider (block-based)
            var metaBytes = System.Text.Encoding.UTF8.GetBytes(metaJson);
            _storageProvider.WriteBlockAsync("sys:metadata", metaBytes).GetAwaiter().GetResult();
        }
        else
        {
            // ✅ Legacy: Use IStorage (file-based)
            storage.Write(Path.Combine(_dbPath, PersistenceConstants.MetaFileName), metaJson);
        }
        
        _metadataDirty = false;
    }

    /// <summary>
    /// Determines if a SQL command changes the database schema.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <returns>True if schema-changing command.</returns>
    private static bool IsSchemaChangingCommand(string sql) =>
        sql.TrimStart().ToUpperInvariant() is var upper &&
        (upper.StartsWith("CREATE ") || upper.StartsWith("ALTER ") || upper.StartsWith("DROP "));

    /// <summary>
    /// Gets or creates the database-specific salt for key derivation.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <returns>Base64-encoded salt string.</returns>
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
    public IDatabase Initialize(string dbPath, string masterPassword) => this;

    /// <inheritdoc />
    public void Flush()
    {
        if (isReadOnly)
            return;

        try
        {
            // ✅ CRITICAL: Flush WAL batch buffer FIRST
            // Rows 101-200 may still be queued in the batch buffer waiting for batch completion
            // Must flush them before storage engine
            FlushBatchWalBuffer();
            
            // ✅ CRITICAL: Flush BOTH storage engine AND all table data
            // Storage engine handles low-level persistence, but table data lives in memory
            // Must flush tables to disk before calling storageEngine.Flush()
            foreach (var table in tables.Values)
            {
                try
                {
                    table.Flush();  // Persist in-memory table data
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Flush] WARNING: Failed to flush table {table.Name}: {ex.Message}");
                    _ = ex;
                }
            }
            
            // ✅ SCDB Phase 1: Flush storage provider if using SCDB mode
            if (_storageProvider is not null)
            {
                _storageProvider.FlushAsync().GetAwaiter().GetResult();
            }
            
            // Then flush storage engine (handles any remaining WAL/transaction buffers)
            if (storageEngine is not null)
            {
                storageEngine.Flush();
            }
            
            SaveMetadata();
            _metadataDirty = false;
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
            System.Diagnostics.Debug.WriteLine("[ForceSave] Flushing storage engine...");
#endif
            
            // ✅ SCDB Phase 1: Flush storage provider if using SCDB mode
            if (_storageProvider is not null)
            {
                _storageProvider.FlushAsync().GetAwaiter().GetResult();
            }
            
            // ✅ UNIFIED: Delegate to IStorageEngine for guaranteed persistence
            // IStorageEngine.Flush() handles all engine types consistently
            if (storageEngine is not null)
            {
                storageEngine.Flush();
            }
            else
            {
                // Fallback: manually flush all tables
                foreach (var table in tables.Values)
                {
                    try
                    {
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
                        _ = ex;
                    }
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
            throw new InvalidOperationException($"Failed to force save database: {ex.Message}", ex);
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
            if (!isReadOnly)
            {
                try
                {
                    SaveMetadata();
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[SharpCoreDB] Failed to save metadata during dispose: {ex.Message}");
#endif
                    _ = ex;
                }
            }

            // ✅ SCDB Phase 1: Dispose storage provider if using SCDB mode
            _storageProvider?.Dispose();
            
            // ✅ UNIFIED: Dispose storage engine if available
            // This ensures all resources are released properly
            storageEngine?.Dispose();
            groupCommitWal?.Dispose();  // ✅ Dispose GroupCommitWAL if it was created
            pageCache?.Clear(false, null);
            queryCache?.Clear();
            ClearPlanCache();  // ✅ Clear query plan cache on disposal
        }

        _disposed = true;
    }

    /// <summary>
    /// Executes a SQL SELECT and returns zero-copy StructRow enumeration.
    /// Performance: avoids Dictionary allocations; 1.5–2x faster on full scans.
    /// Supports simple SELECT * FROM table [WHERE ...] without joins.
    /// </summary>
    [Obsolete("Limited SQL support (no ORDER BY, LIMIT, JOIN). Use ExecuteQuery(string, Dictionary<string, object?>?) via Database.Execution instead.")]
    public IEnumerable<DataStructures.StructRow> ExecuteQueryStruct(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL cannot be empty", nameof(sql));
        var upper = sql.Trim().ToUpperInvariant();
        if (!upper.StartsWith("SELECT ") || !upper.Contains(" FROM "))
            throw new NotSupportedException("ExecuteQueryStruct supports simple SELECT queries only");

        // Extract table name naively
        var fromIdx = upper.IndexOf(" FROM ");
        var afterFrom = sql.Substring(fromIdx + 6).Trim();
        var tableName = afterFrom.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0];

        if (!tables.TryGetValue(tableName, out var table) || table is not DataStructures.Table concrete)
            throw new InvalidOperationException($"Table '{tableName}' does not exist or cannot be scanned with StructRow");

        return concrete.ScanStructRows(enableCaching: false);
    }

    /// <summary>
    /// Backward compatible ExecuteQuery that routes simple SELECT * to StructRow for speed.
    /// </summary>
    [Obsolete("Limited SQL support (no ORDER BY, LIMIT, JOIN). Use ExecuteQuery(string, Dictionary<string, object?>?) from Database.Execution which routes through SqlParser for full SQL support.")]
    public List<Dictionary<string, object>> ExecuteQuery(string sql)
    {
        return ExecuteQuery(sql, parameters: null, noEncrypt: false);
    }
}
