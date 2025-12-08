// <copyright file="PageSerializerPool.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Pooling;

using SharpCoreDB.Core.File;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
/// Pooled page serialization buffers for zero-allocation page I/O.
/// PERFORMANCE: Reuses large page buffers (4KB) to avoid allocations.
/// THREAD-SAFETY: Thread-local pooling eliminates contention.
/// NOTE: PageSerializer is static, so we only pool buffers, not serializer instances.
/// </summary>
public class PageSerializerPool : IDisposable
{
    private readonly ArrayPool<byte> bufferPool;
    private readonly int pageSize;
    private readonly ThreadLocal<BufferCache> threadLocalCache;
    private readonly PoolConfiguration config;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageSerializerPool"/> class.
    /// </summary>
    /// <param name="pageSize">The page size for serialization (default: 4096).</param>
    /// <param name="config">Optional pool configuration.</param>
    public PageSerializerPool(int pageSize = 4096, PoolConfiguration? config = null)
    {
        this.pageSize = pageSize;
        this.bufferPool = ArrayPool<byte>.Shared;
        this.config = config ?? new PoolConfiguration
        {
            MaximumRetained = 20,
            UseThreadLocal = true,
            ThreadLocalCapacity = 3,
            ClearBuffersOnReturn = true,
        };

        if (this.config.UseThreadLocal)
        {
            this.threadLocalCache = new ThreadLocal<BufferCache>(
                () => new BufferCache(this.config.ThreadLocalCapacity),
                trackAllValues: false);
        }
        else
        {
            this.threadLocalCache = null!;
        }
    }

    /// <summary>
    /// Rents a page buffer from the pool.
    /// USAGE: Always use with 'using' statement or call Return() in finally block.
    /// THREAD-SAFETY: Safe to call from multiple threads concurrently.
    /// </summary>
    /// <returns>A rented page buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedPageBuffer Rent()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(PageSerializerPool));
        }

        byte[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is BufferCache cache)
        {
            if (cache.TryRent(pageSize, out buffer))
            {
                fromCache = true;
            }
            else
            {
                buffer = bufferPool.Rent(pageSize);
            }
        }
        else
        {
            buffer = bufferPool.Rent(pageSize);
        }

        return new RentedPageBuffer(buffer, this, pageSize, fromCache);
    }

    /// <summary>
    /// Returns a page buffer to the pool.
    /// SAFETY: Automatically called by RentedPageBuffer.Dispose().
    /// INTERNAL: Do not call directly - use RentedPageBuffer disposal.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <param name="fromCache">Whether buffer came from thread-local cache.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Return(byte[] buffer, bool fromCache)
    {
        if (disposed || buffer == null) return;

        // SAFETY: Clear buffer if configured
        if (config.ClearBuffersOnReturn)
        {
            Array.Clear(buffer, 0, Math.Min(pageSize, buffer.Length));
        }

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is BufferCache cache)
        {
            if (!cache.TryReturn(buffer))
            {
                bufferPool.Return(buffer, clearArray: false);
            }
        }
        else
        {
            bufferPool.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Gets pool statistics for monitoring.
    /// </summary>
    /// <returns>Pool statistics.</returns>
    public PoolStatistics GetStatistics() => new PoolStatistics
    {
        ThreadLocalCapacity = config.ThreadLocalCapacity,
        IsThreadLocalEnabled = config.UseThreadLocal,
    };

    /// <summary>
    /// Disposes the pool and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            threadLocalCache?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Thread-local buffer cache for zero-contention access.
    /// </summary>
    private class BufferCache
    {
        private readonly byte[][] buffers;
        private int count;

        public BufferCache(int capacity)
        {
            buffers = new byte[capacity][];
            count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRent(int minimumSize, out byte[] buffer)
        {
            // Find buffer of adequate size
            for (int i = count - 1; i >= 0; i--)
            {
                if (buffers[i].Length >= minimumSize)
                {
                    buffer = buffers[i];
                    
                    // Remove from cache
                    for (int j = i; j < count - 1; j++)
                    {
                        buffers[j] = buffers[j + 1];
                    }
                    buffers[--count] = null!;
                    
                    return true;
                }
            }

            buffer = null!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReturn(byte[] buffer)
        {
            if (count < buffers.Length)
            {
                buffers[count++] = buffer;
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// RAII wrapper for rented page buffer.
/// USAGE: Always use with 'using' statement to ensure proper return to pool.
/// THREAD-SAFETY: Not thread-safe - each instance should be used by one thread at a time.
/// </summary>
public ref struct RentedPageBuffer
{
    private byte[]? buffer;
    private PageSerializerPool? pool;
    private readonly int pageSize;
    private readonly bool fromCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="RentedPageBuffer"/> struct.
    /// INTERNAL: Created by PageSerializerPool.Rent() only.
    /// </summary>
    internal RentedPageBuffer(byte[] buffer, PageSerializerPool pool, int pageSize, bool fromCache)
    {
        this.buffer = buffer;
        this.pool = pool;
        this.pageSize = pageSize;
        this.fromCache = fromCache;
    }

    /// <summary>
    /// Gets the underlying buffer.
    /// SAFETY: Only valid until Dispose() is called.
    /// </summary>
    public readonly byte[] Buffer => buffer 
        ?? throw new ObjectDisposedException(nameof(RentedPageBuffer));

    /// <summary>
    /// Gets a span view of the buffer.
    /// OPTIMIZED: Zero-allocation access to buffer data.
    /// </summary>
    public readonly Span<byte> AsSpan() => buffer.AsSpan(0, pageSize);

    /// <summary>
    /// Gets a memory view of the buffer.
    /// OPTIMIZED: Zero-allocation async-compatible access.
    /// </summary>
    public readonly Memory<byte> AsMemory() => buffer.AsMemory(0, pageSize);

    /// <summary>
    /// Serializes a page header to the buffer using static PageSerializer.
    /// OPTIMIZED: Uses MemoryMarshal for zero-copy serialization.
    /// </summary>
    /// <param name="header">The page header to serialize.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void SerializeHeader(ref PageHeader header)
    {
        PageSerializer.SerializeHeader(ref header, buffer.AsSpan());
    }

    /// <summary>
    /// Deserializes a page header from the buffer using static PageSerializer.
    /// OPTIMIZED: Uses MemoryMarshal for zero-copy deserialization.
    /// </summary>
    /// <returns>The deserialized page header.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly PageHeader DeserializeHeader()
    {
        return PageSerializer.DeserializeHeader(buffer.AsSpan());
    }

    /// <summary>
    /// Creates a complete page in the buffer.
    /// OPTIMIZED: Uses static PageSerializer with SIMD acceleration.
    /// </summary>
    /// <param name="header">The page header.</param>
    /// <param name="data">The page data.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void CreatePage(ref PageHeader header, ReadOnlySpan<byte> data)
    {
        PageSerializer.CreatePage(ref header, data, buffer.AsSpan());
    }

    /// <summary>
    /// Validates the page in the buffer.
    /// OPTIMIZED: Uses SIMD-accelerated checksum validation.
    /// </summary>
    /// <returns>True if page is valid.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ValidatePage()
    {
        return PageSerializer.ValidatePage(buffer.AsSpan());
    }

    /// <summary>
    /// Gets the page data from the buffer (excluding header).
    /// OPTIMIZED: Returns span slice without allocation.
    /// </summary>
    /// <param name="dataLength">Output: the length of data.</param>
    /// <returns>A span pointing to the data section.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<byte> GetPageData(out int dataLength)
    {
        return PageSerializer.GetPageData(buffer.AsSpan(), out dataLength);
    }

    /// <summary>
    /// Returns the buffer to the pool.
    /// SAFETY: Automatically called at end of 'using' block.
    /// IDEMPOTENT: Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (buffer != null && pool != null)
        {
            pool.Return(buffer, fromCache);
            buffer = null;
            pool = null;
        }
    }
}
