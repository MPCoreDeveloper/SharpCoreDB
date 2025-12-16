// <copyright file="AppendOnlyEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Engines;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Append-only storage engine that wraps the existing Storage implementation.
/// Optimized for high-throughput sequential writes with minimal overhead.
/// </summary>
public class AppendOnlyEngine : IStorageEngine
{
    private readonly IStorage storage;
    private readonly string databasePath;
    private readonly ConcurrentDictionary<string, string> tableFilePaths = new();
    
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
    /// Initializes a new instance of the <see cref="AppendOnlyEngine"/> class.
    /// </summary>
    /// <param name="storage">The underlying storage implementation.</param>
    /// <param name="databasePath">Path to the database directory.</param>
    public AppendOnlyEngine(IStorage storage, string databasePath)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        
        if (!Directory.Exists(databasePath))
        {
            Directory.CreateDirectory(databasePath);
        }
    }

    /// <inheritdoc />
    public StorageEngineType EngineType => StorageEngineType.AppendOnly;

    /// <inheritdoc />
    public long Insert(string tableName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        
        var sw = Stopwatch.StartNew();
        var filePath = GetTableFilePath(tableName);
        var offset = storage.AppendBytes(filePath, data);
        sw.Stop();
        
        Interlocked.Add(ref insertTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalInserts);
        Interlocked.Add(ref bytesWritten, data.Length);
        
        return offset;
    }

    /// <inheritdoc />
    public long[] InsertBatch(string tableName, List<byte[]> dataBlocks)
    {
        ArgumentNullException.ThrowIfNull(dataBlocks);
        
        if (dataBlocks.Count == 0)
            return Array.Empty<long>();
        
        var sw = Stopwatch.StartNew();
        var filePath = GetTableFilePath(tableName);
        var offsets = storage.AppendBytesMultiple(filePath, dataBlocks);
        sw.Stop();
        
        Interlocked.Add(ref insertTicks, sw.ElapsedTicks);
        Interlocked.Add(ref totalInserts, dataBlocks.Count);
        
        long batchBytes = 0;
        foreach (var block in dataBlocks)
        {
            batchBytes += block.Length;
        }
        Interlocked.Add(ref bytesWritten, batchBytes);
        
        return offsets;
    }

    /// <inheritdoc />
    public void Update(string tableName, long storageReference, byte[] newData)
    {
        ArgumentNullException.ThrowIfNull(newData);
        
        var sw = Stopwatch.StartNew();
        
        // Append-only storage doesn't support in-place updates
        // We append a new version and the old one becomes stale
        var filePath = GetTableFilePath(tableName);
        _ = storage.AppendBytes(filePath, newData);
        
        // Note: In a real implementation, you'd need to maintain a mapping
        // from logical record IDs to physical offsets, updating the mapping here.
        // For now, we just append and let the caller handle the indirection.
        
        sw.Stop();
        Interlocked.Add(ref updateTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalUpdates);
        Interlocked.Add(ref bytesWritten, newData.Length);
    }

    /// <inheritdoc />
    public void Delete(string tableName, long storageReference)
    {
        var sw = Stopwatch.StartNew();
        
        // Append-only storage doesn't support in-place deletes
        // Mark as deleted in index/metadata layer (not in this engine)
        // Physical space will be reclaimed during compaction
        
        sw.Stop();
        Interlocked.Add(ref deleteTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalDeletes);
    }

    /// <inheritdoc />
    public byte[]? Read(string tableName, long storageReference)
    {
        var sw = Stopwatch.StartNew();
        var filePath = GetTableFilePath(tableName);
        var data = storage.ReadBytesFrom(filePath, storageReference);
        sw.Stop();
        
        Interlocked.Add(ref readTicks, sw.ElapsedTicks);
        Interlocked.Increment(ref totalReads);
        
        if (data != null)
        {
            Interlocked.Add(ref bytesRead, data.Length);
        }
        
        return data;
    }

    /// <inheritdoc />
    public void BeginTransaction()
    {
        storage.BeginTransaction();
    }

    /// <inheritdoc />
    public async Task CommitAsync()
    {
        await storage.CommitAsync();
    }

    /// <inheritdoc />
    public void Rollback()
    {
        storage.Rollback();
    }

    /// <inheritdoc />
    public bool IsInTransaction => storage.IsInTransaction;

    /// <inheritdoc />
    public void Flush()
    {
        storage.FlushTransactionBuffer();
    }

    /// <inheritdoc />
    public StorageEngineMetrics GetMetrics()
    {
        var totalOps = totalInserts + totalUpdates + totalDeletes + totalReads;
        var ticksPerMicrosecond = (double)Stopwatch.Frequency / 1_000_000.0;
        
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
                ["EngineType"] = "AppendOnly",
                ["TotalOperations"] = totalOps,
                ["WriteAmplification"] = totalUpdates + totalDeletes > 0 
                    ? (double)(totalInserts + totalUpdates) / totalInserts 
                    : 1.0
            }
        };
    }

    /// <summary>
    /// Gets or creates the file path for a table.
    /// </summary>
    private string GetTableFilePath(string tableName)
    {
        return tableFilePaths.GetOrAdd(tableName, name => 
            Path.Combine(databasePath, $"{name}.data"));
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
            // Storage is managed externally, don't dispose it
        }
    }
}
