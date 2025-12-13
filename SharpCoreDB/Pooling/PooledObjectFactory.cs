// <copyright file="PooledObjectFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

using Microsoft.Extensions.ObjectPool;
using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Base factory for creating pooled objects with proper initialization and cleanup.
/// THREAD-SAFETY: Factories are thread-safe and can be used from multiple threads.
/// MEMORY-SAFETY: All objects are properly reset before being returned to the pool.
/// </summary>
/// <typeparam name="T">The type of object to pool.</typeparam>
public abstract class PooledObjectFactory<T> : IPooledObjectPolicy<T> where T : class
{
    /// <summary>
    /// Creates a new instance of the pooled object.
    /// ALLOCATION: Called only when pool is empty or growing.
    /// </summary>
    /// <returns>A new instance of T.</returns>
    public abstract T Create();

    /// <summary>
    /// Returns an object to the pool after validation and cleanup.
    /// SAFETY: Must clear all sensitive data before returning to pool.
    /// THREAD-SAFETY: Return logic must be thread-safe.
    /// </summary>
    /// <param name="obj">The object to return to the pool.</param>
    /// <returns>True if the object is valid and can be reused; false to discard.</returns>
    public virtual bool Return(T obj)
    {
        if (obj == null)
        {
            return false;
        }

        try
        {
            // Validate object is in good state
            if (!ValidateObject(obj))
            {
                return false;
            }

            // Reset object state for reuse
            ResetObject(obj);

            return true;
        }
        catch
        {
            // SAFETY: If reset fails, discard the object
            return false;
        }
    }

    /// <summary>
    /// Validates that an object is in a reusable state.
    /// SAFETY: Override to add custom validation logic.
    /// </summary>
    /// <param name="obj">The object to validate.</param>
    /// <returns>True if valid; false otherwise.</returns>
    protected virtual bool ValidateObject(T obj)
    {
        return obj != null;
    }

    /// <summary>
    /// Resets an object to its initial state for reuse.
    /// SAFETY: Must clear all mutable state and sensitive data.
    /// SECURITY: Clear cryptographic material, keys, and sensitive buffers.
    /// </summary>
    /// <param name="obj">The object to reset.</param>
    protected abstract void ResetObject(T obj);
}

/// <summary>
/// Pool configuration settings for tuning performance and memory usage.
/// </summary>
public class PoolConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of objects retained in the pool.
    /// MEMORY: Higher values = more memory usage but fewer allocations.
    /// RECOMMENDATION: 10-50 for most scenarios, 100+ for high-throughput.
    /// </summary>
    public int MaximumRetained { get; set; } = 20;

    /// <summary>
    /// Gets or sets a value indicating whether to use thread-local pools for reduced contention.
    /// PERFORMANCE: Enables thread-local caching for zero-lock access.
    /// MEMORY: Increases memory usage (pool per thread).
    /// </summary>
    public bool UseThreadLocal { get; set; } = true;

    /// <summary>
    /// Gets or sets the thread-local pool capacity.
    /// THREAD-SAFETY: Each thread maintains its own cache up to this size.
    /// RECOMMENDATION: 2-5 for most scenarios.
    /// </summary>
    public int ThreadLocalCapacity { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether to validate objects on return.
    /// SAFETY: Enables validation but adds overhead.
    /// RECOMMENDATION: Enable in debug builds, disable in release for performance.
    /// </summary>
    public bool ValidateOnReturn { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to clear buffers on return.
    /// SECURITY: Prevents data leakage between pool uses.
    /// RECOMMENDATION: Always enable for buffers containing sensitive data.
    /// </summary>
    public bool ClearBuffersOnReturn { get; set; } = true;
}

/// <summary>
/// Thread-local pool wrapper for zero-contention object access.
/// PERFORMANCE: Eliminates lock contention by maintaining per-thread pools.
/// MEMORY: Uses more memory but provides better throughput under load.
/// </summary>
/// <typeparam name="T">The type of object to pool.</typeparam>
public class ThreadLocalPool<T> where T : class
{
    private readonly ObjectPool<T> globalPool;
    private readonly ThreadLocal<LocalCache<T>> threadLocalCache;
    private readonly int threadLocalCapacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadLocalPool{T}"/> class.
    /// </summary>
    /// <param name="policy">The pooled object policy.</param>
    /// <param name="config">The pool configuration.</param>
    public ThreadLocalPool(IPooledObjectPolicy<T> policy, PoolConfiguration config)
    {
        // Global pool for overflow
        globalPool = new DefaultObjectPool<T>(policy, config.MaximumRetained);
        threadLocalCapacity = config.ThreadLocalCapacity;

        // Thread-local cache for zero-lock access
        threadLocalCache = new ThreadLocal<LocalCache<T>>(
            () => new LocalCache<T>(threadLocalCapacity),
            trackAllValues: false);
    }

    /// <summary>
    /// Gets an object from the pool.
    /// PERFORMANCE: First tries thread-local cache (zero locks), then global pool.
    /// </summary>
    /// <returns>An object from the pool.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get()
    {
        var cache = threadLocalCache.Value;

        // Try thread-local cache first (zero contention)
        if (cache != null && cache.TryGet(out var obj))
        {
            return obj!;
        }

        // Fall back to global pool (lock-based)
        return globalPool.Get();
    }

    /// <summary>
    /// Returns an object to the pool.
    /// PERFORMANCE: First tries thread-local cache (zero locks), then global pool.
    /// SAFETY: Object must be properly reset before calling Return.
    /// </summary>
    /// <param name="obj">The object to return.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T obj)
    {
        var cache = threadLocalCache.Value;

        // Try thread-local cache first (zero contention)
        if (cache != null && cache.TryReturn(obj))
        {
            return;
        }

        // Fall back to global pool (lock-based)
        globalPool.Return(obj);
    }

    /// <summary>
    /// Gets statistics about pool usage.
    /// DIAGNOSTICS: Use for monitoring and tuning.
    /// </summary>
    /// <returns>Pool statistics.</returns>
    public PoolStatistics GetStatistics()
    {
        // Note: This is a simplified implementation
        // In production, you'd track more detailed metrics
        return new PoolStatistics
        {
            ThreadLocalCapacity = threadLocalCapacity,
            IsThreadLocalEnabled = true,
        };
    }

    /// <summary>
    /// Thread-local cache for zero-contention access.
    /// IMPLEMENTATION: Simple array-based stack for maximum performance.
    /// </summary>
    private sealed class LocalCache<TItem> where TItem : class
    {
        private readonly TItem?[] items;
        private int count;

        public LocalCache(int capacity)
        {
            items = new TItem?[capacity];
            count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(out TItem? item)
        {
            if (count > 0)
            {
                item = items[--count];
                items[count] = null; // Clear reference
                return item != null;
            }

            item = null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReturn(TItem item)
        {
            if (count < items.Length)
            {
                items[count++] = item;
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Pool statistics for monitoring and diagnostics.
/// </summary>
public class PoolStatistics
{
    /// <summary>
    /// Gets or sets the thread-local cache capacity.
    /// </summary>
    public int ThreadLocalCapacity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether thread-local pooling is enabled.
    /// </summary>
    public bool IsThreadLocalEnabled { get; set; }

    /// <summary>
    /// Gets or sets the number of objects created (lifetime).
    /// </summary>
    public long ObjectsCreated { get; set; }

    /// <summary>
    /// Gets or sets the number of objects reused (lifetime).
    /// </summary>
    public long ObjectsReused { get; set; }

    /// <summary>
    /// Gets the pool hit rate (0.0 to 1.0).
    /// </summary>
    public double HitRate => ObjectsReused > 0
        ? (double)ObjectsReused / (ObjectsCreated + ObjectsReused)
        : 0.0;
}
