// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
namespace SharpCoreDB.Services;

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
        var parts = new List<string>(parameters.Count);
        foreach (var kv in parameters.OrderBy(k => k.Key))
        {
            var typeName = kv.Value?.GetType().Name ?? "null";
            parts.Add(kv.Key + ":" + typeName);
        }
        return normalizedSql + "|p:" + string.Join(',', parts);
    }

    /// <summary>
    /// Normalizes SQL by trimming, collapsing whitespace, and uppercasing keywords.
    /// Lightweight to maximize hit rate without changing semantics.
    /// </summary>
    public static string NormalizeSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;
        var s = sql.Trim();
        // Collapse multiple spaces
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ");
        return s;
    }
}
