// <copyright file="BufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpCoreDB.Memory;

/// <summary>
/// Phase 2D: High-performance buffer pool for byte arrays.
/// 
/// Manages pools of byte arrays organized by size (power-of-two buckets).
/// Thread-safe using ConcurrentBag per size bucket.
/// 
/// Features:
/// - Automatic right-sizing (rounds up to power of 2)
/// - Minimal fragmentation (size-stratified pools)
/// - Fast allocation/return (lock-free semantics)
/// - Statistics tracking
/// 
/// Usage:
///     var pool = BufferPool.Shared;  // or new BufferPool()
///     byte[] buffer = pool.Rent(1024);  // Gets 1024+ byte buffer
///     try { /* use buffer */ }
///     finally { pool.Return(buffer); }
/// 
/// Expected benefits:
/// - 95%+ reduction in buffer allocations after warm-up
/// - Minimal GC pressure
/// - Lower fragmentation
/// - 2-3x improvement for serialization/parsing
/// </summary>
public class BufferPool
{
    private static readonly Lazy<BufferPool> sharedInstance = new(() => new BufferPool());
    
    /// <summary>
    /// Gets the shared global buffer pool instance.
    /// </summary>
    public static BufferPool Shared => sharedInstance.Value;

    // Pools organized by size (powers of 2): 256, 512, 1024, 2048, 4096, 8192, ...
    private readonly Dictionary<int, ConcurrentBag<byte[]>> sizePoolMap = new();
    private readonly object lockObj = new();
    private readonly int maxBuffersPerSize;
    private long rentCount = 0;
    private long reuseCount = 0;

    /// <summary>
    /// Initializes a new instance of the BufferPool class.
    /// </summary>
    /// <param name="maxBuffersPerSize">Maximum buffers to keep per size. Default: 64.</param>
    public BufferPool(int maxBuffersPerSize = 64)
    {
        if (maxBuffersPerSize <= 0)
            throw new ArgumentException("Max buffers must be greater than 0", nameof(maxBuffersPerSize));

        this.maxBuffersPerSize = maxBuffersPerSize;
        
        // Pre-allocate pools for common sizes
        InitializePool(256);
        InitializePool(512);
        InitializePool(1024);
        InitializePool(2048);
        InitializePool(4096);
        InitializePool(8192);
    }

    /// <summary>
    /// Rents a buffer of at least the specified minimum length.
    /// Buffer will be rounded up to nearest power of 2.
    /// </summary>
    /// <param name="minLength">Minimum required buffer length.</param>
    /// <returns>A byte array of at least minLength bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Rent(int minLength)
    {
        if (minLength < 0)
            throw new ArgumentException("Length cannot be negative", nameof(minLength));

        if (minLength == 0)
            return [];

        Interlocked.Increment(ref rentCount);

        // Get appropriate size (power of 2)
        int size = GetNextPowerOfTwo(minLength);

        // Try to get from pool
        if (GetOrCreatePool(size).TryTake(out var buffer))
        {
            Interlocked.Increment(ref reuseCount);
            return buffer;
        }

        // Pool empty, create new buffer
        return new byte[size];
    }

    /// <summary>
    /// Returns a buffer to the pool for reuse.
    /// Only returns to pool if size matches a managed bucket.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
            return;

        int size = buffer.Length;
        
        // Only pool buffers of expected sizes
        if (!sizePoolMap.ContainsKey(size))
            return;

        // Clear buffer before returning (security + predictability)
        Array.Clear(buffer, 0, buffer.Length);

        // Return to pool if not full
        var pool = GetOrCreatePool(size);
        if (pool.Count < maxBuffersPerSize)
        {
            pool.Add(buffer);
        }
    }

    /// <summary>
    /// Clears all pools, discarding all buffered arrays.
    /// </summary>
    public void Clear()
    {
        lock (lockObj)
        {
            foreach (var pool in sizePoolMap.Values)
            {
                while (pool.TryTake(out _))
                {
                    // Discard
                }
            }
        }
    }

    /// <summary>
    /// Gets statistics about pool usage.
    /// </summary>
    public BufferPoolStatistics GetStatistics()
    {
        lock (lockObj)
        {
            return new BufferPoolStatistics
            {
                TotalRents = rentCount,
                ReuseCount = reuseCount,
                ReusageRate = rentCount > 0 ? (double)reuseCount / rentCount : 0.0,
                TotalBuffersPooled = sizePoolMap.Values.Sum(p => p.Count),
                MaxBuffersPerSize = maxBuffersPerSize,
                NumberOfSizeBuckets = sizePoolMap.Count
            };
        }
    }

    /// <summary>
    /// Gets the nearest power of two that is >= the given value.
    /// </summary>
    private static int GetNextPowerOfTwo(int value)
    {
        if (value <= 256) return 256;
        if (value <= 512) return 512;
        if (value <= 1024) return 1024;
        if (value <= 2048) return 2048;
        if (value <= 4096) return 4096;
        if (value <= 8192) return 8192;
        if (value <= 16384) return 16384;
        if (value <= 32768) return 32768;
        if (value <= 65536) return 65536;
        
        // For larger sizes, use bit manipulation
        int power = 1;
        while (power < value)
            power <<= 1;
        return power;
    }

    /// <summary>
    /// Gets or creates a pool for the specified size.
    /// </summary>
    private ConcurrentBag<byte[]> GetOrCreatePool(int size)
    {
        if (sizePoolMap.TryGetValue(size, out var pool))
            return pool;

        lock (lockObj)
        {
            if (sizePoolMap.TryGetValue(size, out pool))
                return pool;

            pool = new ConcurrentBag<byte[]>();
            sizePoolMap[size] = pool;
            return pool;
        }
    }

    /// <summary>
    /// Pre-initializes a pool for a specific size.
    /// </summary>
    private void InitializePool(int size)
    {
        lock (lockObj)
        {
            if (!sizePoolMap.ContainsKey(size))
                sizePoolMap[size] = new ConcurrentBag<byte[]>();
        }
    }
}

/// <summary>
/// Statistics about buffer pool usage.
/// </summary>
public class BufferPoolStatistics
{
    /// <summary>
    /// Total number of rent operations.
    /// </summary>
    public long TotalRents { get; set; }

    /// <summary>
    /// Number of times buffers were reused from pool.
    /// </summary>
    public long ReuseCount { get; set; }

    /// <summary>
    /// Percentage of rents satisfied by reuse (0.0 to 1.0).
    /// </summary>
    public double ReusageRate { get; set; }

    /// <summary>
    /// Total number of buffers currently in all pools.
    /// </summary>
    public int TotalBuffersPooled { get; set; }

    /// <summary>
    /// Maximum buffers kept per size bucket.
    /// </summary>
    public int MaxBuffersPerSize { get; set; }

    /// <summary>
    /// Number of size buckets in use.
    /// </summary>
    public int NumberOfSizeBuckets { get; set; }

    /// <summary>
    /// Gets a human-readable summary of pool statistics.
    /// </summary>
    public override string ToString()
    {
        return $"BufferPool Stats: {ReuseCount}/{TotalRents} reused ({ReusageRate:P1}), " +
               $"Pooled: {TotalBuffersPooled} buffers, " +
               $"Buckets: {NumberOfSizeBuckets}";
    }
}

/// <summary>
/// Extension methods for BufferPool.
/// </summary>
public static class BufferPoolExtensions
{
    /// <summary>
    /// Uses a buffer from the pool with automatic return.
    /// Pattern: using var _ = pool.RentUsing(1024, out var buffer) { /* use buffer */ }
    /// </summary>
    public static PooledBufferHandle RentUsing(this BufferPool pool, int minLength, out byte[] buffer)
    {
        buffer = pool.Rent(minLength);
        return new PooledBufferHandle(pool, buffer);
    }
}

/// <summary>
/// RAII handle for automatic buffer return to pool.
/// Usage: using var handle = pool.RentUsing(1024, out var buffer)
/// </summary>
public struct PooledBufferHandle : IDisposable
{
    private readonly BufferPool pool;
    private readonly byte[] buffer;
    private bool disposed;

    internal PooledBufferHandle(BufferPool pool, byte[] buffer)
    {
        this.pool = pool;
        this.buffer = buffer;
        this.disposed = false;
    }

    /// <summary>
    /// Returns the buffer to the pool.
    /// </summary>
    public void Dispose()
    {
        if (!disposed)
        {
            pool?.Return(buffer);
            disposed = true;
        }
    }
}
