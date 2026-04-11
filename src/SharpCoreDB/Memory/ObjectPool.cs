// <copyright file="ObjectPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpCoreDB.Memory;

/// <summary>
/// Phase 2D: Generic object pool for reusing objects and reducing allocations.
/// 
/// Thread-safe implementation using ConcurrentBag.
/// Supports optional reset action to restore object to initial state.
/// 
/// Usage:
///     var pool = new ObjectPool<MyObject>(maxSize: 100, resetAction: obj => obj.Reset());
///     var obj = pool.Rent();
///     try { /* use obj */ }
///     finally { pool.Return(obj); }
/// 
/// Expected benefits:
/// - 90%+ reduction in allocations after warm-up
/// - Significant GC pressure reduction
/// - Lower latency variance (fewer GC pauses)
/// - 2-4x improvement for allocation-heavy operations
/// </summary>
/// <typeparam name="T">Type of objects to pool. Should be a reference type.</typeparam>
/// <param name="maxPoolSize">Maximum number of objects to keep in the pool. Default: 100.</param>
/// <param name="resetAction">Optional action to reset object state when returned to pool.</param>
/// <param name="factory">Optional factory function to create new instances. If null, uses new T().</param>
public class ObjectPool<T>(int maxPoolSize = 100, Action<T>? resetAction = null, Func<T>? factory = null)
    where T : class, new()
{
    private readonly int _maxPoolSize = maxPoolSize > 0
        ? maxPoolSize
        : throw new ArgumentException("Pool size must be greater than 0", nameof(maxPoolSize));
    private readonly Action<T>? _resetAction = resetAction;
    private readonly Func<T>? _factory = factory;
    private readonly ConcurrentBag<T> _availableObjects = new();
    private long _rentCount;
    private long _reuseCount;

    /// <summary>
    /// Gets or creates an object from the pool.
    /// If pool is empty, creates a new instance.
    /// </summary>
    /// <returns>An object ready for use.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Rent()
    {
        Interlocked.Increment(ref _rentCount);

        if (_availableObjects.TryTake(out var obj))
        {
            Interlocked.Increment(ref _reuseCount);
            return obj;
        }

        // Pool empty, create new
        return _factory?.Invoke() ?? new T();
    }

    /// <summary>
    /// Returns an object to the pool for reuse.
    /// Calls reset action if provided, then returns to pool if not full.
    /// </summary>
    /// <param name="obj">The object to return to the pool.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T obj)
    {
        if (obj == null)
            return;

        // Reset object state
        _resetAction?.Invoke(obj);

        // Return to pool if not full
        if (_availableObjects.Count < _maxPoolSize)
        {
            _availableObjects.Add(obj);
        }
    }

    /// <summary>
    /// Clears the pool, discarding all pooled objects.
    /// </summary>
    public void Clear()
    {
        while (_availableObjects.TryTake(out _))
        {
            // Discard object
        }
    }

    /// <summary>
    /// Gets the number of objects currently in the pool.
    /// </summary>
    public int AvailableCount => _availableObjects.Count;

    /// <summary>
    /// Gets statistics about pool usage.
    /// </summary>
    public PoolStatistics GetStatistics()
    {
        return new PoolStatistics
        {
            TotalRents = _rentCount,
            ReuseCount = _reuseCount,
            ReusageRate = _rentCount > 0 ? (double)_reuseCount / _rentCount : 0.0,
            CurrentPoolSize = _availableObjects.Count,
            MaxPoolSize = _maxPoolSize
        };
    }
}

/// <summary>
/// Statistics about object pool usage.
/// </summary>
public sealed record PoolStatistics
{
    /// <summary>
    /// Total number of rent operations.
    /// </summary>
    public required long TotalRents { get; init; }

    /// <summary>
    /// Number of times objects were reused from pool.
    /// </summary>
    public required long ReuseCount { get; init; }

    /// <summary>
    /// Percentage of rents that were satisfied by reuse (0.0 to 1.0).
    /// </summary>
    public required double ReusageRate { get; init; }

    /// <summary>
    /// Current number of objects in the pool.
    /// </summary>
    public required int CurrentPoolSize { get; init; }

    /// <summary>
    /// Maximum size of the pool.
    /// </summary>
    public required int MaxPoolSize { get; init; }

    /// <summary>
    /// Gets a human-readable summary of pool statistics.
    /// </summary>
    public override string ToString()
    {
        return $"Pool Stats: {ReuseCount}/{TotalRents} reused ({ReusageRate:P1}), " +
               $"Size: {CurrentPoolSize}/{MaxPoolSize}";
    }
}

/// <summary>
/// Extension methods for ObjectPool.
/// </summary>
public static class ObjectPoolExtensions
{
    /// <summary>
    /// Uses an object from the pool with automatic return.
    /// Pattern: using var _ = pool.RentUsing(out var obj) { /* use obj */ }
    /// </summary>
    public static PooledObjectHandle<T> RentUsing<T>(this ObjectPool<T> pool, out T obj) where T : class, new()
    {
        obj = pool.Rent();
        return new PooledObjectHandle<T>(pool, obj);
    }
}

/// <summary>
/// RAII handle for automatic object return to pool.
/// Usage: using var handle = pool.RentUsing(out var obj)
/// </summary>
public struct PooledObjectHandle<T>(ObjectPool<T> pool, T obj) : IDisposable where T : class, new()
{
    private bool _disposed;

    /// <summary>
    /// Returns the object to the pool.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            pool?.Return(obj);
            _disposed = true;
        }
    }
}
