namespace SharpCoreDB.Graph.Metrics;

/// <summary>
/// Immutable snapshot of all graph metrics at a point in time.
/// C# 14: Record struct for zero-allocation snapshots.
/// </summary>
public readonly record struct MetricSnapshot
{
    // Traversal Metrics
    /// <summary>Total nodes visited across all traversals.</summary>
    public long TotalNodesVisited { get; init; }
    
    /// <summary>Total edges traversed.</summary>
    public long TotalEdgesTraversed { get; init; }
    
    /// <summary>Maximum depth reached.</summary>
    public long MaxDepthReached { get; init; }
    
    /// <summary>Total result count across all queries.</summary>
    public long TotalResultCount { get; init; }
    
    /// <summary>Number of traversals executed.</summary>
    public long TraversalCount { get; init; }
    
    /// <summary>Average execution time per traversal.</summary>
    public TimeSpan AverageExecutionTime { get; init; }
    
    // Cache Metrics
    /// <summary>Total cache hits.</summary>
    public long CacheHits { get; init; }
    
    /// <summary>Total cache misses.</summary>
    public long CacheMisses { get; init; }
    
    /// <summary>Total cache evictions.</summary>
    public long CacheEvictions { get; init; }
    
    /// <summary>Cache hit rate (0.0 to 1.0).</summary>
    public double CacheHitRate => (CacheHits + CacheMisses) > 0
        ? CacheHits / (double)(CacheHits + CacheMisses)
        : 0.0;
    
    /// <summary>Average cache lookup time.</summary>
    public TimeSpan AverageLookupTime { get; init; }
    
    // Parallel Metrics
    /// <summary>Number of parallel traversals executed.</summary>
    public long ParallelTraversals { get; init; }
    
    /// <summary>Total work-stealing operations.</summary>
    public long TotalWorkStealingOps { get; init; }
    
    // Optimizer Metrics
    /// <summary>Number of optimizer invocations.</summary>
    public long OptimizerInvocations { get; init; }
    
    /// <summary>Average prediction error (0.0 to 1.0+).</summary>
    public double AveragePredictionError { get; init; }
    
    /// <summary>Number of strategy overrides.</summary>
    public long StrategyOverrides { get; init; }
    
    // Heuristic Metrics
    /// <summary>Total heuristic function calls.</summary>
    public long HeuristicCalls { get; init; }
    
    /// <summary>Admissible heuristic estimates.</summary>
    public long AdmissibleEstimates { get; init; }
    
    /// <summary>Over-estimates (non-admissible).</summary>
    public long OverEstimates { get; init; }
    
    /// <summary>Admissibility rate (0.0 to 1.0).</summary>
    public double AdmissibilityRate
    {
        get
        {
            var total = AdmissibleEstimates + OverEstimates;
            return total > 0 ? AdmissibleEstimates / (double)total : 0.0;
        }
    }
    
    /// <summary>Average heuristic evaluation time.</summary>
    public TimeSpan AverageHeuristicTime { get; init; }
    
    /// <summary>Timestamp when snapshot was taken.</summary>
    public DateTimeOffset Timestamp { get; init; }
    
    /// <summary>Creates a default empty snapshot.</summary>
    public static MetricSnapshot Empty => new()
    {
        Timestamp = DateTimeOffset.UtcNow
    };
}
