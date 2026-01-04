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
    private readonly Dictionary<string, ITable> tables = [];  // ✅ C# 14: Collection expression
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
    
    private readonly GroupCommitWAL? groupCommitWal;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    
    private bool _disposed;  // ✅ C# 14: No explicit = false needed

    // Batch UPDATE transaction state
    private bool _batchUpdateActive;
    
    // Track if metadata needs to be flushed
    private bool _metadataDirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="Database"/> class.
    /// </summary>
    /// <param name="services">The service provider for dependency injection.</param>
    /// <param name="dbPath">The database directory path.</param>
    /// <param name="masterPassword">The master encryption password.</param>
    /// <param name="isReadOnly">Whether the database is readonly.</param>
    /// <param name="config">Optional database configuration.</param>
    public Database(IServiceProvider services, string dbPath, string masterPassword, bool isReadOnly = false, DatabaseConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);  // ✅ C# 14: Modern validation
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

        // Initialize Group Commit WAL if enabled (AFTER Load)
        if (this.config is not null && this.config.UseGroupCommitWal && !isReadOnly)  // ✅ C# 14: is not null
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
                    catch (Exception)
                    {
                        // Silently skip failed recovery operations
                    }
                }
                
                groupCommitWal.ClearAsync().GetAwaiter().GetResult();
            }

            GroupCommitWAL.CleanupOrphanedWAL(_dbPath);
        }
    }

    /// <summary>
    /// Loads database metadata from disk and initializes tables.
    /// </summary>
    private void Load()
    {
        var metaPath = Path.Combine(_dbPath, PersistenceConstants.MetaFileName);
        var metaExists = File.Exists(metaPath);
        
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[Load] Loading metadata from: {metaPath}");
        System.Diagnostics.Debug.WriteLine($"[Load] File exists: {metaExists}");
#endif
        
        var metaJson = storage.Read(metaPath);

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
    /// </summary>
    private void SaveMetadata()
    {
        var tablesList = tables.Values.Select(t => new
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
        }).ToList();
        
        var meta = new Dictionary<string, object> { [PersistenceConstants.TablesKey] = tablesList };
        storage.Write(Path.Combine(_dbPath, PersistenceConstants.MetaFileName), JsonSerializer.Serialize(meta));
        
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
        if (isReadOnly || !_metadataDirty)
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
            
            // ✅ CRITICAL: Flush all table data files BEFORE saving metadata
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
                    // Suppress unused variable warning
                    _ = ex;
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

            groupCommitWal?.Dispose();
            pageCache?.Clear(false, null);
            queryCache?.Clear();
        }

        _disposed = true;
    }
}
