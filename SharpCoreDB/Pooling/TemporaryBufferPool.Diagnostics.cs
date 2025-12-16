// <copyright file="TemporaryBufferPool.Diagnostics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// TemporaryBufferPool - Diagnostics, statistics, and nested types.
/// Contains metrics, monitoring, TempBufferCache, and wrapper structs.
/// Part of the TemporaryBufferPool partial class.
/// Modern C# 14 with collection expressions and inline arrays.
/// See also: TemporaryBufferPool.Core.cs, TemporaryBufferPool.Operations.cs
/// </summary>
public partial class TemporaryBufferPool
{
    /// <summary>
    /// Gets pool statistics for monitoring.
    /// ✅ C# 14: Target-typed new for statistics object.
    /// </summary>
    public TemporaryBufferPoolStatistics GetStatistics()
    {
        return new()  // ✅ C# 14: Target-typed new
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
    /// Thread-local cache for temporary buffers.
    /// OPTIMIZATION: Separate arrays for different buffer sizes for better cache hit rate.
    /// ✅ C# 14: Collection expressions for array initialization.
    /// </summary>
    private sealed class TempBufferCache : IDisposable
    {
        private readonly byte[][] smallByteBuffers = [null!, null!];  // ✅ C# 14: Collection expression
        private readonly byte[][] mediumByteBuffers = [null!, null!];
        private readonly byte[][] largeByteBuffers = [null!, null!];
        private readonly char[][] charBuffers = [null!, null!, null!, null!];
        
        private int smallByteCount;
        private int mediumByteCount;
        private int largeByteCount;
        private int charCount;
        private bool disposed;

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

        /// <summary>
        /// Clears all cached buffers.
        /// </summary>
        public void Clear()
        {
            // Clear byte buffers
            for (int i = 0; i < smallByteCount; i++)
            {
                smallByteBuffers[i] = null!;
            }
            smallByteCount = 0;

            for (int i = 0; i < mediumByteCount; i++)
            {
                mediumByteBuffers[i] = null!;
            }
            mediumByteCount = 0;

            for (int i = 0; i < largeByteCount; i++)
            {
                largeByteBuffers[i] = null!;
            }
            largeByteCount = 0;

            // Clear char buffers
            for (int i = 0; i < charCount; i++)
            {
                charBuffers[i] = null!;
            }
            charCount = 0;
        }

        /// <summary>
        /// Disposes the cache and clears all buffers.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                Clear();
                disposed = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetFromArray<T>(T[] array, ref int count, out T item)
        {
            if (count > 0)
            {
                item = array[--count];
                array[count] = default!;
                return !EqualityComparer<T>.Default.Equals(item, default);
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
/// ✅ C# 14: ref struct with modern null checks.
/// </summary>
public ref struct RentedTempBuffer
{
    private byte[]? buffer;
    private TemporaryBufferPool? pool;
    private int usedSize;
    private readonly bool fromCache;

    internal RentedTempBuffer(
        byte[]? buffer,
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
    /// Gets the underlying byte buffer.
    /// ✅ C# 14: Throw expression in property.
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
    /// ✅ C# 14: Null-forgiving operator with pattern.
    /// </summary>
    public readonly Span<byte> AsSpan() => buffer!.AsSpan();

    /// <summary>
    /// Gets a span view of the used portion.
    /// </summary>
    public readonly Span<byte> UsedSpan() => buffer!.AsSpan(0, usedSize);

    /// <summary>
    /// Gets a memory view of the buffer.
    /// </summary>
    public readonly Memory<byte> AsMemory() => buffer!.AsMemory();

    /// <summary>
    /// Returns the buffer to the pool.
    /// ✅ C# 14: is not null pattern.
    /// </summary>
    public void Dispose()
    {
        if (buffer is not null && pool is not null)  // ✅ C# 14: is not null
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
/// ✅ C# 14: ref struct with modern null checks.
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
    /// ✅ C# 14: Throw expression in property.
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
    public readonly Span<char> AsSpan() => buffer!.AsSpan();

    /// <summary>
    /// Gets a span view of the used portion.
    /// </summary>
    public readonly Span<char> UsedSpan() => buffer!.AsSpan(0, usedSize);

    /// <summary>
    /// Converts the used portion to a string.
    /// ALLOCATION: Creates a new string.
    /// </summary>
    public readonly override string ToString() => new(buffer!, 0, usedSize);  // ✅ C# 14: new() for string

    /// <summary>
    /// Returns the buffer to the pool.
    /// ✅ C# 14: is not null pattern.
    /// </summary>
    public void Dispose()
    {
        if (buffer is not null && pool is not null)  // ✅ C# 14: is not null
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
    /// <summary>
    /// Gets or sets the total number of byte buffers rented (lifetime).
    /// </summary>
    public long ByteBuffersRented { get; set; }
    
    /// <summary>
    /// Gets or sets the total number of char buffers rented (lifetime).
    /// </summary>
    public long CharBuffersRented { get; set; }
    
    /// <summary>
    /// Gets or sets the number of thread-local cache hits.
    /// </summary>
    public long CacheHits { get; set; }
    
    /// <summary>
    /// Gets or sets the number of thread-local cache misses.
    /// </summary>
    public long CacheMisses { get; set; }
    
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
}
