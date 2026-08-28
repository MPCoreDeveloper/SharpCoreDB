// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
namespace SharpCoreDB.Services;

using SharpCoreDB.Optimization;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

/// <summary>
/// LRU cache for compiled query plans keyed by normalized SQL + parameter shape.
/// Tracks hit/miss stats. Falls back to dynamic parsing on miss.
/// </summary>
public sealed class QueryPlanCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> map = new();
    private readonly LinkedList<string> lru = new();
    private readonly object lruLock = new();
    private readonly int capacity;
    private long hits;
    private long misses;

    /// <summary>
    /// Cache entry containing cached and compiled plan with metadata.
    /// </summary>
    public sealed class CacheEntry
    {
        /// <summary>Gets the cache key (normalized SQL + parameter shape).</summary>
        public string Key { get; init; } = string.Empty;
        /// <summary>Gets the cached query plan parts.</summary>
        public DataStructures.CachedQueryPlan CachedPlan { get; init; } = new("", []);
        /// <summary>Gets the compiled plan, if available.</summary>
        public DataStructures.CompiledQueryPlan? CompiledPlan { get; init; }
        /// <summary>Gets the optimized physical plan, if available.</summary>
        public Optimization.PhysicalPlan? OptimizedPlan { get; init; }
        /// <summary>Estimated cost of the optimized plan.</summary>
        public double OptimizedCost { get; init; }
        /// <summary>Gets the UTC timestamp when cached.</summary>
        public DateTime CachedAtUtc { get; init; }
        private long accessCount;
        /// <summary>Gets or sets the total access count.</summary>
        public long AccessCount
        {
            get => Interlocked.Read(ref accessCount);
            set => Interlocked.Exchange(ref accessCount, value);
        }
        internal void Touch() => Interlocked.Increment(ref accessCount);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryPlanCache"/> class.
    /// </summary>
    /// <param name="capacity">Maximum entries before LRU evicts.</param>
    public QueryPlanCache(int capacity)
    {
        this.capacity = Math.Max(16, capacity);
    }

    /// <summary>
    /// Gets existing entry or creates via factory and inserts with LRU maintenance.
    /// </summary>
    /// <param name="key">Normalized key.</param>
    /// <param name="factory">Factory to build entry on miss.</param>
    /// <returns>The cache entry.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CacheEntry GetOrAdd(string key, Func<string, CacheEntry> factory)
    {
        if (map.TryGetValue(key, out var entry))
        {
            Interlocked.Increment(ref hits);
            entry.Touch();
            UpdateLru(key);
            return entry;
        }

        Interlocked.Increment(ref misses);
        var created = factory(key);
        // Insert into dict
        map[key] = created;
        // LRU insert
        InsertLru(key);
        // Evict if needed
        if (map.Count > capacity)
        {
            EvictLeastRecent();
        }
        return created;
    }

    /// <summary>
    /// Tries to retrieve a cached plan without updating LRU or hit count.
    /// Used for validation/lookup only. Completely lock-free on hit path.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="entry">The cache entry if found, null otherwise.</param>
    /// <returns>True if found, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCachedPlan(string key, out CacheEntry? entry)
    {
        // Lock-free: direct dictionary lookup without any locking
        return map.TryGetValue(key, out entry);
    }

    /// <summary>
    /// Returns cache statistics.
    /// </summary>
    public (long Hits, long Misses, double HitRate, int Count) GetStatistics()
    {
        var h = Interlocked.Read(ref hits);
        var m = Interlocked.Read(ref misses);
        var total = h + m;
        var rate = total > 0 ? (double)h / total : 0d;
        return (h, m, rate, map.Count);
    }

    /// <summary>
    /// Clears cache and resets stats.
    /// </summary>
    public void Clear()
    {
        lock (lruLock)
        {
            map.Clear();
            lru.Clear();
            Interlocked.Exchange(ref hits, 0);
            Interlocked.Exchange(ref misses, 0);
        }
    }

    private void UpdateLru(string key)
    {
        lock (lruLock)
        {
            var node = lru.Find(key);
            if (node is not null)
            {
                lru.Remove(node);
                lru.AddFirst(node);
            }
        }
    }

    private void InsertLru(string key)
    {
        lock (lruLock)
        {
            lru.AddFirst(key);
        }
    }

    private void EvictLeastRecent()
    {
        lock (lruLock)
        {
            var tail = lru.Last;
            if (tail is null) return;
            var key = tail.Value;
            lru.RemoveLast();
            map.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Builds a cache key from normalized SQL and parameter shape (names ordered + types).
    /// </summary>
    public static string BuildKey(string normalizedSql, Dictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return normalizedSql + "|p:none";

        // v2 fast path: a single parameter avoids the OrderBy + list allocation.
        if (parameters.Count == 1)
        {
            foreach (var single in parameters)
            {
                var typeName = single.Value?.GetType().Name ?? "null";
                return normalizedSql + "|p:" + single.Key + ":" + typeName;
            }
        }

        var parts = new List<string>(parameters.Count);
        foreach (var kv in parameters.OrderBy(k => k.Key))
        {
            var typeName = kv.Value?.GetType().Name ?? "null";
            parts.Add(kv.Key + ":" + typeName);
        }
        return normalizedSql + "|p:" + string.Join(',', parts);
    }

    /// <summary>
    /// Normalizes SQL by trimming and collapsing whitespace.
    /// Lightweight to maximize hit rate without changing semantics.
    /// v2: manual whitespace collapse — the previous Regex.Replace allocated a Regex
    /// instance and intermediate strings on every query (hot path).
    /// </summary>
    public static string NormalizeSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        // v2 fast path: if the string is already trimmed and contains no whitespace runs,
        // return it unchanged (avoids an allocation on every cached-query call).
        bool needsNormalization = false;
        bool previousWasSpace = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v')
            {
                if (previousWasSpace || i == 0 || i == sql.Length - 1)
                {
                    needsNormalization = true;
                    break;
                }

                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }
        }

        return needsNormalization ? CollapseWhitespace(sql.Trim()) : sql;
    }

    /// <summary>
    /// Collapses runs of whitespace into a single space with zero regex/LINQ allocations.
    /// </summary>
    private static string CollapseWhitespace(string value)
    {
        if (value.Length == 0)
            return value;

        var buffer = new char[value.Length];
        int write = 0;
        bool previousWasSpace = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v')
            {
                if (!previousWasSpace)
                {
                    buffer[write++] = ' ';
                    previousWasSpace = true;
                }
            }
            else
            {
                buffer[write++] = c;
                previousWasSpace = false;
            }
        }

        return new string(buffer, 0, write);
    }
}
