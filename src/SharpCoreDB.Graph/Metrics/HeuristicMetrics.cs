namespace SharpCoreDB.Graph.Metrics;

/// <summary>
/// Immutable snapshot of A* heuristic performance metrics.
/// C# 14: Record struct for zero-allocation snapshots.
/// </summary>
public readonly record struct HeuristicMetrics
{
    /// <summary>Total nodes explored during A* search.</summary>
    public long NodesExplored { get; init; }
    
    /// <summary>Length of optimal path found.</summary>
    public long OptimalPathLength { get; init; }
    
    /// <summary>
    /// Heuristic efficiency: OptimalPathLength / NodesExplored.
    /// Perfect heuristic = 1.0, values closer to 1.0 = better guidance.
    /// </summary>
    public double HeuristicEfficiency => NodesExplored > 0
        ? OptimalPathLength / (double)NodesExplored
        : 0.0;
    
    /// <summary>Number of admissible heuristic estimates (h(n) <= actual cost).</summary>
    public long AdmissibleEstimates { get; init; }
    
    /// <summary>Number of over-estimates (h(n) > actual cost).</summary>
    public long OverEstimates { get; init; }
    
    /// <summary>
    /// Admissibility rate: AdmissibleEstimates / (AdmissibleEstimates + OverEstimates).
    /// 1.0 = perfectly admissible, <1.0 = some over-estimates.
    /// </summary>
    public double AdmissibilityRate
    {
        get
        {
            var total = AdmissibleEstimates + OverEstimates;
            return total > 0 ? AdmissibleEstimates / (double)total : 0.0;
        }
    }
    
    /// <summary>Total time spent evaluating heuristic function.</summary>
    public TimeSpan HeuristicEvaluationTime { get; init; }
    
    /// <summary>Number of heuristic function calls.</summary>
    public long HeuristicCalls { get; init; }
    
    /// <summary>Average heuristic evaluation time per call.</summary>
    public TimeSpan AverageEvaluationTime => HeuristicCalls > 0
        ? TimeSpan.FromTicks(HeuristicEvaluationTime.Ticks / HeuristicCalls)
        : TimeSpan.Zero;
    
    /// <summary>Timestamp when metrics were captured.</summary>
    public DateTimeOffset Timestamp { get; init; }
    
    /// <summary>Creates a default empty metrics instance.</summary>
    public static HeuristicMetrics Empty => new()
    {
        Timestamp = DateTimeOffset.UtcNow
    };
}
