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

    /// <summary>
    /// Initializes a new instance of the <see cref="WAL"/> class.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="config">Optional database configuration for buffer size.</param>
    public WAL(string dbPath, DatabaseConfig? config = null)
    {
        this.logPath = Path.Combine(dbPath, PersistenceConstants.WalFileName);

        // Use FileStream with asynchronous I/O
        this.fileStream = new FileStream(this.logPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
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

    private void WriteToLog(byte[] bytes, int offset, int count)
    {
        while (count > 0)
        {
            var writable = Math.Min(BufferSize - this.bufferPosition, count);
            Buffer.BlockCopy(bytes, offset, this.buffer, this.bufferPosition, writable);
            this.bufferPosition += writable;
            offset += writable;
            count -= writable;

            // Flush the buffer to the file
            if (this.bufferPosition >= BufferSize)
            {
                this.FlushAsync().GetAwaiter().GetResult();
            }
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
    /// Asynchronously flushes buffered data to disk.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task FlushAsync()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(WAL));
        }

        await this.FlushBufferAsync();
    }

    private void FlushBuffer()
    {
        if (this.bufferPosition > 0)
        {
            this.fileStream.Write(this.buffer.AsSpan(0, this.bufferPosition));
            this.bufferPosition = 0;
        }
    }

    private async Task FlushBufferAsync()
    {
        if (this.bufferPosition > 0)
        {
            await this.fileStream.WriteAsync(this.buffer.AsMemory(0, this.bufferPosition));
            this.bufferPosition = 0;
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
            this.fileStream?.Dispose();
            this.disposed = true;
        }
    }
}
