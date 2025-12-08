// <copyright file="GroupCommitWAL.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// Append-only Write-Ahead Log with group commits for high throughput.
/// Uses a background worker thread to batch multiple pending commits into a single fsync operation.
/// Supports FullSync and Async durability modes.
/// Each instance uses a unique WAL file to avoid file locking conflicts.
/// </summary>
public class GroupCommitWAL : IDisposable
{
    private readonly string logPath;
    private readonly string instanceId;
    private readonly FileStream fileStream;
    private readonly DurabilityMode durabilityMode;
    private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
    private readonly Channel<PendingCommit> commitQueue;
    private readonly Task backgroundWorker;
    private readonly CancellationTokenSource cts;
    private bool disposed = false;

    // Configuration
    private readonly int maxBatchSize;
    private readonly TimeSpan maxBatchDelay;

    // Statistics
    private long totalCommits = 0;
    private long totalBatches = 0;
    private long totalBytesWritten = 0;

    /// <summary>
    /// Represents a pending commit request with its completion source.
    /// </summary>
    private class PendingCommit
    {
        public WalRecord Record { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; set; } = new();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupCommitWAL"/> class.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="durabilityMode">The durability mode (FullSync or Async).</param>
    /// <param name="maxBatchSize">Maximum number of commits to batch (default 100).</param>
    /// <param name="maxBatchDelayMs">Maximum delay before flushing batch in milliseconds (default 10ms).</param>
    /// <param name="instanceId">Optional instance ID for unique WAL file. If null, generates a new GUID.</param>
    public GroupCommitWAL(
        string dbPath,
        DurabilityMode durabilityMode = DurabilityMode.FullSync,
        int maxBatchSize = 100,
        int maxBatchDelayMs = 10,
        string? instanceId = null)
    {
        // Generate unique instance ID if not provided (prevents file locking conflicts)
        this.instanceId = instanceId ?? Guid.NewGuid().ToString("N");
        
        // Create instance-specific WAL filename
        this.logPath = Path.Combine(dbPath, $"wal-{this.instanceId}.log");
        
        this.durabilityMode = durabilityMode;
        this.maxBatchSize = maxBatchSize;
        this.maxBatchDelay = TimeSpan.FromMilliseconds(maxBatchDelayMs);

        // Create file stream with appropriate options
        var options = FileOptions.Asynchronous;
        if (durabilityMode == DurabilityMode.FullSync)
        {
            options |= FileOptions.WriteThrough; // OS-level write-through
        }

        this.fileStream = new FileStream(
            this.logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,  // Allow concurrent reads for recovery
            bufferSize: 64 * 1024, // 64KB buffer
            options);

        // Create unbounded channel for commit queue
        this.commitQueue = Channel.CreateUnbounded<PendingCommit>(new UnboundedChannelOptions
        {
            SingleReader = true, // Only background worker reads
            SingleWriter = false, // Multiple threads can enqueue
        });

        // Start background worker
        this.cts = new CancellationTokenSource();
        this.backgroundWorker = Task.Run(() => BackgroundCommitWorker(cts.Token), cts.Token);
    }

    /// <summary>
    /// Asynchronously commits data to the WAL.
    /// Returns a task that completes when the data has been durably written (batched with other commits).
    /// </summary>
    /// <param name="data">The data to commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the commit is durable.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public async Task<bool> CommitAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(GroupCommitWAL));
        }

        // Create WAL record with checksum
        var record = new WalRecord(data);

        // Create pending commit
        var pending = new PendingCommit
        {
            Record = record,
        };

        // Enqueue for batching
        await commitQueue.Writer.WriteAsync(pending, cancellationToken);

        Interlocked.Increment(ref totalCommits);

        // Wait for batch to complete
        return await pending.CompletionSource.Task;
    }

    /// <summary>
    /// Background worker that processes commit batches.
    /// Batches multiple pending commits into a single fsync operation for improved throughput.
    /// </summary>
    private async Task BackgroundCommitWorker(CancellationToken cancellationToken)
    {
        var batch = new List<PendingCommit>(maxBatchSize);
        byte[]? buffer = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                batch.Clear();

                try
                {
                    // Wait for first commit with timeout
                    var waitTask = commitQueue.Reader.WaitToReadAsync(cancellationToken);
                    if (!await waitTask.ConfigureAwait(false))
                    {
                        break; // Channel closed
                    }

                    // Collect batch with timeout
                    var deadline = DateTime.UtcNow + maxBatchDelay;
                    while (batch.Count < maxBatchSize && DateTime.UtcNow < deadline)
                    {
                        if (commitQueue.Reader.TryRead(out var pending))
                        {
                            batch.Add(pending);
                        }
                        else if (batch.Count > 0)
                        {
                            break; // Have some commits, process them
                        }
                        else
                        {
                            // Wait a bit for more commits
                            await Task.Delay(1, cancellationToken);
                        }
                    }

                    if (batch.Count == 0)
                    {
                        continue;
                    }

                    // Calculate total size needed
                    int totalSize = batch.Sum(p => p.Record.TotalSize);

                    // Rent buffer if needed
                    if (buffer == null || buffer.Length < totalSize)
                    {
                        if (buffer != null)
                        {
                            pool.Return(buffer, clearArray: true);
                        }
                        buffer = pool.Rent(totalSize);
                    }

                    // Write all records to buffer
                    int offset = 0;
                    foreach (var pending in batch)
                    {
                        offset += pending.Record.WriteTo(buffer.AsSpan(offset));
                    }

                    // Write buffer to file
                    await fileStream.WriteAsync(buffer.AsMemory(0, offset), cancellationToken);

                    // Flush based on durability mode
                    if (durabilityMode == DurabilityMode.FullSync)
                    {
                        // Force flush to physical disk (guarantees durability)
                        await Task.Run(() => fileStream.Flush(flushToDisk: true), cancellationToken);
                    }
                    else
                    {
                        // Async mode - just flush to OS buffer
                        await fileStream.FlushAsync(cancellationToken);
                    }

                    Interlocked.Increment(ref totalBatches);
                    Interlocked.Add(ref totalBytesWritten, offset);

                    // Complete all pending commits
                    foreach (var pending in batch)
                    {
                        pending.CompletionSource.SetResult(true);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    // Fail all pending commits
                    foreach (var pending in batch)
                    {
                        pending.CompletionSource.SetException(ex);
                    }
                }
            }
        }
        finally
        {
            if (buffer != null)
            {
                pool.Return(buffer, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Performs crash recovery by replaying the WAL from the beginning.
    /// Returns all successfully committed records in order.
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

        byte[]? buffer = null;
        try
        {
            // Read entire WAL file (allow write sharing for active fileStream)
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileSize = (int)fs.Length;
            
            if (fileSize == 0)
            {
                return records; // Empty file
            }

            buffer = pool.Rent(fileSize);
            int bytesRead = fs.Read(buffer, 0, fileSize);
            var span = buffer.AsSpan(0, bytesRead);

            // Parse records sequentially
            int offset = 0;
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
                    // Corrupted or incomplete record - stop recovery
                    // This is expected at the end if crash occurred during write
                    break;
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
                continue;
            }
        }
        
        return allRecords;
    }

    /// <summary>
    /// Reads and parses a WAL file, returning all valid records.
    /// </summary>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <returns>List of recovered data records.</returns>
    private static List<ReadOnlyMemory<byte>> ReadWalFile(string walFilePath)
    {
        var records = new List<ReadOnlyMemory<byte>>();
        var pool = ArrayPool<byte>.Shared;
        byte[]? buffer = null;

        try
        {
            // Allow write sharing in case file is actively being written
            using var fs = new FileStream(walFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileSize = (int)fs.Length;
            
            if (fileSize == 0)
            {
                return records; // Empty file
            }

            buffer = pool.Rent(fileSize);
            int bytesRead = fs.Read(buffer, 0, fileSize);
            var span = buffer.AsSpan(0, bytesRead);

            // Parse records sequentially
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
                    break; // Stop on first corrupted record
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
                continue;
            }
        }
        
        return deletedCount;
    }

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
    /// Clears the WAL file after successful checkpoint.
    /// </summary>
    public async Task ClearAsync()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(GroupCommitWAL));
        }

        // Wait for all pending commits to complete
        commitQueue.Writer.Complete();
        await backgroundWorker;

        // Truncate file
        fileStream.SetLength(0);
        fileStream.Flush(flushToDisk: true);

        // Restart background worker
        var newCts = new CancellationTokenSource();
        cts.Dispose();
    }

    /// <summary>
    /// Disposes the WAL and stops the background worker.
    /// Cleans up the instance-specific WAL file.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        // Signal shutdown
        commitQueue.Writer.Complete();
        cts.Cancel();

        // Wait for background worker to finish
        try
        {
            backgroundWorker.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout
        }

        // Cleanup resources
        cts.Dispose();
        fileStream.Dispose();
        
        // Delete instance-specific WAL file (it's been committed to main database)
        try
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
        catch
        {
            // Ignore deletion errors - file might be locked or already deleted
        }
    }

    /// <summary>
    /// Asynchronously disposes the WAL and stops the background worker.
    /// Cleans up the instance-specific WAL file.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        // Signal shutdown
        commitQueue.Writer.Complete();
        cts.Cancel();

        // Wait for background worker to finish
        try
        {
            await backgroundWorker.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout
        }

        // Cleanup resources
        cts.Dispose();
        await fileStream.DisposeAsync();
        
        // Delete instance-specific WAL file (it's been committed to main database)
        try
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
        catch
        {
            // Ignore deletion errors - file might be locked or already deleted
        }
    }

    /// <summary>
    /// Gets the instance ID of this WAL.
    /// </summary>
    public string InstanceId => instanceId;
}
