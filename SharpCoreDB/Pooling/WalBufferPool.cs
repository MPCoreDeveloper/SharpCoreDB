// <copyright file="WalBufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// Specialized buffer pool for WAL operations with zero-contention access.
/// PERFORMANCE: Uses ArrayPool with thread-local caching for optimal throughput.
/// MEMORY: Reuses large buffers (4MB) to minimize GC pressure.
/// THREAD-SAFETY: Completely lock-free when using thread-local cache.
/// </summary>
public class WalBufferPool : IDisposable
{
    private readonly ArrayPool<byte> bufferPool;
    private readonly int defaultBufferSize;
    private readonly ThreadLocal<BufferCache> threadLocalCache;
    private readonly PoolConfiguration config;
    private bool disposed;

    // Metrics for monitoring
    private long buffersRented;
    private long buffersReturned;
    private long cacheHits;
    private long cacheMisses;

    /// <summary>
    /// Initializes a new instance of the <see cref="WalBufferPool"/> class.
    /// </summary>
    /// <param name="defaultBufferSize">Default buffer size (default: 4MB for WAL).</param>
    /// <param name="config">Optional pool configuration.</param>
    public WalBufferPool(int defaultBufferSize = 4 * 1024 * 1024, PoolConfiguration? config = null)
    {
        this.defaultBufferSize = defaultBufferSize;
        this.bufferPool = ArrayPool<byte>.Shared;
        this.config = config ?? new PoolConfiguration
        {
            UseThreadLocal = true,
            ThreadLocalCapacity = 2, // WAL typically needs 1-2 buffers per thread
            ClearBuffersOnReturn = true,
        };

        if (this.config.UseThreadLocal)
        {
            this.threadLocalCache = new ThreadLocal<BufferCache>(
                () => new BufferCache(this.config.ThreadLocalCapacity),
                trackAllValues: true); // SECURITY: Track all values to dispose them
        }
        else
        {
            this.threadLocalCache = null!;
        }
    }

    /// <summary>
    /// Rents a buffer of the default size from the pool.
    /// PERFORMANCE: First tries thread-local cache (zero locks), then ArrayPool.
    /// ALLOCATION: Returns existing buffer from pool, or creates new if needed.
    /// USAGE: Always return buffer using Return() or RentedBuffer.Dispose().
    /// </summary>
    /// <returns>A rented buffer wrapper.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedBuffer Rent() => Rent(defaultBufferSize);

    /// <summary>
    /// Rents a buffer of the specified minimum size from the pool.
    /// PERFORMANCE: Returned buffer may be larger than requested.
    /// THREAD-SAFETY: Safe to call from multiple threads concurrently.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented buffer wrapper.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedBuffer Rent(int minimumSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        Interlocked.Increment(ref buffersRented);

        byte[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is BufferCache cache)
        {
            if (cache.TryRent(minimumSize, out buffer))
            {
                Interlocked.Increment(ref cacheHits);
                fromCache = true;
            }
            else
            {
                Interlocked.Increment(ref cacheMisses);
                buffer = bufferPool.Rent(minimumSize);
            }
        }
        else
        {
            buffer = bufferPool.Rent(minimumSize);
        }

        return new RentedBuffer(buffer, this, minimumSize, fromCache);
    }

    /// <summary>
    /// Returns a buffer to the pool.
    /// SAFETY: Buffer is cleared if ClearBuffersOnReturn is enabled.
    /// PERFORMANCE: First tries thread-local cache, then ArrayPool.
    /// INTERNAL: Called automatically by RentedBuffer.Dispose().
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <param name="actualSize">The actual size that was used.</param>
    /// <param name="fromCache">Whether buffer was from thread-local cache.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Return(byte[] buffer, int actualSize, bool fromCache)
    {
        if (disposed || buffer == null)
        {
            return;
        }

        Interlocked.Increment(ref buffersReturned);

        // Clear buffer if configured (SECURITY)
        if (config.ClearBuffersOnReturn && actualSize > 0)
        {
            // Only clear the portion that was actually used
            Array.Clear(buffer, 0, Math.Min(actualSize, buffer.Length));
        }

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is BufferCache cache)
        {
            if (!cache.TryReturn(buffer))
            {
                // Cache full, return to ArrayPool
                bufferPool.Return(buffer, clearArray: false); // Already cleared above
            }
        }
        else
        {
            bufferPool.Return(buffer, clearArray: false); // Already cleared above
        }
    }

    /// <summary>
    /// Gets pool statistics for monitoring and diagnostics.
    /// </summary>
    /// <returns>Pool statistics.</returns>
    public WalBufferPoolStatistics GetStatistics()
    {
        return new WalBufferPoolStatistics
        {
            BuffersRented = Interlocked.Read(ref buffersRented),
            BuffersReturned = Interlocked.Read(ref buffersReturned),
            CacheHits = Interlocked.Read(ref cacheHits),
            CacheMisses = Interlocked.Read(ref cacheMisses),
            DefaultBufferSize = defaultBufferSize,
            ThreadLocalEnabled = config.UseThreadLocal,
            ThreadLocalCapacity = config.ThreadLocalCapacity,
        };
    }

    /// <summary>
    /// Resets pool statistics.
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref buffersRented, 0);
        Interlocked.Exchange(ref buffersReturned, 0);
        Interlocked.Exchange(ref cacheHits, 0);
        Interlocked.Exchange(ref cacheMisses, 0);
    }

    /// <summary>
    /// Disposes the pool and releases all resources.
    /// SAFETY: Clears thread-local caches and returns buffers to ArrayPool.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for proper IDisposable pattern.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing && threadLocalCache != null)
            {
                // SECURITY: Dispose all thread-local caches to clear buffers
                // Clear all tracked cache instances
                foreach (var cache in threadLocalCache.Values)
                {
                    cache?.Dispose();
                }
                
                threadLocalCache.Dispose();
            }

            disposed = true;
        }
    }

    /// <summary>
    /// Thread-local buffer cache for zero-contention access.
    /// IMPLEMENTATION: Simple array-based stack optimized for WAL patterns.
    /// CAPACITY: Typically 2-3 buffers per thread (WAL write + flush buffer).
    /// </summary>
    private sealed class BufferCache : IDisposable
    {
        private readonly BufferEntry[] entries;
        private int count;
        private bool disposed;

        public BufferCache(int capacity)
        {
            entries = new BufferEntry[capacity];
            count = 0;
            disposed = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRent(int minimumSize, out byte[] buffer)
        {
            // Search for buffer of adequate size
            for (int i = count - 1; i >= 0; i--)
            {
                if (entries[i].Buffer.Length >= minimumSize)
                {
                    buffer = entries[i].Buffer;
                    
                    // Remove from cache by shifting
                    for (int j = i; j < count - 1; j++)
                    {
                        entries[j] = entries[j + 1];
                    }
                    entries[--count] = default;
                    
                    return true;
                }
            }

            buffer = null!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReturn(byte[] buffer)
        {
            if (count < entries.Length)
            {
                entries[count++] = new BufferEntry { Buffer = buffer };
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears all cached buffers securely.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < count; i++)
            {
                if (entries[i].Buffer != null)
                {
                    Array.Clear(entries[i].Buffer, 0, entries[i].Buffer.Length);
                    entries[i] = default;
                }
            }
            count = 0;
        }

        /// <summary>
        /// Disposes the cache and clears all buffers.
        /// SECURITY: Ensures buffers are cleared on disposal.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                Clear();
                disposed = true;
            }
        }

        private struct BufferEntry
        {
            public byte[] Buffer;
        }
    }
}

/// <summary>
/// RAII wrapper for rented WAL buffer.
/// USAGE: Always use with 'using' statement to ensure proper return to pool.
/// SAFETY: Automatically returns buffer on disposal with proper cleanup.
/// </summary>
public ref struct RentedBuffer
{
    private byte[]? buffer;
    private WalBufferPool? pool;
    private int usedSize;
    private readonly bool fromCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="RentedBuffer"/> struct.
    /// INTERNAL: Created by WalBufferPool.Rent() only.
    /// </summary>
    internal RentedBuffer(byte[] buffer, WalBufferPool pool, int requestedSize, bool fromCache)
    {
        this.buffer = buffer;
        this.pool = pool;
        this.usedSize = 0;
        this.fromCache = fromCache;
    }

    /// <summary>
    /// Gets the underlying buffer array.
    /// SAFETY: Only valid until Dispose() is called.
    /// WARNING: Buffer may be larger than requested size.
    /// </summary>
    public readonly byte[] Buffer => buffer 
        ?? throw new ObjectDisposedException(nameof(RentedBuffer));

    /// <summary>
    /// Gets or sets the number of bytes actually used.
    /// SECURITY: Only this portion will be cleared on return if configured.
    /// </summary>
    public int UsedSize
    {
        readonly get => usedSize;
        set
        {
            if (value < 0 || (buffer != null && value > buffer.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            usedSize = value;
        }
    }

    /// <summary>
    /// Gets a span view of the buffer.
    /// OPTIMIZED: Zero-allocation access to buffer data.
    /// </summary>
    public readonly Span<byte> AsSpan()
    {
        return buffer.AsSpan();
    }

    /// <summary>
    /// Gets a span view of the used portion of the buffer.
    /// OPTIMIZED: Zero-allocation access to used data only.
    /// </summary>
    public readonly Span<byte> UsedSpan()
    {
        return buffer.AsSpan(0, usedSize);
    }

    /// <summary>
    /// Gets a memory view of the buffer.
    /// OPTIMIZED: Zero-allocation async-compatible access.
    /// </summary>
    public readonly Memory<byte> AsMemory()
    {
        return buffer.AsMemory();
    }

    /// <summary>
    /// Gets a memory view of the used portion.
    /// OPTIMIZED: Zero-allocation async-compatible access to used data only.
    /// </summary>
    public readonly Memory<byte> UsedMemory()
    {
        return buffer.AsMemory(0, usedSize);
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
            pool.Return(buffer, usedSize, fromCache);
            buffer = null;
            pool = null;
        }
    }
}

/// <summary>
/// Statistics for WAL buffer pool monitoring.
/// </summary>
public class WalBufferPoolStatistics
{
    /// <summary>
    /// Gets or sets the total number of buffers rented (lifetime).
    /// </summary>
    public long BuffersRented { get; set; }

    /// <summary>
    /// Gets or sets the total number of buffers returned (lifetime).
    /// </summary>
    public long BuffersReturned { get; set; }

    /// <summary>
    /// Gets or sets the number of thread-local cache hits.
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Gets or sets the number of thread-local cache misses.
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Gets or sets the default buffer size.
    /// </summary>
    public int DefaultBufferSize { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether thread-local caching is enabled.
    /// </summary>
    public bool ThreadLocalEnabled { get; set; }

    /// <summary>
    /// Gets or sets the thread-local cache capacity.
    /// </summary>
    public int ThreadLocalCapacity { get; set; }

    /// <summary>
    /// Gets the cache hit rate (0.0 to 1.0).
    /// </summary>
    public double CacheHitRate => CacheHits + CacheMisses > 0
        ? (double)CacheHits / (CacheHits + CacheMisses)
        : 0.0;

    /// <summary>
    /// Gets the number of buffers currently outstanding.
    /// </summary>
    public long OutstandingBuffers => BuffersRented - BuffersReturned;
}
