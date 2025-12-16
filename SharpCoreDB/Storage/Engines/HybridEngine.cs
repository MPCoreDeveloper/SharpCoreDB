// <copyright file="HybridEngine.cs" company="MPCoreDeveloper">
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
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Hybrid storage engine combining WAL append-only writes with page-based main storage.
/// Features:
/// - Fast writes via append-only WAL
/// - Efficient reads via page-based main file
/// - Periodic compaction merges WAL into main storage
/// - Best of both worlds for mixed OLTP workloads
/// </summary>
public class HybridEngine : IStorageEngine
{
    private readonly string databasePath;
    private readonly IStorage walStorage;
    private readonly ConcurrentDictionary<string, TableState> tableStates = new();
    private readonly object compactionLock = new();
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
    private long compactionCount;
    private long compactionTicks;

    // Compaction thresholds
    private const int WAL_COMPACTION_THRESHOLD = 1000; // Compact after 1000 WAL entries

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridEngine"/> class.
    /// </summary>
    /// <param name="walStorage">The WAL storage implementation.</param>
    /// <param name="databasePath">Path to the database directory.</param>
    public HybridEngine(IStorage walStorage, string databasePath)
    {
        this.walStorage = walStorage ?? throw new ArgumentNullException(nameof(walStorage));
        this.databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        
        if (!Directory.Exists(databasePath))
        {
            Directory.CreateDirectory(databasePath);
        }
    }

    /// <inheritdoc />
    public StorageEngineType EngineType => StorageEngineType.Hybrid;

    /// <inheritdoc />
    public long Insert(string tableName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        
        var sw = Stopwatch.StartNew();
        var state = GetOrCreateTableState(tableName);
        
        // Write to WAL (fast append-only write)
        var walOffset = walStorage.AppendBytes(state.WalPath, data);
        
        // Track in memory index
        long nextId = state.NextRecordId;
        var recordId = Interlocked.Increment(ref nextId);
        state.NextRecordId = nextId;
        state.WalIndex[recordId] = walOffset;
        state.WalEntryCount++;
        
        sw.Stop();
        Interlocked.Add(ref insertTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalInserts);
        Interlocked.Add(ref bytesWritten, data.Length);
        
        // Check if compaction needed
        if (state.WalEntryCount >= WAL_COMPACTION_THRESHOLD)
        {
            _ = Task.Run(() => TryCompactTable(tableName));
        }
        
        return recordId;
    }

    /// <inheritdoc />
    public long[] InsertBatch(string tableName, List<byte[]> dataBlocks)
    {
        ArgumentNullException.ThrowIfNull(dataBlocks);
        
        if (dataBlocks.Count == 0)
            return Array.Empty<long>();
        
        var sw = Stopwatch.StartNew();
        var state = GetOrCreateTableState(tableName);
        
        // Batch write to WAL
        var walOffsets = walStorage.AppendBytesMultiple(state.WalPath, dataBlocks);
        
        var recordIds = new long[dataBlocks.Count];
        for (int i = 0; i < dataBlocks.Count; i++)
        {
            long nextId = state.NextRecordId;
            var recordId = Interlocked.Increment(ref nextId);
            state.NextRecordId = nextId;
            state.WalIndex[recordId] = walOffsets[i];
            recordIds[i] = recordId;
            Interlocked.Add(ref bytesWritten, dataBlocks[i].Length);
        }
        
        state.WalEntryCount += dataBlocks.Count;
        
        sw.Stop();
        Interlocked.Add(ref insertTicks, sw.ElapsedTicks);
        Interlocked.Add(ref totalInserts, dataBlocks.Count);
        
        // Check if compaction needed
        if (state.WalEntryCount >= WAL_COMPACTION_THRESHOLD)
        {
            _ = Task.Run(() => TryCompactTable(tableName));
        }
        
        return recordIds;
    }

    /// <inheritdoc />
    public void Update(string tableName, long storageReference, byte[] newData)
    {
        ArgumentNullException.ThrowIfNull(newData);
        
        var sw = Stopwatch.StartNew();
        var state = GetOrCreateTableState(tableName);
        
        // Hybrid approach: If record is in WAL, update WAL entry; otherwise append to WAL
        var walOffset = walStorage.AppendBytes(state.WalPath, newData);
        state.WalIndex[storageReference] = walOffset;
        state.WalEntryCount++;
        
        sw.Stop();
        Interlocked.Add(ref updateTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalUpdates);
        Interlocked.Add(ref bytesWritten, newData.Length);
    }

    /// <inheritdoc />
    public void Delete(string tableName, long storageReference)
    {
        var sw = Stopwatch.StartNew();
        var state = GetOrCreateTableState(tableName);
        
        // Mark as deleted in index
        state.WalIndex.TryRemove(storageReference, out _);
        state.DeletedRecords.Add(storageReference);
        
        sw.Stop();
        Interlocked.Add(ref deleteTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalDeletes);
    }

    /// <inheritdoc />
    public byte[]? Read(string tableName, long storageReference)
    {
        var sw = Stopwatch.StartNew();
        var state = GetOrCreateTableState(tableName);
        
        // Check if deleted
        if (state.DeletedRecords.Contains(storageReference))
        {
            sw.Stop();
            Interlocked.Add(ref readTicks, sw.ElapsedTicks);
            Interlocked.Increment(ref totalReads);
            return null;
        }
        
        // Try WAL index first (fast path for recent data)
        if (state.WalIndex.TryGetValue(storageReference, out var walOffset))
        {
            var data = walStorage.ReadBytesFrom(state.WalPath, walOffset);
            sw.Stop();
            Interlocked.Add(ref readTicks, sw.ElapsedTicks);
            Interlocked.Increment(ref totalReads);
            
            if (data != null)
            {
                Interlocked.Add(ref bytesRead, data.Length);
            }
            
            return data;
        }
        
        // Try page-based storage (for compacted data)
        if (state.PageManager != null)
        {
            try
            {
                var (pageId, recordId) = DecodeStorageReference(storageReference);
                var data = state.PageManager.ReadRecord(new PageManager.PageId(pageId), new PageManager.RecordId(recordId));
                
                sw.Stop();
                Interlocked.Add(ref readTicks, sw.ElapsedTicks);
                Interlocked.Increment(ref totalReads);
                Interlocked.Add(ref bytesRead, data?.Length ?? 0);
                
                return data;
            }
            catch (InvalidOperationException)
            {
                // Record not found in pages
            }
        }
        
        sw.Stop();
        Interlocked.Add(ref readTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalReads);
        return null;
    }

    /// <inheritdoc />
    public void BeginTransaction()
    {
        walStorage.BeginTransaction();
        isInTransaction = true;
        transactionActions.Clear();
    }

    /// <inheritdoc />
    public async Task CommitAsync()
    {
        await walStorage.CommitAsync();
        isInTransaction = false;
        transactionActions.Clear();
    }

    /// <inheritdoc />
    public void Rollback()
    {
        walStorage.Rollback();
        isInTransaction = false;
        transactionActions.Clear();
    }

    /// <inheritdoc />
    public bool IsInTransaction => isInTransaction;

    /// <inheritdoc />
    public void Flush()
    {
        walStorage.FlushTransactionBuffer();
    }

    /// <inheritdoc />
    public StorageEngineMetrics GetMetrics()
    {
        var totalOps = totalInserts + totalUpdates + totalDeletes + totalReads;
        var ticksPerMicrosecond = (double)Stopwatch.Frequency / 1_000_000.0;
        
        // Calculate WAL and page statistics
        long totalWalEntries = 0;
        long totalPageRecords = 0;
        long totalWalSize = 0;
        long totalPageSize = 0;
        
        foreach (var state in tableStates.Values)
        {
            totalWalEntries += state.WalEntryCount;
            
            if (File.Exists(state.WalPath))
            {
                totalWalSize += new FileInfo(state.WalPath).Length;
            }
            
            if (state.PageManager != null && File.Exists(state.PagePath))
            {
                totalPageSize += new FileInfo(state.PagePath).Length;
            }
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
                ["EngineType"] = "Hybrid",
                ["TotalOperations"] = totalOps,
                ["WalEntries"] = totalWalEntries,
                ["PageRecords"] = totalPageRecords,
                ["WalSizeBytes"] = totalWalSize,
                ["PageSizeBytes"] = totalPageSize,
                ["TotalSizeBytes"] = totalWalSize + totalPageSize,
                ["CompactionCount"] = Interlocked.Read(ref compactionCount),
                ["AvgCompactionTimeMicros"] = compactionCount > 0 
                    ? (Interlocked.Read(ref compactionTicks) / ticksPerMicrosecond / compactionCount) 
                    : 0
            }
        };
    }

    /// <summary>
    /// Performs VACUUM operation by compacting all tables.
    /// </summary>
    public async Task<VacuumStats> VacuumAsync()
    {
        var sw = Stopwatch.StartNew();
        long totalReclaimed = 0;
        int tablesCompacted = 0;
        
        foreach (var tableName in tableStates.Keys.ToList())
        {
            var reclaimed = await CompactTable(tableName);
            totalReclaimed += reclaimed;
            if (reclaimed > 0) tablesCompacted++;
        }
        
        sw.Stop();
        
        return new VacuumStats
        {
            TablesCompacted = tablesCompacted,
            BytesReclaimed = totalReclaimed,
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    private TableState GetOrCreateTableState(string tableName)
    {
        return tableStates.GetOrAdd(tableName, name =>
        {
            var walPath = Path.Combine(databasePath, $"{name}.wal");
            var pagePath = Path.Combine(databasePath, $"{name}.pages");
            
            return new TableState
            {
                TableName = name,
                WalPath = walPath,
                PagePath = pagePath,
                WalIndex = new ConcurrentDictionary<long, long>(),
                DeletedRecords = new ConcurrentSet<long>(),
                NextRecordId = 0,
                WalEntryCount = 0
            };
        });
    }

    private void TryCompactTable(string tableName)
    {
        if (!Monitor.TryEnter(compactionLock))
        {
            return; // Another compaction already in progress
        }
        
        try
        {
            _ = CompactTable(tableName).GetAwaiter().GetResult();
        }
        finally
        {
            Monitor.Exit(compactionLock);
        }
    }

    private async Task<long> CompactTable(string tableName)
    {
        var state = GetOrCreateTableState(tableName);
        var sw = Stopwatch.StartNew();
        
        // Initialize page manager if needed
        if (state.PageManager == null)
        {
            var tableId = (uint)Math.Abs(tableName.GetHashCode());
            state.PageManager = new PageManager(databasePath, tableId);
        }
        
        // Read all WAL entries
        long reclaimedBytes = 0;
        var walEntries = new List<(long recordId, byte[] data)>();
        
        foreach (var kvp in state.WalIndex)
        {
            var data = walStorage.ReadBytesFrom(state.WalPath, kvp.Value);
            if (data != null && !state.DeletedRecords.Contains(kvp.Key))
            {
                walEntries.Add((kvp.Key, data));
            }
        }
        
        // Write to page-based storage
        foreach (var (recordId, data) in walEntries)
        {
            var pageId = state.PageManager.FindPageWithSpace((uint)state.TableName.GetHashCode(), data.Length + 16);
            var pageRecordId = state.PageManager.InsertRecord(pageId, data);
            
            // Remove from WAL index (now in page storage)
            state.WalIndex.TryRemove(recordId, out _);
        }
        
        // Clear WAL
        if (File.Exists(state.WalPath))
        {
            var fileInfo = new FileInfo(state.WalPath);
            reclaimedBytes = fileInfo.Length;
            File.Delete(state.WalPath);
        }
        
        state.WalEntryCount = 0;
        
        sw.Stop();
        Interlocked.Increment(ref compactionCount);
        Interlocked.Add(ref compactionTicks, sw.ElapsedTicks);
        
        await Task.CompletedTask;
        return reclaimedBytes;
    }

    private static long EncodeStorageReference(ulong pageId, ushort recordId)
    {
        return (long)((pageId << 16) | recordId);
    }

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
            foreach (var state in tableStates.Values)
            {
                state.PageManager?.Dispose();
            }
            
            tableStates.Clear();
        }
    }

    private sealed class TableState
    {
        public required string TableName { get; init; }
        public required string WalPath { get; init; }
        public required string PagePath { get; init; }
        public required ConcurrentDictionary<long, long> WalIndex { get; init; }
        public required ConcurrentSet<long> DeletedRecords { get; init; }
        public long NextRecordId { get; set; }
        public int WalEntryCount { get; set; }
        public PageManager? PageManager { get; set; }
    }

    private sealed class ConcurrentSet<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, byte> dict = new();
        
        public void Add(T item) => dict.TryAdd(item, 0);
        public bool Contains(T item) => dict.ContainsKey(item);
    }
}

/// <summary>
/// VACUUM operation statistics.
/// </summary>
public record VacuumStats
{
    /// <summary>Gets or initializes number of tables compacted.</summary>
    public required int TablesCompacted { get; init; }

    /// <summary>Gets or initializes bytes reclaimed from WAL.</summary>
    public required long BytesReclaimed { get; init; }

    /// <summary>Gets or initializes duration in milliseconds.</summary>
    public required long DurationMs { get; init; }
}
