// <copyright file="TemporaryBufferPool.Operations.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// TemporaryBufferPool - Rent and return operations.
/// Contains buffer rental, return logic, and thread-local cache operations.
/// Part of the TemporaryBufferPool partial class.
/// Modern C# 14 with throw expressions and pattern matching.
/// See also: TemporaryBufferPool.Core.cs, TemporaryBufferPool.Diagnostics.cs
/// </summary>
public partial class TemporaryBufferPool
{
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
    /// ✅ C# 14: ObjectDisposedException.ThrowIf and modern null checks.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented temporary byte buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public RentedTempBuffer RentByteBuffer(int minimumSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);  // ✅ C# 14: ThrowIf

        Interlocked.Increment(ref byteBuffersRented);

        byte[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)  // ✅ C# 14: is pattern
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

        return new RentedTempBuffer(buffer, this, minimumSize, fromCache);
    }

    /// <summary>
    /// Rents a char buffer for string operations.
    /// USAGE: String building, UTF-8 to char conversion, text processing.
    /// ✅ C# 14: ObjectDisposedException.ThrowIf.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented temporary char buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public RentedTempCharBuffer RentCharBuffer(int minimumSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);  // ✅ C# 14: ThrowIf

        Interlocked.Increment(ref charBuffersRented);
        
        char[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)  // ✅ C# 14: is pattern
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
    /// ✅ C# 14: is null pattern for early return.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReturnByteBuffer(byte[] buffer, bool fromCache)
    {
        if (disposed || buffer is null) return;  // ✅ C# 14: is null

        // Optionally clear if configured
        if (config.ClearBuffersOnReturn)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)  // ✅ C# 14: is pattern
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
    /// ✅ C# 14: is null pattern for early return.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReturnCharBuffer(char[] buffer, bool fromCache)
    {
        if (disposed || buffer is null) return;  // ✅ C# 14: is null

        // Optionally clear if configured
        if (config.ClearBuffersOnReturn)
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is TempBufferCache cache)  // ✅ C# 14: is pattern
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
}
