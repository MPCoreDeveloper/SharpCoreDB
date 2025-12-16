// <copyright file="WalBufferPool.Operations.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

using System;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// Specialized buffer pool for WAL operations - Operations partial class.
/// Contains Rent/Return operations for buffer management.
/// </summary>
public partial class WalBufferPool
{
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
        ObjectDisposedException.ThrowIf(disposed, this); // ✅ C# 14: ObjectDisposedException.ThrowIf

        Interlocked.Increment(ref buffersRented);

        byte[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is BufferCache cache) // ✅ C# 14: is pattern
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

        return new RentedBuffer(buffer, this, minimumSize, fromCache); // ✅ C# 14: Target-typed new (implicit in ref struct)
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
    internal void Return(byte[]? buffer, int actualSize, bool fromCache)
    {
        if (disposed || buffer is null) // ✅ C# 14: is null pattern
            return;

        Interlocked.Increment(ref buffersReturned);

        // Clear buffer if configured (SECURITY)
        if (config.ClearBuffersOnReturn && actualSize > 0)
        {
            // Only clear the portion that was actually used
            Array.Clear(buffer, 0, Math.Min(actualSize, buffer.Length));
        }

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is BufferCache cache) // ✅ C# 14: is pattern
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
    public WalBufferPoolStatistics GetStatistics() => // ✅ C# 14: Expression body
        new() // ✅ C# 14: Target-typed new
        {
            BuffersRented = Interlocked.Read(ref buffersRented),
            BuffersReturned = Interlocked.Read(ref buffersReturned),
            CacheHits = Interlocked.Read(ref cacheHits),
            CacheMisses = Interlocked.Read(ref cacheMisses),
            DefaultBufferSize = defaultBufferSize,
            ThreadLocalEnabled = config.UseThreadLocal,
            ThreadLocalCapacity = config.ThreadLocalCapacity,
        };

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
}
