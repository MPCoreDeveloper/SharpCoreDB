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
using System.Threading.Tasks;

/// <summary>
/// Page-based storage engine with 8KB fixed-size pages and in-place updates.
/// Features:
/// - Fixed 8KB pages with typed headers and checksums
/// - Slot array for row storage (SQLite-style)
/// - Simple free list page allocator
/// - In-place UPDATE/DELETE without tombstones
/// - Optimized for OLTP workloads with frequent updates
/// </summary>
public class PageBasedEngine : IStorageEngine
{
    private readonly string databasePath;
    private readonly ConcurrentDictionary<string, PageManager> tableManagers = new();
    private readonly ConcurrentDictionary<string, uint> tableIds = new();
    private readonly object transactionLock = new();
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
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    public PageBasedEngine(string databasePath)
    {
        this.databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        
        if (!Directory.Exists(databasePath))
        {
            Directory.CreateDirectory(databasePath);
        }
    }

    /// <inheritdoc />
    public StorageEngineType EngineType => StorageEngineType.PageBased;

    /// <inheritdoc />
    public long Insert(string tableName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        
        var sw = Stopwatch.StartNew();
        var manager = GetOrCreatePageManager(tableName);
        var tableId = GetTableId(tableName);
        
        // Find page with sufficient space or allocate new one
        var pageId = manager.FindPageWithSpace(tableId, data.Length + 12 + 4); // data + header + slot
        
        // Insert record into page
        var recordId = manager.InsertRecord(pageId, data);
        
        // Encode storage reference: upper 48 bits = pageId, lower 16 bits = recordId
        var storageRef = EncodeStorageReference(pageId.Value, recordId.SlotIndex);
        
        sw.Stop();
        Interlocked.Add(ref insertTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalInserts);
        Interlocked.Add(ref bytesWritten, data.Length);
        
        return storageRef;
    }

    /// <inheritdoc />
    public long[] InsertBatch(string tableName, List<byte[]> dataBlocks)
    {
        ArgumentNullException.ThrowIfNull(dataBlocks);
        
        if (dataBlocks.Count == 0)
            return Array.Empty<long>();
        
        var sw = Stopwatch.StartNew();
        var results = new long[dataBlocks.Count];
        var manager = GetOrCreatePageManager(tableName);
        var tableId = GetTableId(tableName);
        
        for (int i = 0; i < dataBlocks.Count; i++)
        {
            var data = dataBlocks[i];
            var pageId = manager.FindPageWithSpace(tableId, data.Length + 12 + 4);
            var recordId = manager.InsertRecord(pageId, data);
            results[i] = EncodeStorageReference(pageId.Value, recordId.SlotIndex);
            
            Interlocked.Add(ref bytesWritten, data.Length);
        }
        
        sw.Stop();
        Interlocked.Add(ref insertTicks, sw.ElapsedTicks);
        Interlocked.Add(ref totalInserts, dataBlocks.Count);
        
        return results;
    }

    /// <inheritdoc />
    public void Update(string tableName, long storageReference, byte[] newData)
    {
        ArgumentNullException.ThrowIfNull(newData);
        
        var sw = Stopwatch.StartNew();
        var (pageId, recordId) = DecodeStorageReference(storageReference);
        var manager = GetOrCreatePageManager(tableName);
        
        // In-place update - PageManager handles space management
        manager.UpdateRecord(new PageManager.PageId(pageId), new PageManager.RecordId(recordId), newData);
        
        sw.Stop();
        Interlocked.Add(ref updateTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalUpdates);
        Interlocked.Add(ref bytesWritten, newData.Length);
    }

    /// <inheritdoc />
    public void Delete(string tableName, long storageReference)
    {
        var sw = Stopwatch.StartNew();
        var (pageId, recordId) = DecodeStorageReference(storageReference);
        var manager = GetOrCreatePageManager(tableName);
        
        // In-place delete - marks slot as deleted without tombstones
        manager.DeleteRecord(new PageManager.PageId(pageId), new PageManager.RecordId(recordId));
        
        sw.Stop();
        Interlocked.Add(ref deleteTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalDeletes);
    }

    /// <inheritdoc />
    public byte[]? Read(string tableName, long storageReference)
    {
        var sw = Stopwatch.StartNew();
        var (pageId, recordId) = DecodeStorageReference(storageReference);
        var manager = GetOrCreatePageManager(tableName);
        
        try
        {
            var data = manager.ReadRecord(new PageManager.PageId(pageId), new PageManager.RecordId(recordId));
            
            sw.Stop();
            Interlocked.Add(ref readTicks, sw.ElapsedTicks);
            Interlocked.Increment(ref totalReads);
            Interlocked.Add(ref bytesRead, data?.Length ?? 0);
            
            return data;
        }
        catch (InvalidOperationException)
        {
            // Record deleted
            sw.Stop();
            Interlocked.Add(ref readTicks, sw.ElapsedTicks);
            Interlocked.Increment(ref totalReads);
            return null;
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
            
            // Flush all dirty pages to disk
            foreach (var manager in tableManagers.Values)
            {
                manager.FlushDirtyPages();
            }
            
            transactionActions.Clear();
            isInTransaction = false;
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
    public StorageEngineMetrics GetMetrics()
    {
        var totalOps = totalInserts + totalUpdates + totalDeletes + totalReads;
        var ticksPerMicrosecond = (double)Stopwatch.Frequency / 1_000_000.0;
        
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
            AvgInsertTimeMicros = totalInserts > 0 
                ? (Interlocked.Read(ref insertTicks) / ticksPerMicrosecond / totalInserts) 
                : 0,
            AvgUpdateTimeMicros = totalUpdates > 0 
                ? (Interlocked.Read(ref updateTicks) / ticksPerMicrosecond / totalUpdates) 
                : 0,
            AvgDeleteTimeMicros = totalDeletes > 0 
                ? (Interlocked.Read(ref deleteTicks) / ticksPerMicrosecond / totalDeletes) 
                : 0,
            AvgReadTimeMicros = totalReads > 0 
                ? (Interlocked.Read(ref readTicks) / ticksPerMicrosecond / totalReads) 
                : 0,
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
            return new PageManager(databasePath, tableId);
        });
    }

    /// <summary>
    /// Gets or creates a unique table ID for the specified table name.
    /// </summary>
    private uint GetTableId(string tableName)
    {
        return tableIds.GetOrAdd(tableName, name =>
        {
            // Simple hash-based ID generation
            // Production would use a persistent counter
            return (uint)Math.Abs(name.GetHashCode());
        });
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
