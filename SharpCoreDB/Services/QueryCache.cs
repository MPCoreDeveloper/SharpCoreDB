using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpCoreDB.Services;

/// <summary>
/// Caches parsed SQL queries for improved performance.
/// </summary>
public class QueryCache
{
    private readonly ConcurrentDictionary<string, CachedQuery> _cache = new();
    private readonly int _maxSize;
    private long _hits = 0;
    private long _misses = 0;

    /// <summary>
    /// Represents a cached query execution plan.
    /// </summary>
    public class CachedQuery
    {
        public string Sql { get; set; } = string.Empty;
        public string[] Parts { get; set; } = Array.Empty<string>();
        public DateTime CachedAt { get; set; }
        public long AccessCount { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the QueryCache class.
    /// </summary>
    /// <param name="maxSize">Maximum number of cached queries.</param>
    public QueryCache(int maxSize = 1000)
    {
        _maxSize = maxSize;
    }

    /// <summary>
    /// Gets or adds a cached query.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CachedQuery GetOrAdd(string sql, Func<string, CachedQuery> factory)
    {
        if (_cache.TryGetValue(sql, out var cached))
        {
            Interlocked.Increment(ref _hits);
            cached.AccessCount++;
            return cached;
        }

        Interlocked.Increment(ref _misses);

        // Check cache size before adding
        if (_cache.Count >= _maxSize)
        {
            EvictLeastUsed();
        }

        var query = factory(sql);
        _cache.TryAdd(sql, query);
        return query;
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public (long Hits, long Misses, double HitRate, int Count) GetStatistics()
    {
        var hits = Interlocked.Read(ref _hits);
        var misses = Interlocked.Read(ref _misses);
        var total = hits + misses;
        var hitRate = total > 0 ? (double)hits / total : 0;
        return (hits, misses, hitRate, _cache.Count);
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    private void EvictLeastUsed()
    {
        // Simple LRU: remove 10% of least accessed entries
        var toRemove = _maxSize / 10;
        var leastUsed = _cache
            .OrderBy(kv => kv.Value.AccessCount)
            .Take(toRemove)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in leastUsed)
        {
            _cache.TryRemove(key, out _);
        }
    }
}
