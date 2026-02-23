// <copyright file="BufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// High-performance buffer pool for WAL streaming operations.
/// Provides efficient memory reuse to minimize GC pressure and allocations.
/// C# 14: Primary constructors, Span<T> for zero-copy operations.
/// </summary>
public sealed class BufferPool : IDisposable
{
    private readonly ConcurrentQueue<BufferSegment> _availableBuffers = [];
    private readonly Lock _poolLock = new();
    private readonly PoolConfiguration _config;
    private readonly ILogger<BufferPool>? _logger;

    private int _totalBuffersCreated;
    private int _currentBufferCount;
    private bool _disposed;

    /// <summary>Gets the pool configuration.</summary>
    public PoolConfiguration Config => _config;

    /// <summary>Gets the current number of available buffers.</summary>
    public int AvailableBuffers => _availableBuffers.Count;

    /// <summary>Gets the total number of buffers created.</summary>
    public int TotalBuffersCreated => _totalBuffersCreated;

    /// <summary>Gets the current number of buffers in the pool.</summary>
    public int CurrentBufferCount => _currentBufferCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferPool"/> class.
    /// </summary>
    /// <param name="config">The pool configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public BufferPool(PoolConfiguration? config = null, ILogger<BufferPool>? logger = null)
    {
        _config = config ?? new PoolConfiguration();
        _logger = logger;

        // Pre-allocate initial buffers
        PreallocateBuffers();
    }

    /// <summary>
    /// Rents a buffer from the pool.
    /// </summary>
    /// <param name="minimumSize">The minimum buffer size required.</param>
    /// <returns>A buffer segment from the pool.</returns>
    public BufferSegment Rent(int minimumSize = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Try to get an existing buffer
        if (_availableBuffers.TryDequeue(out var buffer))
        {
            Interlocked.Decrement(ref _currentBufferCount);

            // Check if buffer is large enough
            if (buffer.Data.Length >= minimumSize)
            {
                buffer.Reset();
                return buffer;
            }
            else
            {
                // Buffer too small, return it and create a new one
                Return(buffer);
            }
        }

        // Create a new buffer
        return CreateNewBuffer(minimumSize);
    }

    /// <summary>
    /// Returns a buffer to the pool.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    public void Return(BufferSegment buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (_disposed)
        {
            // If disposed, just let the buffer be GC'd
            return;
        }

        // Validate buffer before returning
        if (!IsValidBuffer(buffer))
        {
            _logger?.LogWarning("Invalid buffer returned to pool, discarding");
            return;
        }

        // Reset buffer state
        buffer.Reset();

        // Check if we should keep this buffer
        if (_availableBuffers.Count < _config.MaxBuffers)
        {
            _availableBuffers.Enqueue(buffer);
            Interlocked.Increment(ref _currentBufferCount);
        }
        else
        {
            // Pool is full, let buffer be GC'd
            _logger?.LogDebug("Buffer pool full, discarding returned buffer");
        }
    }

    /// <summary>
    /// Rents a buffer and returns a disposable wrapper that automatically returns it.
    /// </summary>
    /// <param name="minimumSize">The minimum buffer size required.</param>
    /// <returns>A disposable buffer handle.</returns>
    public PooledBufferHandle RentDisposable(int minimumSize = 0)
    {
        var buffer = Rent(minimumSize);
        return new PooledBufferHandle(buffer, this);
    }

    /// <summary>
    /// Gets pool statistics.
    /// </summary>
    /// <returns>Pool statistics.</returns>
    public PoolMetrics GetMetrics()
    {
        return new PoolMetrics
        {
            TotalBuffersCreated = _totalBuffersCreated,
            CurrentBufferCount = _currentBufferCount,
            AvailableBuffers = _availableBuffers.Count,
            PoolUtilizationPercent = _config.MaxBuffers > 0
                ? (double)_currentBufferCount / _config.MaxBuffers * 100
                : 0,
            AverageBufferSize = CalculateAverageBufferSize(),
            TotalMemoryUsage = CalculateTotalMemoryUsage()
        };
    }

    /// <summary>
    /// Clears all buffers from the pool.
    /// </summary>
    public void Clear()
    {
        lock (_poolLock)
        {
            if (_disposed)
            {
                return;
            }

            _availableBuffers.Clear();
            _currentBufferCount = 0;

            _logger?.LogInformation("Buffer pool cleared");
        }
    }

    /// <summary>
    /// Pre-allocates initial buffers.
    /// </summary>
    private void PreallocateBuffers()
    {
        for (var i = 0; i < _config.InitialBuffers; i++)
        {
            var buffer = CreateNewBuffer(_config.DefaultBufferSize);
            _availableBuffers.Enqueue(buffer);
            Interlocked.Increment(ref _currentBufferCount);
        }

        _logger?.LogInformation("Pre-allocated {Count} buffers of size {Size}",
            _config.InitialBuffers, _config.DefaultBufferSize);
    }

    /// <summary>
    /// Creates a new buffer with the specified minimum size.
    /// </summary>
    /// <param name="minimumSize">The minimum buffer size.</param>
    /// <returns>A new buffer segment.</returns>
    private BufferSegment CreateNewBuffer(int minimumSize)
    {
        var size = Math.Max(minimumSize, _config.DefaultBufferSize);
        size = Math.Min(size, _config.MaxBufferSize);

        var data = GC.AllocateUninitializedArray<byte>(size, pinned: false);
        var buffer = new BufferSegment(data, this);

        Interlocked.Increment(ref _totalBuffersCreated);

        _logger?.LogDebug("Created new buffer of size {Size}", size);

        return buffer;
    }

    /// <summary>
    /// Validates a buffer before returning it to the pool.
    /// </summary>
    /// <param name="buffer">The buffer to validate.</param>
    /// <returns>True if the buffer is valid.</returns>
    private static bool IsValidBuffer(BufferSegment buffer)
    {
        // Basic validation - can be extended with checksums, etc.
        return buffer.Data is not null && buffer.Data.Length > 0;
    }

    /// <summary>
    /// Calculates the average buffer size in the pool.
    /// </summary>
    /// <returns>The average buffer size.</returns>
    private double CalculateAverageBufferSize()
    {
        if (_availableBuffers.IsEmpty)
        {
            return 0;
        }

        long totalSize = 0;
        var count = 0;

        foreach (var buffer in _availableBuffers)
        {
            totalSize += buffer.Data.Length;
            count++;
        }

        return count > 0 ? (double)totalSize / count : 0;
    }

    /// <summary>
    /// Calculates the total memory usage of the pool.
    /// </summary>
    /// <returns>The total memory usage in bytes.</returns>
    private long CalculateTotalMemoryUsage()
    {
        long totalSize = 0;

        foreach (var buffer in _availableBuffers)
        {
            totalSize += buffer.Data.Length;
        }

        return totalSize;
    }

    /// <summary>
    /// Disposes the buffer pool.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();

        _logger?.LogInformation("Buffer pool disposed");
    }
}

/// <summary>
/// Represents a segment of pooled buffer memory.
/// </summary>
public sealed class BufferSegment
{
    private readonly BufferPool _pool;

    /// <summary>Gets the buffer data.</summary>
    public byte[] Data { get; }

    /// <summary>Gets the current position in the buffer.</summary>
    public int Position { get; private set; }

    /// <summary>Gets the number of bytes written to the buffer.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the remaining capacity in the buffer.</summary>
    public int RemainingCapacity => Data.Length - Position;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferSegment"/> class.
    /// </summary>
    /// <param name="data">The buffer data.</param>
    /// <param name="pool">The pool this buffer belongs to.</param>
    internal BufferSegment(byte[] data, BufferPool pool)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>
    /// Writes data to the buffer.
    /// </summary>
    /// <param name="data">The data to write.</param>
    /// <returns>The number of bytes written.</returns>
    public int Write(ReadOnlySpan<byte> data)
    {
        var bytesToWrite = Math.Min(data.Length, RemainingCapacity);
        if (bytesToWrite > 0)
        {
            data[..bytesToWrite].CopyTo(Data.AsSpan(Position));
            Position += bytesToWrite;
            Length = Math.Max(Length, Position);
        }
        return bytesToWrite;
    }

    /// <summary>
    /// Reads data from the buffer.
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes read.</returns>
    public int Read(Span<byte> destination)
    {
        var bytesToRead = Math.Min(destination.Length, Length - Position);
        if (bytesToRead > 0)
        {
            Data.AsSpan(Position, bytesToRead).CopyTo(destination);
            Position += bytesToRead;
        }
        return bytesToRead;
    }

    /// <summary>
    /// Gets a span representing the written data.
    /// </summary>
    /// <returns>A span of the written data.</returns>
    public ReadOnlySpan<byte> GetWrittenSpan()
    {
        return Data.AsSpan(0, Length);
    }

    /// <summary>
    /// Resets the buffer position and length.
    /// </summary>
    public void Reset()
    {
        Position = 0;
        Length = 0;
    }

    /// <summary>
    /// Returns this buffer to the pool.
    /// </summary>
    public void Return()
    {
        _pool.Return(this);
    }
}

/// <summary>
/// Disposable handle for pooled buffers that automatically returns them.
/// </summary>
public readonly struct PooledBufferHandle : IDisposable
{
    private readonly BufferSegment _buffer;
    private readonly BufferPool _pool;

    /// <summary>Gets the buffer segment.</summary>
    public BufferSegment Buffer => _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledBufferHandle"/> struct.
    /// </summary>
    /// <param name="buffer">The buffer segment.</param>
    /// <param name="pool">The buffer pool.</param>
    internal PooledBufferHandle(BufferSegment buffer, BufferPool pool)
    {
        _buffer = buffer;
        _pool = pool;
    }

    /// <summary>
    /// Disposes the handle and returns the buffer to the pool.
    /// </summary>
    public void Dispose()
    {
        _pool.Return(_buffer);
    }
}
