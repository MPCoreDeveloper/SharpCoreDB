// <copyright file="WalBufferPool.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

using System;
using System.Buffers;
using System.Threading;

/// <summary>
/// Specialized buffer pool for WAL operations - Core partial class.
/// Contains fields, constructor, configuration, and disposal logic.
/// PERFORMANCE: Uses ArrayPool with thread-local caching for optimal throughput.
/// MEMORY: Reuses large buffers (4MB) to minimize GC pressure.
/// THREAD-SAFETY: Completely lock-free when using thread-local cache.
/// </summary>
public partial class WalBufferPool : IDisposable
{
    private readonly ArrayPool<byte> bufferPool;
    private readonly int defaultBufferSize;
    private readonly ThreadLocal<BufferCache>? threadLocalCache;
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultBufferSize); // ✅ C# 14: ThrowIfNegativeOrZero

        this.defaultBufferSize = defaultBufferSize;
        bufferPool = ArrayPool<byte>.Shared;
        this.config = config ?? new() // ✅ C# 14: Target-typed new
        {
            UseThreadLocal = true,
            ThreadLocalCapacity = 2, // WAL typically needs 1-2 buffers per thread
            ClearBuffersOnReturn = true,
        };

        if (this.config.UseThreadLocal)
        {
            threadLocalCache = new ThreadLocal<BufferCache>(
                () => new BufferCache(this.config.ThreadLocalCapacity),
                trackAllValues: true); // SECURITY: Track all values to dispose them
        }
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
        if (disposed)
            return;

        if (disposing && threadLocalCache is not null) // ✅ C# 14: is not null pattern
        {
            // SECURITY: Dispose all thread-local caches to clear buffers
            foreach (var cache in threadLocalCache.Values)
            {
                cache?.Dispose();
            }
            
            threadLocalCache.Dispose();
        }

        disposed = true;
    }
}
