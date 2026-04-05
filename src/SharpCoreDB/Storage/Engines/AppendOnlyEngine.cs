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
using System.Runtime.CompilerServices;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Insert(string tableName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var filePath = GetTableFilePath(tableName);
        var offset = storage.AppendBytes(filePath, data);

        Interlocked.Increment(ref totalInserts);
        Interlocked.Add(ref bytesWritten, data.Length);

        return offset;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long[] InsertBatch(string tableName, List<byte[]> dataBlocks)
    {
        ArgumentNullException.ThrowIfNull(dataBlocks);

        if (dataBlocks.Count == 0)
            return Array.Empty<long>();

        var filePath = GetTableFilePath(tableName);
        var offsets = storage.AppendBytesMultiple(filePath, dataBlocks);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(string tableName, long storageReference, byte[] newData)
    {
        ArgumentNullException.ThrowIfNull(newData);

        // Append-only: append new version, old ref becomes stale
        var filePath = GetTableFilePath(tableName);
        _ = storage.AppendBytes(filePath, newData);

        Interlocked.Increment(ref totalUpdates);
        Interlocked.Add(ref bytesWritten, newData.Length);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Delete(string tableName, long storageReference)
    {
        // Append-only: logical delete only (index layer handles removal)
        // Physical space reclaimed during compaction
        Interlocked.Increment(ref totalDeletes);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[]? Read(string tableName, long storageReference)
    {
        var filePath = GetTableFilePath(tableName);
        var data = storage.ReadBytesFrom(filePath, storageReference);

        Interlocked.Increment(ref totalReads);

        if (data != null)
        {
            Interlocked.Add(ref bytesRead, data.Length);
        }

        return data;
    }

    /// <inheritdoc />
    public IEnumerable<(long storageReference, byte[] data)> GetAllRecords(string tableName)
    {
        // AppendOnly engine doesn't maintain a record index
        // For full table scan, we need to read the entire file sequentially
        // This is a simplified implementation - production would need better record delimiting
        
        var filePath = GetTableFilePath(tableName);
        
        if (!File.Exists(filePath))
        {
            yield break;
        }
        
        // Read entire file
        var allData = storage.ReadBytes(filePath, noEncrypt: false);
        if (allData == null || allData.Length == 0)
        {
            yield break;
        }
        
        // Parse length-prefixed records
        long position = 0;
        while (position < allData.Length)
        {
            // Check if we have at least 4 bytes for length prefix
            if (position + 4 > allData.Length)
            {
                break;
            }
            
            // Read record length (4 bytes, little-endian)
            int recordLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                allData.AsSpan((int)position, 4));
            
            if (recordLength <= 0 || position + 4 + recordLength > allData.Length)
            {
                break; // Invalid or incomplete record
            }
            
            // Extract record data (skip 4-byte length prefix)
            var recordData = new byte[recordLength];
            Array.Copy(allData, position + 4, recordData, 0, recordLength);
            
            yield return (position, recordData);
            
            // Move to next record
            position += 4 + recordLength;
        }
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
    public bool SupportsDeltaUpdates => false; // Append-only doesn't support in-place delta updates

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
    /// ✅ FIXED: Use .dat extension to match PersistenceConstants.TableFileExtension
    /// This ensures data written by AppendOnly engine can be read by Table.Select()
    /// </summary>
    private string GetTableFilePath(string tableName)
    {
        return tableFilePaths.GetOrAdd(tableName, name => 
            Path.Combine(databasePath, $"{name}.dat"));  // ✅ Changed from .data to .dat
    }

    /// <summary>
    /// Compacts a table by removing deleted and stale versions.
    /// Only active rows (positions from the caller's active list) are kept.
    /// </summary>
    /// <param name="tableName">The table to compact.</param>
    /// <param name="activePositions">List of positions for active rows (from primary key index).</param>
    /// <returns>Number of bytes reclaimed.</returns>
    public long CompactTable(string tableName, List<long> activePositions)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(activePositions);
        
        var filePath = GetTableFilePath(tableName);
        
        if (!File.Exists(filePath))
        {
            return 0; // No file to compact
        }
        
        var originalSize = new FileInfo(filePath).Length;
        
        // Read entire file
        var allData = storage.ReadBytes(filePath, noEncrypt: false);
        if (allData == null || allData.Length == 0)
        {
            return 0;
        }
        
        // Create set for O(1) lookup
        var activeSet = new HashSet<long>(activePositions);
        
        // Collect active rows
        var activeRows = new List<byte[]>();
        var newPositions = new Dictionary<long, long>(); // old position -> new position
        
        long position = 0;
        long newPosition = 0;
        
        while (position < allData.Length)
        {
            if (position + 4 > allData.Length)
                break;
            
            int recordLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                allData.AsSpan((int)position, 4));
            
            if (recordLength <= 0 || position + 4 + recordLength > allData.Length)
                break;
            
            // Check if this position is active
            if (activeSet.Contains(position))
            {
                // Extract record data
                var recordData = new byte[recordLength];
                Array.Copy(allData, position + 4, recordData, 0, recordLength);
                activeRows.Add(recordData);
                
                // Track position mapping for index updates
                newPositions[position] = newPosition;
                newPosition += 4 + recordLength; // length prefix + data
            }
            
            position += 4 + recordLength;
        }
        
        // Write compacted data to temp file
        var tempPath = filePath + ".compact.tmp";
        
        try
        {
            // Delete temp file if it exists
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            
            // Write active rows to temp file
            if (activeRows.Count > 0)
            {
                storage.AppendBytesMultiple(tempPath, activeRows);
            }
            else
            {
                // Create empty file
                File.WriteAllBytes(tempPath, Array.Empty<byte>());
            }
            
            // Replace original file with compacted file
            File.Delete(filePath);
            File.Move(tempPath, filePath);
            
            var newSize = new FileInfo(filePath).Length;
            var bytesReclaimed = originalSize - newSize;
            
            return bytesReclaimed;
        }
        catch
        {
            // Cleanup temp file on error
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Ignore */ }
            }
            throw;
        }
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
