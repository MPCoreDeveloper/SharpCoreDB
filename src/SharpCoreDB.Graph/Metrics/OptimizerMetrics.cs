namespace SharpCoreDB.Graph.Metrics;

using SharpCoreDB.Interfaces;

/// <summary>
/// Immutable snapshot of optimizer prediction metrics.
/// C# 14: Record struct for zero-allocation snapshots.
/// </summary>
public readonly record struct OptimizerMetrics
{
    /// <summary>Estimated execution cost (milliseconds).</summary>
    public double EstimatedCostMs { get; init; }
    
    /// <summary>Actual execution cost (milliseconds).</summary>
    public double ActualCostMs { get; init; }
    
    /// <summary>
    /// Prediction error ratio: |Estimated - Actual| / Actual.
    /// 0.0 = perfect prediction, >1.0 = off by more than 100%.
    /// </summary>
    public double PredictionError => ActualCostMs > 0
        ? Math.Abs(EstimatedCostMs - ActualCostMs) / ActualCostMs
        : 0.0;
    
    /// <summary>Estimated result cardinality.</summary>
    public long EstimatedCardinality { get; init; }
    
    /// <summary>Actual result cardinality.</summary>
    public long ActualCardinality { get; init; }
    
    /// <summary>
    /// Cardinality prediction error ratio.
    /// </summary>
    public double CardinalityError => ActualCardinality > 0
        ? Math.Abs(EstimatedCardinality - ActualCardinality) / (double)ActualCardinality
        : 0.0;
    
    /// <summary>Strategy recommended by optimizer.</summary>
    public GraphTraversalStrategy RecommendedStrategy { get; init; }
    
    /// <summary>Strategy actually executed.</summary>
    public GraphTraversalStrategy ActualStrategy { get; init; }
    
    /// <summary>Whether optimizer recommendation was overridden.</summary>
    public bool StrategyOverridden => RecommendedStrategy != ActualStrategy;
    
    /// <summary>Timestamp when metrics were captured.</summary>
    public DateTimeOffset Timestamp { get; init; }
    
    /// <summary>Creates a default empty metrics instance.</summary>
    public static OptimizerMetrics Empty => new()
    {
        Timestamp = DateTimeOffset.UtcNow
    };
}
