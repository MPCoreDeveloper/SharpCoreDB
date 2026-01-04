// <copyright file="Database.PlanCaching.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using SharpCoreDB.Services;
using SharpCoreDB.DataStructures;
using System.Runtime.CompilerServices;

/// <summary>
/// Database implementation - Query Plan Caching partial class.
/// Provides automatic, transparent query plan caching for SELECT, INSERT, UPDATE, DELETE.
/// 
/// Location: Database/Caching/Database.PlanCaching.cs
/// Purpose: Manage query plan cache lifecycle, normalize SQL, cache lookups
/// Performance: Lock-free reads on cache hit path, minimal overhead (10-20 cycles)
/// Thread-safety: Uses ConcurrentDictionary for thread-safe cache operations
/// </summary>
public partial class Database
{
    /// <summary>
    /// Gets the query plan cache, initializing if needed.
    /// Lazy initialization to avoid allocation for databases with caching disabled.
    /// </summary>
    private QueryPlanCache GetPlanCache()
    {
        // Lock-free fast path: if already initialized, return immediately (typical case)
        var cache = planCache;
        if (cache is not null)
            return cache;

        // Slow path: initialize on first access
        lock (_walLock)
        {
            cache = planCache;
            if (cache is null)
            {
                cache = planCache = new QueryPlanCache(config?.CompiledPlanCacheCapacity ?? BufferConstants.DEFAULT_QUERY_CACHE_SIZE);
            }
        }

        return cache;
    }

    /// <summary>
    /// Determines if query plan caching is enabled.
    /// Returns false if already disabled via config.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsPlanCachingEnabled() => config?.EnableCompiledPlanCache ?? true;

    /// <summary>
    /// Caches a query plan for DML operations (INSERT, UPDATE, DELETE).
    /// Normalizes SQL and parameters to maximize cache hit rate.
    /// </summary>
    /// <param name="sql">The SQL statement to cache.</param>
    /// <param name="parameters">Optional parameters bound to the query.</param>
    /// <param name="commandType">Type of command (SELECT, INSERT, UPDATE, DELETE).</param>
    /// <returns>Cached query plan entry, or null if caching disabled.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal QueryPlanCache.CacheEntry? GetOrAddPlan(string sql, Dictionary<string, object?>? parameters, SqlCommandType commandType)
    {
        if (!IsPlanCachingEnabled())
            return null;

        var normalized = (config?.NormalizeSqlForPlanCache ?? true) 
            ? NormalizeSqlForCaching(sql) 
            : sql;
        
        var key = BuildCacheKey(normalized, parameters, commandType);
        var cache = GetPlanCache();

        return cache.GetOrAdd(key, _ =>
        {
            var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cachedPlan = new CachedQueryPlan(sql, parts);
            
            return new QueryPlanCache.CacheEntry
            {
                Key = key,
                CachedPlan = cachedPlan,
                CompiledPlan = null,
                CachedAtUtc = DateTime.UtcNow
            };
        });
    }

    /// <summary>
    /// Retrieves a cached plan without modifying cache state.
    /// Used for validation/lookup only, does not update LRU.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CachedQueryPlan? TryGetCachedPlan(string sql, Dictionary<string, object?>? parameters, SqlCommandType commandType)
    {
        if (!IsPlanCachingEnabled() || planCache is null)
            return null;

        var normalized = (config?.NormalizeSqlForPlanCache ?? true) 
            ? NormalizeSqlForCaching(sql) 
            : sql;
        
        var key = BuildCacheKey(normalized, parameters, commandType);

        // Fast path: direct dictionary lookup without lock
        if (planCache.TryGetCachedPlan(key, out var entry))
            return entry?.CachedPlan;

        return null;
    }

    /// <summary>
    /// Normalizes SQL for plan caching by:
    /// - Trimming whitespace
    /// - Collapsing multiple spaces
    /// - Preserving original semantics
    /// 
    /// Avoids: uppercasing (preserves case), string allocations where possible.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormalizeSqlForCaching(ReadOnlySpan<char> sql)
    {
        if (sql.IsEmpty)
            return string.Empty;

        var trimmed = sql.Trim();
        if (trimmed.IsEmpty)
            return string.Empty;

        // Use QueryPlanCache's normalization (proven, tested)
        return QueryPlanCache.NormalizeSql(trimmed.ToString());
    }

    /// <summary>
    /// Builds a cache key combining:
    /// - Normalized SQL
    /// - Parameter names and types (if any)
    /// - Command type
    /// 
    /// Ensures different DML operations don't share cache entries.
    /// Example key: "INSERT INTO users VALUES|p:@name:String|INSERT"
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string BuildCacheKey(string normalizedSql, Dictionary<string, object?>? parameters, SqlCommandType commandType)
    {
        var baseKey = QueryPlanCache.BuildKey(normalizedSql, parameters);
        var cmdType = commandType.ToString().ToUpperInvariant();
        return $"{baseKey}|{cmdType}";
    }

    /// <summary>
    /// Clears the query plan cache.
    /// Called during Dispose or explicit cache reset.
    /// </summary>
    internal void ClearPlanCache()
    {
        planCache?.Clear();
        planCache = null;
    }

    /// <summary>
    /// Gets plan cache statistics for diagnostics.
    /// Returns zeros if cache not initialized or disabled.
    /// </summary>
    internal (long Hits, long Misses, double HitRate, int Count) GetPlanCacheStats()
    {
        return planCache?.GetStatistics() ?? (0, 0, 0, 0);
    }
}

/// <summary>
/// SQL command type enumeration for cache key differentiation.
/// Ensures INSERT, UPDATE, DELETE don't share cache entries despite similar query patterns.
/// </summary>
internal enum SqlCommandType
{
    /// <summary>SELECT statement</summary>
    SELECT = 0,

    /// <summary>INSERT statement</summary>
    INSERT = 1,

    /// <summary>UPDATE statement</summary>
    UPDATE = 2,

    /// <summary>DELETE statement</summary>
    DELETE = 3,

    /// <summary>Other statements (DDL, TCL, etc.)</summary>
    OTHER = 4
}
