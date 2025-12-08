// <copyright file="TemporaryBufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

/// <summary>
/// Pool for temporary buffers used in key/value operations, string conversions, and intermediate data.
/// PERFORMANCE: Eliminates allocation churn for frequently used temporary buffers.
/// THREAD-SAFETY: Thread-local caching for zero-contention access.
/// MEMORY: Pools small-to-medium buffers (1KB-256KB) that would otherwise create GC pressure.
/// </summary>
public class TemporaryBufferPool : IDisposable
{
    private readonly ArrayPool<byte> bytePool;
    private readonly ArrayPool<char> charPool;
    private readonly ThreadLocal<TempBufferCache> threadLocalCache;
    private readonly PoolConfiguration config;
    private bool disposed;

    // Common buffer sizes for database operations
    private const int SmallBufferSize = 1024;      // 1KB - for small keys/values
    private const int MediumBufferSize = 8192;     // 8KB - for typical records
    private const int LargeBufferSize = 65536;     // 64KB - for large records
    private const int XLargeBufferSize = 262144;   // 256KB - for bulk operations

    // Metrics
    private long byteBuffersRented;
    private long charBuffersRented;
    private long cacheHits;
    private long cacheMisses;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemporaryBufferPool"/> class.
    /// </summary>
    /// <param name="config">Optional pool configuration.</param>
    public TemporaryBufferPool(PoolConfiguration? config = null)
    {
        this.bytePool = ArrayPool<byte>.Shared;
        this.charPool = ArrayPool<char>.Shared;
        this.config = config ?? new PoolConfiguration
        {
            UseThreadLocal = true,
            ThreadLocalCapacity = 8, // Temp buffers used frequently
            ClearBuffersOnReturn = false, // Not sensitive data
        };

        if (this.config.UseThreadLocal)
        {
            this.threadLocalCache = new ThreadLocal<TempBufferCache>(
                () => new TempBufferCache(),
                trackAllValues: false);
        }
        else
        {
            this.threadLocalCache = null!;
        }
    }

    /// <summary>
    /// Rents a small byte buffer (1KB) for temporary use.
    /// USAGE: Keys, small values, small serialization operations.
    /// </summary>
    /// <returns>A rented temporary byte buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedTempBuffer RentSmallByteBuffer() => RentByteBuffer(SmallBufferSize);

    /// <summary>
    /// Rents a medium byte buffer (8KB) for temporary use.
    /// USAGE: Typical database records, intermediate serialization.
    /// </summary>
    /// <returns>A rented temporary byte buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedTempBuffer RentMediumByteBuffer() => RentByteBuffer(MediumBufferSize);

    /// <summary>
    /// Rents a large byte buffer (64KB) for temporary use.
    /// USAGE: Large records, batch operations.
    /// </summary>
    /// <returns>A rented temporary byte buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedTempBuffer RentLargeByteBuffer() => RentByteBuffer(LargeBufferSize);

    /// <summary>
    /// Rents a byte buffer of the specified minimum size.
    /// PERFORMANCE: Use predefined sizes (Small/Medium/Large) when possible for better cache reuse.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented temporary byte buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public RentedTempBuffer RentByteBuffer(int minimumSize)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(TemporaryBufferPool));
        }

        Interlocked.Increment(ref byteBuffersRented);

        byte[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)
        {
            if (cache.TryRentByteBuffer(minimumSize, out buffer))
            {
                Interlocked.Increment(ref cacheHits);
                fromCache = true;
            }
            else
            {
                Interlocked.Increment(ref cacheMisses);
                buffer = bytePool.Rent(minimumSize);
            }
        }
        else
        {
            buffer = bytePool.Rent(minimumSize);
        }

        return new RentedTempBuffer(buffer, null, this, minimumSize, BufferType.Byte, fromCache);
    }

    /// <summary>
    /// Rents a char buffer for string operations.
    /// USAGE: String building, UTF-8 to char conversion, text processing.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented temporary char buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public RentedTempCharBuffer RentCharBuffer(int minimumSize)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(TemporaryBufferPool));
        }

        Interlocked.Increment(ref charBuffersRented);

        char[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)
        {
            if (cache.TryRentCharBuffer(minimumSize, out buffer))
            {
                Interlocked.Increment(ref cacheHits);
                fromCache = true;
            }
            else
            {
                Interlocked.Increment(ref cacheMisses);
                buffer = charPool.Rent(minimumSize);
            }
        }
        else
        {
            buffer = charPool.Rent(minimumSize);
        }

        return new RentedTempCharBuffer(buffer, this, minimumSize, fromCache);
    }

    /// <summary>
    /// Returns a byte buffer to the pool.
    /// INTERNAL: Called automatically by RentedTempBuffer.Dispose().
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReturnByteBuffer(byte[] buffer, bool fromCache)
    {
        if (disposed || buffer == null) return;

        // Optionally clear if configured
        if (config.ClearBuffersOnReturn)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)
        {
            if (!cache.TryReturnByteBuffer(buffer))
            {
                bytePool.Return(buffer, clearArray: false);
            }
        }
        else
        {
            bytePool.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Returns a char buffer to the pool.
    /// INTERNAL: Called automatically by RentedTempCharBuffer.Dispose().
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReturnCharBuffer(char[] buffer, bool fromCache)
    {
        if (disposed || buffer == null) return;

        // Optionally clear if configured
        if (config.ClearBuffersOnReturn)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)
        {
            if (!cache.TryReturnCharBuffer(buffer))
            {
                charPool.Return(buffer, clearArray: false);
            }
        }
        else
        {
            charPool.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Gets pool statistics for monitoring.
    /// </summary>
    public TemporaryBufferPoolStatistics GetStatistics()
    {
        return new TemporaryBufferPoolStatistics
        {
            ByteBuffersRented = Interlocked.Read(ref byteBuffersRented),
            CharBuffersRented = Interlocked.Read(ref charBuffersRented),
            CacheHits = Interlocked.Read(ref cacheHits),
            CacheMisses = Interlocked.Read(ref cacheMisses),
            ThreadLocalEnabled = config.UseThreadLocal,
            ThreadLocalCapacity = config.ThreadLocalCapacity,
        };
    }

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
    /// Thread-local cache for temporary buffers.
    /// OPTIMIZATION: Separate arrays for different buffer sizes for better cache hit rate.
    /// </summary>
    private class TempBufferCache
    {
        private readonly byte[][] smallByteBuffers = new byte[2][];
        private readonly byte[][] mediumByteBuffers = new byte[2][];
        private readonly byte[][] largeByteBuffers = new byte[2][];
        private readonly char[][] charBuffers = new char[4][];
        
        private int smallByteCount;
        private int mediumByteCount;
        private int largeByteCount;
        private int charCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRentByteBuffer(int minimumSize, out byte[] buffer)
        {
            // Try to find appropriate size bucket
            if (minimumSize <= SmallBufferSize && TryGetFromArray(smallByteBuffers, ref smallByteCount, out buffer))
                return true;
            
            if (minimumSize <= MediumBufferSize && TryGetFromArray(mediumByteBuffers, ref mediumByteCount, out buffer))
                return true;
            
            if (minimumSize <= LargeBufferSize && TryGetFromArray(largeByteBuffers, ref largeByteCount, out buffer))
                return true;

            buffer = null!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReturnByteBuffer(byte[] buffer)
        {
            // Determine appropriate size bucket
            if (buffer.Length <= SmallBufferSize)
                return TryPutInArray(smallByteBuffers, ref smallByteCount, buffer);
            
            if (buffer.Length <= MediumBufferSize)
                return TryPutInArray(mediumByteBuffers, ref mediumByteCount, buffer);
            
            if (buffer.Length <= XLargeBufferSize)
                return TryPutInArray(largeByteBuffers, ref largeByteCount, buffer);

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRentCharBuffer(int minimumSize, out char[] buffer)
        {
            return TryGetFromArray(charBuffers, ref charCount, out buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReturnCharBuffer(char[] buffer)
        {
            return TryPutInArray(charBuffers, ref charCount, buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetFromArray<T>(T[] array, ref int count, out T item)
        {
            if (count > 0)
            {
                item = array[--count];
                array[count] = default!;
                return item != null;
            }

            item = default!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryPutInArray<T>(T[] array, ref int count, T item)
        {
            if (count < array.Length)
            {
                array[count++] = item;
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Buffer type for classification.
/// </summary>
internal enum BufferType
{
    Byte,
    Char,
}

/// <summary>
/// RAII wrapper for rented temporary byte buffer.
/// USAGE: Always use with 'using' statement.
/// </summary>
public ref struct RentedTempBuffer
{
    private byte[]? buffer;
    private char[]? charBuffer;
    private TemporaryBufferPool? pool;
    private int usedSize;
    private readonly BufferType bufferType;
    private readonly bool fromCache;

    internal RentedTempBuffer(
        byte[]? buffer,
        char[]? charBuffer,
        TemporaryBufferPool pool,
        int requestedSize,
        BufferType bufferType,
        bool fromCache)
    {
        this.buffer = buffer;
        this.charBuffer = charBuffer;
        this.pool = pool;
        this.usedSize = 0;
        this.bufferType = bufferType;
        this.fromCache = fromCache;
    }

    /// <summary>
    /// Gets the underlying byte buffer.
    /// </summary>
    public readonly byte[] ByteBuffer => buffer ?? throw new ObjectDisposedException(nameof(RentedTempBuffer));

    /// <summary>
    /// Gets or sets the used size.
    /// </summary>
    public int UsedSize
    {
        readonly get => usedSize;
        set => usedSize = value;
    }

    /// <summary>
    /// Gets a span view of the buffer.
    /// </summary>
    public readonly Span<byte> AsSpan() => buffer.AsSpan();

    /// <summary>
    /// Gets a span view of the used portion.
    /// </summary>
    public readonly Span<byte> UsedSpan() => buffer.AsSpan(0, usedSize);

    /// <summary>
    /// Gets a memory view of the buffer.
    /// </summary>
    public readonly Memory<byte> AsMemory() => buffer.AsMemory();

    /// <summary>
    /// Returns the buffer to the pool.
    /// </summary>
    public void Dispose()
    {
        if (buffer != null && pool != null)
        {
            pool.ReturnByteBuffer(buffer, fromCache);
            buffer = null;
            pool = null;
        }
    }
}

/// <summary>
/// RAII wrapper for rented temporary char buffer.
/// USAGE: Always use with 'using' statement.
/// </summary>
public ref struct RentedTempCharBuffer
{
    private char[]? buffer;
    private TemporaryBufferPool? pool;
    private int usedSize;
    private readonly bool fromCache;

    internal RentedTempCharBuffer(
        char[] buffer,
        TemporaryBufferPool pool,
        int requestedSize,
        bool fromCache)
    {
        this.buffer = buffer;
        this.pool = pool;
        this.usedSize = 0;
        this.fromCache = fromCache;
    }

    /// <summary>
    /// Gets the underlying char buffer.
    /// </summary>
    public readonly char[] CharBuffer => buffer ?? throw new ObjectDisposedException(nameof(RentedTempCharBuffer));

    /// <summary>
    /// Gets or sets the used size.
    /// </summary>
    public int UsedSize
    {
        readonly get => usedSize;
        set => usedSize = value;
    }

    /// <summary>
    /// Gets a span view of the buffer.
    /// </summary>
    public readonly Span<char> AsSpan() => buffer.AsSpan();

    /// <summary>
    /// Gets a span view of the used portion.
    /// </summary>
    public readonly Span<char> UsedSpan() => buffer.AsSpan(0, usedSize);

    /// <summary>
    /// Converts the used portion to a string.
    /// ALLOCATION: Creates a new string.
    /// </summary>
    public readonly string ToString() => new string(buffer, 0, usedSize);

    /// <summary>
    /// Returns the buffer to the pool.
    /// </summary>
    public void Dispose()
    {
        if (buffer != null && pool != null)
        {
            pool.ReturnCharBuffer(buffer, fromCache);
            buffer = null;
            pool = null;
        }
    }
}

/// <summary>
/// Statistics for temporary buffer pool monitoring.
/// </summary>
public class TemporaryBufferPoolStatistics
{
    public long ByteBuffersRented { get; set; }
    public long CharBuffersRented { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public bool ThreadLocalEnabled { get; set; }
    public int ThreadLocalCapacity { get; set; }

    public double CacheHitRate => CacheHits + CacheMisses > 0
        ? (double)CacheHits / (CacheHits + CacheMisses)
        : 0.0;
}
