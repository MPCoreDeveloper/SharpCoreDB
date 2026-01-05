// <copyright file="SubqueryCache.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Execution;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// Caches non-correlated subquery results.
/// HOT PATH - Thread-safe, zero-allocation lookups.
/// 
/// Design:
/// - Key: Query hash from SubqueryClassifier
/// - Value: Cached result (scalar value or row list)
/// - Invalidation: On table modifications (INSERT/UPDATE/DELETE)
/// 
/// Performance:
/// - Lookup: O(1) - Dictionary.TryGetValue
/// - Insert: O(1) - Dictionary.Add
/// - Invalidation: O(n) - scan all keys (rare operation)
/// 
/// Expected Speedup:
/// - Non-correlated subqueries: 100-1000x faster after caching
/// - Example: (SELECT MAX(price) FROM products) executed once per query
/// </summary>
public sealed class SubqueryCache
{
    private readonly Dictionary<string, CachedSubqueryResult> cache = [];
    private readonly ReaderWriterLockSlim rwLock = new(LockRecursionPolicy.NoRecursion);
    private long hits;
    private long misses;

    /// <summary>
    /// Gets or executes a cached subquery result.
    /// ✅ C# 14: Target-typed new, is patterns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public object? GetOrExecute(
        string cacheKey,
        SubqueryType type,
        Func<List<Dictionary<string, object>>> executor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(executor);

        // Try read with read lock
        rwLock.EnterReadLock();
        try
        {
            if (cache.TryGetValue(cacheKey, out var cached))
            {
                Interlocked.Increment(ref hits);
                return cached.Result;
            }
        }
        finally
        {
            rwLock.ExitReadLock();
        }

        // Cache miss - execute with write lock
        Interlocked.Increment(ref misses);

        rwLock.EnterWriteLock();
        try
        {
            // Double-check after acquiring write lock
            if (cache.TryGetValue(cacheKey, out var cached))
            {
                return cached.Result;
            }

            // Execute subquery
            var results = executor();

            // Extract result based on type
            var result = ExtractResult(results, type);

            // Cache result
            cache[cacheKey] = new CachedSubqueryResult
            {
                Result = result,
                Type = type,
                ExecutedAt = DateTime.UtcNow,
                ResultCount = results.Count
            };

            return result;
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Invalidates cached results for a specific table.
    /// Called after INSERT/UPDATE/DELETE operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Invalidate(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        rwLock.EnterWriteLock();
        try
        {
            // Find keys referencing this table
            // Simple implementation: check if key contains table name
            // Production: Store table dependencies in cache metadata
            List<string> keysToRemove = [];

            foreach (var key in cache.Keys)
            {
                if (key.Contains(tableName, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(key);
                }
            }

            // Remove cached results
            foreach (var key in keysToRemove)
            {
                cache.Remove(key);
            }
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Clears all cached results.
    /// </summary>
    public void Clear()
    {
        rwLock.EnterWriteLock();
        try
        {
            cache.Clear();
            Interlocked.Exchange(ref hits, 0);
            Interlocked.Exchange(ref misses, 0);
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public (long Hits, long Misses, double HitRate, int Count) GetStatistics()
    {
        var totalHits = Interlocked.Read(ref hits);
        var totalMisses = Interlocked.Read(ref misses);
        var total = totalHits + totalMisses;
        var hitRate = total > 0 ? (double)totalHits / total : 0;

        rwLock.EnterReadLock();
        try
        {
            return (totalHits, totalMisses, hitRate, cache.Count);
        }
        finally
        {
            rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Extracts the appropriate result from query results based on type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? ExtractResult(List<Dictionary<string, object>> results, SubqueryType type)
    {
        return type switch
        {
            SubqueryType.Scalar => ExtractScalarResult(results),
            SubqueryType.Row => ExtractRowResult(results),
            SubqueryType.Table => results, // Return full list
            _ => throw new InvalidOperationException($"Unknown subquery type: {type}")
        };
    }

    /// <summary>
    /// Extracts scalar result (single value) from query results.
    /// Returns first value from first row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? ExtractScalarResult(List<Dictionary<string, object>> results)
    {
        if (results.Count == 0)
        {
            return null; // No results - return NULL
        }

        if (results.Count > 1)
        {
            throw new InvalidOperationException("Scalar subquery returned multiple rows");
        }

        var firstRow = results[0];
        return firstRow.Count > 0 ? firstRow.Values.First() : null;
    }

    /// <summary>
    /// Extracts row result (single row with multiple columns).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Dictionary<string, object>? ExtractRowResult(List<Dictionary<string, object>> results)
    {
        if (results.Count == 0)
        {
            return null;
        }

        if (results.Count > 1)
        {
            throw new InvalidOperationException("Row subquery returned multiple rows");
        }

        return results[0];
    }
}

/// <summary>
/// Cached subquery result metadata.
/// ✅ C# 14: Required properties.
/// </summary>
internal sealed class CachedSubqueryResult
{
    /// <summary>Gets or sets the cached result value.</summary>
    public required object? Result { get; init; }

    /// <summary>Gets or sets the subquery type.</summary>
    public required SubqueryType Type { get; init; }

    /// <summary>Gets or sets when the result was cached.</summary>
    public required DateTime ExecutedAt { get; init; }

    /// <summary>Gets or sets the number of result rows.</summary>
    public required int ResultCount { get; init; }
}
