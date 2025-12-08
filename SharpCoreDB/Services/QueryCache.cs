// <copyright file="QueryCache.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

/// <summary>
/// Caches parsed SQL queries for improved performance.
/// .NET 10 optimizations: AggressiveInlining, AggressiveOptimization for hot paths.
/// </summary>
public class QueryCache
{
    private readonly ConcurrentDictionary<string, CachedQuery> cache = new();
    private readonly int maxSize;
    private long hits = 0;
    private long misses = 0;
    private readonly ConcurrentDictionary<string, string> resultCache = new();

    /// <summary>
    /// Represents a cached query execution plan.
    /// </summary>
    public class CachedQuery
    {
        private long accessCount;

        /// <summary>Gets or sets the original SQL query string.</summary>
        public string Sql { get; set; } = string.Empty;

        /// <summary>Gets or sets the parsed parts of the SQL query.</summary>
        public string[] Parts { get; set; } = [];

        /// <summary>Gets or sets the timestamp when the query was cached.</summary>
        public DateTime CachedAt { get; set; }

        /// <summary>Gets or sets the number of times this query has been accessed.</summary>
        public long AccessCount
        {
            get => Interlocked.Read(ref this.accessCount);
            set => Interlocked.Exchange(ref this.accessCount, value);
        }

        internal void IncrementAccessCount()
        {
            Interlocked.Increment(ref this.accessCount);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryCache"/> class.
    /// </summary>
    /// <param name="maxSize">Maximum number of cached queries.</param>
    public QueryCache(int maxSize = 1024)
    {
        this.maxSize = maxSize;
    }

    /// <summary>
    /// Gets or adds a cached query.
    /// .NET 10: AggressiveInlining for hot path - called on every query.
    /// </summary>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CachedQuery GetOrAdd(string sql, Func<string, CachedQuery> factory)
    {
        if (this.cache.TryGetValue(sql, out var cached))
        {
            Interlocked.Increment(ref this.hits);
            cached.IncrementAccessCount();
            return cached;
        }

        Interlocked.Increment(ref this.misses);

        // Check cache size before adding
        if (this.cache.Count >= this.maxSize)
        {
            this.EvictLeastUsed();
        }

        var query = factory(sql);
        this.cache.TryAdd(sql, query);
        return query;
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns></returns>
    public (long Hits, long Misses, double HitRate, int Count) GetStatistics()
    {
        var hits = Interlocked.Read(ref this.hits);
        var misses = Interlocked.Read(ref this.misses);
        var total = hits + misses;
        var hitRate = total > 0 ? (double)hits / total : 0;
        return (hits, misses, hitRate, this.cache.Count);
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void Clear()
    {
        this.cache.Clear();
        this.resultCache.Clear();
        Interlocked.Exchange(ref this.hits, 0);
        Interlocked.Exchange(ref this.misses, 0);
    }

    public string? GetCachedResult(string sql) => this.resultCache.TryGetValue(sql, out var res) ? res : null;

    public void CacheResult(string sql, List<Dictionary<string, object>> results)
    {
        var json = OptimizedRowParser.SerializeRowsOptimized(results);
        this.resultCache[sql] = json;
    }

    private void EvictLeastUsed()
    {
        // Simple LRU: remove 10% of least accessed entries
        var toRemove = this.maxSize / 10;
        var leastUsed = this.cache
            .OrderBy(kv => kv.Value.AccessCount)
            .Take(toRemove)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in leastUsed)
        {
            this.cache.TryRemove(key, out _);
        }
    }
}
