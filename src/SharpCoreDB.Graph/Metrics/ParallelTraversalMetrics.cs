namespace SharpCoreDB.Graph.Metrics;

/// <summary>
/// Immutable snapshot of parallel traversal execution metrics.
/// C# 14: Record struct for zero-allocation snapshots.
/// </summary>
public readonly record struct ParallelTraversalMetrics
{
    /// <summary>Configured degree of parallelism.</summary>
    public int DegreeOfParallelism { get; init; }
    
    /// <summary>Peak number of active threads.</summary>
    public int PeakActiveThreads { get; init; }
    
    /// <summary>Total CPU time across all threads.</summary>
    public TimeSpan TotalThreadTime { get; init; }
    
    /// <summary>Wall-clock execution time.</summary>
    public TimeSpan WallClockTime { get; init; }
    
    /// <summary>
    /// Parallel speedup factor (sequential time / parallel time).
    /// Values >1.0 indicate speedup, <1.0 indicate overhead.
    /// </summary>
    public double ParallelSpeedup { get; init; }
    
    /// <summary>
    /// Parallel efficiency (speedup / DOP).
    /// Perfect scaling = 1.0, <1.0 indicates overhead/contention.
    /// </summary>
    public double ParallelEfficiency => DegreeOfParallelism > 0
        ? ParallelSpeedup / DegreeOfParallelism
        : 0.0;
    
    /// <summary>Nodes processed per thread.</summary>
    public long[] NodesPerThread { get; init; }
    
    /// <summary>Work-stealing operations performed.</summary>
    public long WorkStealingOperations { get; init; }
    
    /// <summary>Total idle time across all threads (milliseconds).</summary>
    public long IdleTimeMs { get; init; }
    
    /// <summary>Timestamp when metrics were captured.</summary>
    public DateTimeOffset Timestamp { get; init; }
    
    /// <summary>Creates a default empty metrics instance.</summary>
    public static ParallelTraversalMetrics Empty => new()
    {
        NodesPerThread = [],
        Timestamp = DateTimeOffset.UtcNow
    };
}
