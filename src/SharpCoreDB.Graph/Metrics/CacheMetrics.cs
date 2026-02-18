namespace SharpCoreDB.Graph.Metrics;

/// <summary>
/// Immutable snapshot of cache performance metrics.
/// C# 14: Record struct for zero-allocation snapshots.
/// </summary>
public readonly record struct CacheMetrics
{
    /// <summary>Total cache hits.</summary>
    public long Hits { get; init; }
    
    /// <summary>Total cache misses.</summary>
    public long Misses { get; init; }
    
    /// <summary>Cache hit rate (0.0 to 1.0).</summary>
    public double HitRate => (Hits + Misses) > 0 
        ? Hits / (double)(Hits + Misses) 
        : 0.0;
    
    /// <summary>Average cache lookup time.</summary>
    public TimeSpan AverageLookupTime { get; init; }
    
    /// <summary>Time spent constructing cached plans.</summary>
    public TimeSpan CacheConstructionTime { get; init; }
    
    /// <summary>Current number of cached entries.</summary>
    public int CurrentSize { get; init; }
    
    /// <summary>Maximum cache capacity.</summary>
    public int MaxSize { get; init; }
    
    /// <summary>Total evictions performed.</summary>
    public long Evictions { get; init; }
    
    /// <summary>Estimated memory usage in bytes.</summary>
    public long EstimatedMemoryBytes { get; init; }
    
    /// <summary>Timestamp when metrics were captured.</summary>
    public DateTimeOffset Timestamp { get; init; }
    
    /// <summary>Creates a default empty metrics instance.</summary>
    public static CacheMetrics Empty => new()
    {
        Timestamp = DateTimeOffset.UtcNow
    };
}
