// <copyright file="WalBufferPool.Diagnostics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Specialized buffer pool for WAL operations - Diagnostics partial class.
/// Contains nested types: BufferCache, RentedBuffer, WalBufferPoolStatistics.
/// </summary>
public partial class WalBufferPool
{
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
                entries[count++] = new BufferEntry { Buffer = buffer }; // ✅ C# 14: Target-typed new
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
                if (entries[i].Buffer is not null) // ✅ C# 14: is not null pattern
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
            if (disposed)
                return;

            Clear();
            disposed = true;
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
        ?? throw new ObjectDisposedException(nameof(RentedBuffer)); // ✅ C# 14: Throw expression

    /// <summary>
    /// Gets or sets the number of bytes actually used.
    /// SECURITY: Only this portion will be cleared on return if configured.
    /// </summary>
    public int UsedSize
    {
        readonly get => usedSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value); // ✅ C# 14: ThrowIfNegative
            if (buffer is not null && value > buffer.Length) // ✅ C# 14: is not null
                throw new ArgumentOutOfRangeException(nameof(value), "UsedSize exceeds buffer length");
            
            usedSize = value;
        }
    }

    /// <summary>
    /// Gets a span view of the buffer.
    /// OPTIMIZED: Zero-allocation access to buffer data.
    /// </summary>
    public readonly Span<byte> AsSpan() => buffer.AsSpan(); // ✅ C# 14: Expression body

    /// <summary>
    /// Gets a span view of the used portion of the buffer.
    /// OPTIMIZED: Zero-allocation access to used data only.
    /// </summary>
    public readonly Span<byte> UsedSpan() => buffer.AsSpan(0, usedSize); // ✅ C# 14: Expression body

    /// <summary>
    /// Gets a memory view of the buffer.
    /// OPTIMIZED: Zero-allocation async-compatible access.
    /// </summary>
    public readonly Memory<byte> AsMemory() => buffer.AsMemory(); // ✅ C# 14: Expression body

    /// <summary>
    /// Gets a memory view of the used portion.
    /// OPTIMIZED: Zero-allocation async-compatible access to used data only.
    /// </summary>
    public readonly Memory<byte> UsedMemory() => buffer.AsMemory(0, usedSize); // ✅ C# 14: Expression body

    /// <summary>
    /// Returns the buffer to the pool.
    /// SAFETY: Automatically called at end of 'using' block.
    /// IDEMPOTENT: Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (buffer is not null && pool is not null) // ✅ C# 14: is not null pattern
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
    public double CacheHitRate => CacheHits + CacheMisses > 0 // ✅ C# 14: Expression body
        ? (double)CacheHits / (CacheHits + CacheMisses)
        : 0.0;

    /// <summary>
    /// Gets the number of buffers currently outstanding.
    /// </summary>
    public long OutstandingBuffers => BuffersRented - BuffersReturned; // ✅ C# 14: Expression body
}
