// <copyright file="PageBasedEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Engines;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Text;

/// <summary>
/// Page-based storage engine with 8KB fixed-size pages and in-place updates.
/// Features:
/// - Fixed 8KB pages with typed headers and checksums
/// - Slot array for row storage (SQLite-style)
/// - Simple free list page allocator
/// - In-place UPDATE/DELETE without tombstones
/// - Optimized for OLTP workloads with frequent updates
/// </summary>
public partial class PageBasedEngine : IStorageEngine
{
    private readonly string databasePath;
    private readonly DatabaseConfig? config; // ✅ NEW: Store config for PageManager
    private readonly ConcurrentDictionary<string, PageManager> tableManagers = new();
    private readonly ConcurrentDictionary<string, uint> tableIds = new();
    private readonly Lock transactionLock = new();
    private bool isInTransaction;
    private readonly List<Action> transactionActions = new();
    
    // Performance metrics
    private long totalInserts;
    private long totalUpdates;
    private long totalDeletes;
    private long totalReads;
    private long bytesWritten;
    private long bytesRead;
    private long insertTicks;
    private long updateTicks;
    private long deleteTicks;
    private long readTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageBasedEngine"/> class.
    /// ✅ NEW: Accepts DatabaseConfig for auto-configuration!
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <param name="config">Optional database configuration for auto-tuning.</param>
    public PageBasedEngine(string databasePath, DatabaseConfig? config = null)
    {
        this.databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        this.config = config; // ✅ NEW: Store config
        
        if (!Directory.Exists(databasePath))
        {
            Directory.CreateDirectory(databasePath);
        }
    }

    /// <inheritdoc />
    public StorageEngineType EngineType => StorageEngineType.PageBased;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Insert(string tableName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // PERF: Removed Stopwatch per-op overhead (~50-100ns per call)
        var tableId = GetTableId(tableName);
        var manager = GetOrCreatePageManager(tableName);
        var pageId = manager.FindPageWithSpace(tableId, data.Length);
        var recordId = manager.InsertRecord(pageId, data);

        Interlocked.Increment(ref totalInserts);
        Interlocked.Add(ref bytesWritten, data.Length);

        return EncodeStorageReference(pageId.Value, recordId.SlotIndex);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long[] InsertBatch(string tableName, List<byte[]> dataBlocks)
    {
        ArgumentNullException.ThrowIfNull(dataBlocks);

        if (dataBlocks.Count == 0)
            return Array.Empty<long>();

        #if DEBUG
        Console.WriteLine($"[PageBasedEngine.InsertBatch] Inserting {dataBlocks.Count} records into table {tableName}");
        #endif

        var results = new long[dataBlocks.Count];
        var manager = GetOrCreatePageManager(tableName);
        var tableId = GetTableId(tableName);
        long totalBytes = 0;

        // PERF: Track current page to avoid repeated FindPageWithSpace calls
        var currentPageId = PageManager.PageId.Invalid;

        for (int i = 0; i < dataBlocks.Count; i++)
        {
            var data = dataBlocks[i];
            var requiredSpace = data.Length + 16;

            // Reuse current page if it has space, otherwise find a new one
            if (!currentPageId.IsValid || currentPageId == PageManager.PageId.Invalid)
            {
                currentPageId = manager.FindPageWithSpace(tableId, requiredSpace);
            }
            else
            {
                // Try to keep using the same page
                try
                {
                    var recordId = manager.InsertRecord(currentPageId, data);
                    results[i] = EncodeStorageReference(currentPageId.Value, recordId.SlotIndex);
                    totalBytes += data.Length;
                    continue;
                }
                catch (InvalidOperationException)
                {
                    // Page full, find new page
                    currentPageId = manager.FindPageWithSpace(tableId, requiredSpace);
                }
            }

            var rid = manager.InsertRecord(currentPageId, data);
            results[i] = EncodeStorageReference(currentPageId.Value, rid.SlotIndex);
            totalBytes += data.Length;
        }

        Interlocked.Add(ref totalInserts, dataBlocks.Count);
        Interlocked.Add(ref bytesWritten, totalBytes);

        #if DEBUG
        Console.WriteLine($"[PageBasedEngine.InsertBatch] Inserted {dataBlocks.Count} records successfully");

        var (hits, misses, hitRate, size, _) = manager.GetCacheStats();
        Console.WriteLine($"[PageBasedEngine.InsertBatch] Cache stats - Size: {size}, Hits: {hits}, Misses: {misses}, HitRate: {hitRate:P2}");
        #endif

        return results;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(string tableName, long storageReference, byte[] newData)
    {
        ArgumentNullException.ThrowIfNull(newData);

        var manager = GetOrCreatePageManager(tableName);
        var (pageId, recordId) = DecodeStorageReference(storageReference);
        manager.UpdateRecord(new PageManager.PageId(pageId), new PageManager.RecordId(recordId), newData);

        Interlocked.Increment(ref totalUpdates);
        Interlocked.Add(ref bytesWritten, newData.Length);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Delete(string tableName, long storageReference)
    {
        var (pageId, recordId) = DecodeStorageReference(storageReference);
        var manager = GetOrCreatePageManager(tableName);

        manager.DeleteRecord(new PageManager.PageId(pageId), new PageManager.RecordId(recordId));

        Interlocked.Increment(ref totalDeletes);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[]? Read(string tableName, long storageReference)
    {
        var (pageId, recordId) = DecodeStorageReference(storageReference);
        var manager = GetOrCreatePageManager(tableName);

        bool success = manager.TryReadRecord(
            new PageManager.PageId(pageId), 
            new PageManager.RecordId(recordId), 
            out var data);

        if (success && data != null)
        {
            Interlocked.Increment(ref totalReads);
            Interlocked.Add(ref bytesRead, data.Length);
            return data;
        }

        return null;
    }

    /// <inheritdoc />
    public IEnumerable<(long storageReference, byte[] data)> GetAllRecords(string tableName)
    {
        var tableId = GetTableId(tableName);
        var manager = GetOrCreatePageManager(tableName);
        
        foreach (var pageId in manager.GetAllTablePages(tableId))
        {
            foreach (var recordId in manager.GetAllRecordsInPage(pageId))
            {
                var storageRef = EncodeStorageReference(pageId.Value, recordId.SlotIndex);
                
                // ✅ FIXED: Yield raw binary directly - NO Base64 decoding overhead!
                // Data is already in optimal binary format from serialization layer
                if (manager.TryReadRecord(pageId, recordId, out var data) && data != null)
                {
                    yield return (storageRef, data);
                }
            }
        }
    }

    /// <inheritdoc />
    public void BeginTransaction()
    {
        lock (transactionLock)
        {
            if (isInTransaction)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }
            
            isInTransaction = true;
            transactionActions.Clear();
        }
    }

    /// <inheritdoc />
    public async Task CommitAsync()
    {
        lock (transactionLock)
        {
            if (!isInTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }
            
            #if DEBUG
            Console.WriteLine($"[PageBasedEngine.CommitAsync] Committing transaction for {tableManagers.Count} tables");
            #endif
            
            // Flush all dirty pages to disk
            foreach (var (tableName, manager) in tableManagers)
            {
                #if DEBUG
                Console.WriteLine($"[PageBasedEngine.CommitAsync] Flushing dirty pages for table: {tableName}");
                var (hits, misses, hitRate, size, _) = manager.GetCacheStats();
                Console.WriteLine($"[PageBasedEngine.CommitAsync] Cache stats before flush - Size: {size}, Hits: {hits}, Misses: {misses}, HitRate: {hitRate:P2}");
                #endif
                
                manager.FlushDirtyPages();
                
                #if DEBUG
                Console.WriteLine($"[PageBasedEngine.CommitAsync] Flushed dirty pages for table: {tableName}");
                #endif
            }
            
            transactionActions.Clear();
            isInTransaction = false;
            
            #if DEBUG
            Console.WriteLine("[PageBasedEngine.CommitAsync] Transaction committed successfully");
            #endif
        }
        
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Rollback()
    {
        lock (transactionLock)
        {
            if (!isInTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }
            
            // For simplicity, we don't support rollback in this POC
            // A production implementation would use WAL or shadow paging
            transactionActions.Clear();
            isInTransaction = false;
        }
    }

    /// <inheritdoc />
    public bool IsInTransaction
    {
        get
        {
            lock (transactionLock)
            {
                return isInTransaction;
            }
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        foreach (var manager in tableManagers.Values)
        {
            manager.FlushDirtyPages();
        }
    }

    /// <inheritdoc />
    public bool SupportsDeltaUpdates => config?.EnableDeltaUpdates ?? false;

    /// <inheritdoc />
    public StorageEngineMetrics GetMetrics()
    {
        var totalOps = totalInserts + totalUpdates + totalDeletes + totalReads;
        
        // Calculate page statistics
        long totalPages = 0;
        long totalFreeSpace = 0;
        int totalCompactionCandidates = 0;
        
        foreach (var manager in tableManagers.Values)
        {
            // Note: This is a simplified metric calculation
            // A production implementation would track these metrics more efficiently
            totalPages++;
        }
        
        return new StorageEngineMetrics
        {
            TotalInserts = Interlocked.Read(ref totalInserts),
            TotalUpdates = Interlocked.Read(ref totalUpdates),
            TotalDeletes = Interlocked.Read(ref totalDeletes),
            TotalReads = Interlocked.Read(ref totalReads),
            BytesWritten = Interlocked.Read(ref bytesWritten),
            BytesRead = Interlocked.Read(ref bytesRead),
            AvgInsertTimeMicros = 0,
            AvgUpdateTimeMicros = 0,
            AvgDeleteTimeMicros = 0,
            AvgReadTimeMicros = 0,
            CustomMetrics = new Dictionary<string, object>
            {
                ["EngineType"] = "PageBased",
                ["TotalOperations"] = totalOps,
                ["TotalPages"] = totalPages,
                ["AvgFreeSpacePerPage"] = totalPages > 0 ? totalFreeSpace / totalPages : 0,
                ["CompactionCandidates"] = totalCompactionCandidates,
                ["UpdatesInPlace"] = totalUpdates, // All updates are in-place in this engine
                ["WriteAmplification"] = 1.0 // Page-based has ~1.0 write amplification for updates
            }
        };
    }

    /// <summary>
    /// Gets or creates a PageManager for the specified table.
    /// </summary>
    private PageManager GetOrCreatePageManager(string tableName)
    {
        return tableManagers.GetOrAdd(tableName, name =>
        {
            var tableId = GetTableId(name);
            return new PageManager(databasePath, tableId, config); // ✅ Pass config!
        });
    }

    /// <summary>
    /// Gets or creates a unique table ID for the specified table name.
    /// Uses a deterministic hash so table page files are stable across process restarts.
    /// Falls back to legacy hash-based file naming when legacy files already exist.
    /// </summary>
    private uint GetTableId(string tableName)
    {
        return tableIds.GetOrAdd(tableName, ResolveTableId);
    }

    /// <summary>
    /// Resolves table ID using deterministic hashing with backward compatibility.
    /// </summary>
    private uint ResolveTableId(string tableName)
    {
        var stableTableId = ComputeStableTableId(tableName);
        var stablePath = Path.Combine(databasePath, $"table_{stableTableId}.pages");
        if (File.Exists(stablePath))
        {
            return stableTableId;
        }

        var legacyTableId = unchecked((uint)tableName.GetHashCode());
        var legacyPath = Path.Combine(databasePath, $"table_{legacyTableId}.pages");
        if (File.Exists(legacyPath))
        {
            return legacyTableId;
        }

        return stableTableId;
    }

    /// <summary>
    /// Computes a deterministic 32-bit FNV-1a hash for table IDs.
    /// </summary>
    private static uint ComputeStableTableId(string tableName)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;

        var normalized = tableName.ToUpperInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);

        uint hash = fnvOffset;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= fnvPrime;
        }

        return hash == 0 ? 1u : hash;
    }

    /// <summary>
    /// Encodes a page ID and record ID into a single 64-bit storage reference.
    /// Format: [48 bits: pageId][16 bits: recordId]
    /// </summary>
    private static long EncodeStorageReference(ulong pageId, ushort recordId)
    {
        return (long)((pageId << 16) | recordId);
    }

    /// <summary>
    /// Decodes a storage reference into page ID and record ID.
    /// </summary>
    private static (ulong pageId, ushort recordId) DecodeStorageReference(long storageReference)
    {
        var pageId = (ulong)storageReference >> 16;
        var recordId = (ushort)(storageReference & 0xFFFF);
        return (pageId, recordId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var manager in tableManagers.Values)
            {
                manager.Dispose();
            }
            
            tableManagers.Clear();
        }
    }
}
