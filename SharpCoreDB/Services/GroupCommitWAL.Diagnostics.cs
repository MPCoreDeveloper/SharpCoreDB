// <copyright file="GroupCommitWAL.Diagnostics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// GroupCommitWAL - Diagnostics and utility methods.
/// Contains statistics, crash recovery, and helper functions.
/// Part of the GroupCommitWAL partial class.
/// See also: GroupCommitWAL.Core.cs, GroupCommitWAL.Batching.cs
/// </summary>
public partial class GroupCommitWAL
{
    /// <summary>
    /// Gets statistics about WAL performance.
    /// </summary>
    /// <returns>Tuple of (total commits, total batches, average batch size, total bytes written).</returns>
    public (long TotalCommits, long TotalBatches, double AverageBatchSize, long TotalBytesWritten) GetStatistics()
    {
        long commits = Interlocked.Read(ref totalCommits);
        long batches = Interlocked.Read(ref totalBatches);
        long bytes = Interlocked.Read(ref totalBytesWritten);

        double avgBatchSize = batches > 0 ? (double)commits / batches : 0;

        return (commits, batches, avgBatchSize, bytes);
    }

    /// <summary>
    /// Gets adaptive batching statistics.
    /// </summary>
    /// <returns>Tuple of (current size, adjustments, enabled).</returns>
    public (int CurrentSize, long Adjustments, bool Enabled) GetAdaptiveBatchStatistics()
    {
        return (currentBatchSize, Interlocked.Read(ref totalBatchAdjustments), enableAdaptiveBatching);
    }

    /// <summary>
    /// Gets the current dynamic batch size.
    /// </summary>
    /// <returns>Current batch size (may be different from initial if adaptive batching is enabled).</returns>
    public int GetCurrentBatchSize()
    {
        return currentBatchSize;
    }

    /// <summary>
    /// Performs crash recovery by replaying the WAL from the beginning.
    /// Returns all successfully committed records in order.
    /// Uses streaming (64KB chunks) instead of loading entire file into memory.
    /// </summary>
    /// <returns>List of recovered data records.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<ReadOnlyMemory<byte>> CrashRecovery()
    {
        var records = new List<ReadOnlyMemory<byte>>();

        if (!File.Exists(logPath))
        {
            return records; // No WAL file, nothing to recover
        }

        const int CHUNK_SIZE = 64 * 1024; // 64KB chunks for streaming
        byte[]? buffer = null;
        byte[] carryover = Array.Empty<byte>(); // For partial records across chunks
        
        try
        {
            buffer = pool.Rent(CHUNK_SIZE);
            
            // Read WAL file in chunks (allow write sharing for active fileStream)
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CHUNK_SIZE);
            
            int bytesRead;
            while ((bytesRead = fs.Read(buffer, 0, CHUNK_SIZE)) > 0)
            {
                // Combine carryover from previous chunk with new data
                byte[] combined;
                if (carryover.Length > 0)
                {
                    combined = new byte[carryover.Length + bytesRead];
                    Array.Copy(carryover, 0, combined, 0, carryover.Length);
                    Array.Copy(buffer, 0, combined, carryover.Length, bytesRead);
                }
                else
                {
                    combined = new byte[bytesRead];
                    Array.Copy(buffer, 0, combined, 0, bytesRead);
                }
                
                var span = combined.AsSpan();
                int offset = 0;
                
                // Parse records from chunk
                while (offset < span.Length)
                {
                    if (WalRecord.TryReadFrom(span[offset..], out var record, out int consumed))
                    {
                        // Valid record - add to recovery list
                        records.Add(record.Data);
                        offset += consumed;
                    }
                    else
                    {
                        // Incomplete record - save as carryover for next chunk
                        // OR corrupted record at end of file - stop recovery
                        if (fs.Position < fs.Length)
                        {
                            // More data to read - save carryover
                            carryover = span[offset..].ToArray();
                        }
                        break; // Exit inner loop
                    }
                }
                
                // If we parsed all data, clear carryover
                if (offset >= span.Length)
                {
                    carryover = Array.Empty<byte>();
                }
            }
        }
        finally
        {
            if (buffer != null)
            {
                pool.Return(buffer, clearArray: false);
            }
        }

        return records;
    }

    /// <summary>
    /// Recovers data from ALL WAL files in the specified database path.
    /// Useful for production scenarios where multiple instances may have created WAL files.
    /// </summary>
    /// <param name="dbPath">The database path to search for WAL files.</param>
    /// <returns>List of all recovered data records from all WAL files.</returns>
    public static List<ReadOnlyMemory<byte>> RecoverAll(string dbPath)
    {
        var allRecords = new List<ReadOnlyMemory<byte>>();
        
        if (!Directory.Exists(dbPath))
        {
            return allRecords;
        }

        // Find all instance-specific WAL files
        var walFiles = Directory.GetFiles(dbPath, "wal-*.log");
        
        foreach (var walFile in walFiles)
        {
            try
            {
                // Read each WAL file
                var records = ReadWalFile(walFile);
                allRecords.AddRange(records);
            }
            catch
            {
                // Skip corrupted WAL files
            }
        }
        
        return allRecords;
    }

    /// <summary>
    /// Reads and parses a WAL file, returning all valid records.
    /// Uses streaming (64KB chunks) instead of loading entire file.
    /// </summary>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <returns>List of recovered data records.</returns>
    private static List<ReadOnlyMemory<byte>> ReadWalFile(string walFilePath)
    {
        var records = new List<ReadOnlyMemory<byte>>();
        var pool = ArrayPool<byte>.Shared;
        const int CHUNK_SIZE = 64 * 1024; // 64KB chunks
        byte[]? buffer = null;
        byte[] carryover = Array.Empty<byte>();

        try
        {
            buffer = pool.Rent(CHUNK_SIZE);
            
            // Allow write sharing in case file is actively being written
            using var fs = new FileStream(walFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CHUNK_SIZE);
            
            int bytesRead;
            while ((bytesRead = fs.Read(buffer, 0, CHUNK_SIZE)) > 0)
            {
                // Combine carryover with new data
                byte[] combined;
                if (carryover.Length > 0)
                {
                    combined = new byte[carryover.Length + bytesRead];
                    Array.Copy(carryover, 0, combined, 0, carryover.Length);
                    Array.Copy(buffer, 0, combined, carryover.Length, bytesRead);
                }
                else
                {
                    combined = new byte[bytesRead];
                    Array.Copy(buffer, 0, combined, 0, bytesRead);
                }
                
                var span = combined.AsSpan();
                int offset = 0;
                
                while (offset < span.Length)
                {
                    if (WalRecord.TryReadFrom(span[offset..], out var record, out int consumed))
                    {
                        records.Add(record.Data);
                        offset += consumed;
                    }
                    else
                    {
                        // Incomplete or corrupted record
                        if (fs.Position < fs.Length)
                        {
                            carryover = span[offset..].ToArray();
                        }
                        break; // Stop on first corrupted record or save carryover
                    }
                }
                
                if (offset >= span.Length)
                {
                    carryover = Array.Empty<byte>();
                }
            }
        }
        finally
        {
            if (buffer != null)
            {
                pool.Return(buffer, clearArray: false);
            }
        }

        return records;
    }

    /// <summary>
    /// Cleans up orphaned WAL files that are older than the specified age.
    /// Orphaned files may be left behind if a database instance crashes.
    /// </summary>
    /// <param name="dbPath">The database path to clean.</param>
    /// <param name="maxAge">Maximum age of files to keep (default 1 hour).</param>
    /// <returns>Number of files deleted.</returns>
    public static int CleanupOrphanedWAL(string dbPath, TimeSpan? maxAge = null)
    {
        int deletedCount = 0;
        var cutoff = DateTime.Now - (maxAge ?? TimeSpan.FromHours(1));

        if (!Directory.Exists(dbPath))
        {
            return 0;
        }

        var walFiles = Directory.GetFiles(dbPath, "wal-*.log");
        
        foreach (var walFile in walFiles)
        {
            try
            {
                var info = new FileInfo(walFile);
                
                // Delete if older than cutoff and not currently in use
                if (info.LastWriteTime < cutoff)
                {
                    File.Delete(walFile);
                    deletedCount++;
                }
            }
            catch
            {
                // Skip if file is in use or can't be deleted
            }
        }
        
        return deletedCount;
    }

    /// <summary>
    /// Calculates optimal batch size based on data volume.
    /// Used for bulk insert operations to determine grouping strategy.
    /// </summary>
    /// <param name="totalRows">Total number of rows to insert.</param>
    /// <returns>Recommended batch size for this operation.</returns>
    public static int GetDynamicBatchSize(int totalRows)
    {
        return totalRows switch
        {
            < 100 => 10,                      // Small: 10 rows per batch
            < 1_000 => 100,                   // Medium: 100 rows per batch
            < 10_000 => 1_000,                // Large: 1K rows per batch
            < 100_000 => 5_000,               // Very large: 5K rows per batch
            _ => 10_000                       // Extreme: 10K rows per batch (max)
        };
    }

    /// <summary>
    /// Calculates optimal WAL buffer size based on data volume.
    /// Used to dynamically adjust buffer size for bulk operations.
    /// </summary>
    /// <param name="totalRows">Total number of rows to insert.</param>
    /// <param name="avgRowSize">Average row size in bytes (default 1KB).</param>
    /// <returns>Recommended WAL buffer size in bytes.</returns>
    public static int GetDynamicWalBufferSize(int totalRows, int avgRowSize = 1024)
    {
        long estimatedDataSize = (long)totalRows * avgRowSize;
        
        return estimatedDataSize switch
        {
            < 1 * 1024 * 1024 => 1 * 1024 * 1024,      // < 1MB data: 1MB buffer
            < 10 * 1024 * 1024 => 4 * 1024 * 1024,     // < 10MB data: 4MB buffer
            < 100 * 1024 * 1024 => 16 * 1024 * 1024,   // < 100MB data: 16MB buffer
            _ => 64 * 1024 * 1024                      // ≥ 100MB data: 64MB buffer (max)
        };
    }
}
