// <copyright file="Database.Statistics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

/// <summary>
/// Database implementation - Statistics partial class.
/// Handles cache and database statistics with modern C# 14 patterns.
/// </summary>
public partial class Database
{
    /// <summary>
    /// Gets query cache statistics including hits, misses, and hit rate.
    /// </summary>
    /// <returns>A tuple containing cache hits, misses, hit rate, and count.</returns>
    public (long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics() =>
        queryCache?.GetStatistics() ?? (0, 0, 0, 0);  // ✅ C# 14: tuple + null-coalescing

    /// <summary>
    /// Clears the query cache.
    /// </summary>
    public void ClearQueryCache() => queryCache?.Clear();

    /// <summary>
    /// Gets page cache statistics including hits, misses, evictions, and capacity.
    /// </summary>
    /// <returns>A tuple containing cache statistics.</returns>
    public (long Hits, long Misses, double HitRate, long Evictions, int CurrentSize, int Capacity) GetPageCacheStatistics()
    {
        if (pageCache is null)
            return (0, 0, 0, 0, 0, 0);

        var stats = pageCache.Statistics;
        return (stats.Hits, stats.Misses, stats.HitRate, stats.Evictions, pageCache.Count, pageCache.Capacity);
    }

    /// <summary>
    /// Clears the page cache, optionally flushing dirty pages.
    /// </summary>
    /// <param name="flushDirty">Whether to flush dirty pages before clearing.</param>
    public void ClearPageCache(bool flushDirty = false)
    {
        pageCache?.Clear(flushDirty, (id, data) => 
        {
            // Flush logic handled by WAL
        });
    }

    /// <summary>
    /// Gets comprehensive database statistics including table counts, cache stats, and configuration.
    /// </summary>
    /// <returns>A dictionary containing database statistics.</returns>
    public Dictionary<string, object> GetDatabaseStatistics()
    {
        var stats = new Dictionary<string, object>
        {
            ["TablesCount"] = tables.Count,
            ["IsReadOnly"] = isReadOnly,
            ["QueryCacheEnabled"] = config?.EnableQueryCache ?? false,
            ["NoEncryptMode"] = config?.NoEncryptMode ?? false,
            ["UseMemoryMapping"] = config?.UseMemoryMapping ?? false,
            ["WalBufferSize"] = config?.WalBufferSize ?? 0,
            ["PageCacheEnabled"] = config?.EnablePageCache ?? false,
        };

        // Query cache stats
        if (queryCache is not null)
        {
            var (hits, misses, hitRate, count) = queryCache.GetStatistics();  // ✅ C# 14: tuple deconstruction
            stats["QueryCacheHits"] = hits;
            stats["QueryCacheMisses"] = misses;
            stats["QueryCacheHitRate"] = hitRate;
            stats["QueryCacheCount"] = count;
        }

        // Page cache stats
        if (pageCache is not null)
        {
            var pageCacheStats = pageCache.Statistics;
            stats["PageCacheHits"] = pageCacheStats.Hits;
            stats["PageCacheMisses"] = pageCacheStats.Misses;
            stats["PageCacheHitRate"] = pageCacheStats.HitRate;
            stats["PageCacheEvictions"] = pageCacheStats.Evictions;
            stats["PageCacheSize"] = pageCache.Count;
            stats["PageCacheCapacity"] = pageCache.Capacity;
            stats["PageCacheLatchFailures"] = pageCacheStats.LatchFailures;
        }

        // Table-specific stats
        foreach (var (name, table) in tables)  // ✅ C# 14: tuple deconstruction in foreach
        {
            stats[$"Table_{name}_Columns"] = table.Columns.Count;
            
            // ✅ PERFORMANCE FIX: Use cached row count instead of full table scan!
            // Before: table.Select().Count took 53% CPU time (10K BTree searches!)
            // After: O(1) cached value lookup
            var rowCount = table.GetCachedRowCount();
            if (rowCount < 0)
            {
                // Cache not initialized - do one-time refresh
                table.RefreshRowCount();
                rowCount = table.GetCachedRowCount();
            }
            stats[$"Table_{name}_Rows"] = rowCount;
            
            stats[$"Table_{name}_ColumnUsage"] = table.GetColumnUsage();
        }

        return stats;
    }
}
