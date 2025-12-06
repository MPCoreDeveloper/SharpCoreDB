// <copyright file="WAL.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

/// <summary>
/// Implementation of IWAL using a log file with buffered I/O for improved performance.
/// Uses 4MB ArrayPool.<byte> buffer with async flush.
/// </summary>
public class WAL : IWAL, IDisposable
{
    private readonly string logPath;
    private readonly FileStream fileStream;
    private readonly byte[] buffer;
    private int bufferPosition = 0;
    private bool disposed = false;
    private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
    private const int BufferSize = 4 * 1024 * 1024; // 4MB
    private readonly SemaphoreSlim semaphore = new(1);
    private const int FlushThreshold = 1000;
    private List<WalEntry> _pendingEntries = new();
    private readonly WalManager? _walManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WAL"/> class.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="config">Optional database configuration for buffer size.</param>
    /// <param name="walManager">Optional WalManager for pooled streams.</param>
    public WAL(string dbPath, DatabaseConfig? config = null, WalManager? walManager = null)
    {
        this.logPath = Path.Combine(dbPath, PersistenceConstants.WalFileName);
        _walManager = walManager;

        if (_walManager != null)
        {
            this.fileStream = _walManager.GetStream(this.logPath);
        }
        else
        {
            // Fallback to direct creation
            this.fileStream = new FileStream(this.logPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        }

        this.buffer = this.pool.Rent(BufferSize);
    }

    /// <inheritdoc />
    public void Log(string operation)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(WAL));
        }

        var operationBytes = Encoding.UTF8.GetBytes(operation + Environment.NewLine);
        if (this.bufferPosition + operationBytes.Length > this.buffer.Length)
        {
            this.FlushBuffer();
        }

        operationBytes.AsSpan().CopyTo(this.buffer.AsSpan(this.bufferPosition));
        this.bufferPosition += operationBytes.Length;
    }

    /// <summary>
    /// Asynchronously appends an entry to the WAL without flushing per entry for performance.
    /// Ensures WAL integrity by buffering entries.
    /// </summary>
    /// <param name="entry">The entry to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AppendEntryAsync(WalEntry entry, CancellationToken cancellationToken = default)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(WAL));
        }

        _pendingEntries.Add(entry);
        if (_pendingEntries.Count >= FlushThreshold)
        {
            await FlushPendingAsync();
        }
    }

    private async Task FlushPendingAsync()
    {
        await this.semaphore.WaitAsync();
        try
        {
            foreach (var entry in _pendingEntries)
            {
                var operationBytes = Encoding.UTF8.GetBytes(entry.Operation + Environment.NewLine);
                if (this.bufferPosition + operationBytes.Length > this.buffer.Length)
                {
                    await this.FlushBufferAsync();
                }
                operationBytes.AsSpan().CopyTo(this.buffer.AsSpan(this.bufferPosition));
                this.bufferPosition += operationBytes.Length;
            }
            await this.FlushBufferAsync(); // Ensure buffer is flushed after batch
            _pendingEntries.Clear();
        }
        catch (Exception ex)
        {
            // Handle error
            throw new InvalidOperationException("Failed to flush pending entries", ex);
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(WAL));
        }

        // Ensure all buffered data is written before committing
        this.FlushBuffer();
        this.fileStream.Flush(true);
        this.fileStream.Close();

        File.Delete(this.logPath);
        this.disposed = true;
    }

    /// <summary>
    /// Asynchronously commits the WAL transaction, ensuring all buffered data is flushed to disk for WAL integrity.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(WAL));
        }

        // Ensure all buffered data is written before committing to maintain WAL integrity
        await FlushPendingAsync();
        await this.FlushBufferAsync();
        await this.fileStream.FlushAsync(cancellationToken);
        this.fileStream.Close();

        File.Delete(this.logPath);
        this.disposed = true;
    }

    /// <summary>
    /// Asynchronously flushes buffered data to disk.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task FlushAsync()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(WAL));
        }

        await FlushPendingAsync();
        await this.FlushBufferAsync();
    }

    private void FlushBuffer()
    {
        this.semaphore.Wait();
        try
        {
            if (this.bufferPosition > 0)
            {
                this.fileStream.Write(this.buffer.AsSpan(0, this.bufferPosition));
                this.bufferPosition = 0;
            }
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    private async Task FlushBufferAsync()
    {
        await this.semaphore.WaitAsync();
        try
        {
            if (this.bufferPosition > 0)
            {
                await this.fileStream.WriteAsync(this.buffer.AsMemory(0, this.bufferPosition));
                this.bufferPosition = 0;
            }
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    /// <summary>
    /// Disposes the WAL instance and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.FlushBuffer();
            this.pool.Return(this.buffer);
            if (_walManager != null)
            {
                _walManager.ReturnStream(this.logPath, this.fileStream);
            }
            else
            {
                this.fileStream?.Dispose();
            }
            this.semaphore.Dispose();
            this.disposed = true;
        }
    }
}

/// <summary>
public record WalEntry(string Operation);
