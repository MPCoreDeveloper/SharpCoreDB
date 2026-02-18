namespace SharpCoreDB.Graph.Metrics;

using SharpCoreDB.Interfaces;

/// <summary>
/// Immutable snapshot of traversal execution metrics.
/// C# 14: Record struct for zero-allocation snapshots.
/// </summary>
public readonly record struct TraversalMetrics
{
    /// <summary>Total nodes visited during traversal.</summary>
    public long NodesVisited { get; init; }
    
    /// <summary>Total edges traversed.</summary>
    public long EdgesTraversed { get; init; }
    
    /// <summary>Maximum depth reached during traversal.</summary>
    public long MaxDepthReached { get; init; }
    
    /// <summary>Number of nodes in result set.</summary>
    public long ResultCount { get; init; }
    
    /// <summary>Wall-clock execution time.</summary>
    public TimeSpan ExecutionTime { get; init; }
    
    /// <summary>Traversal strategy used.</summary>
    public GraphTraversalStrategy Strategy { get; init; }
    
    /// <summary>Timestamp when metrics were captured.</summary>
    public DateTimeOffset Timestamp { get; init; }
    
    // Bidirectional-specific metrics
    /// <summary>Nodes explored from start (bidirectional only).</summary>
    public long ForwardNodesExplored { get; init; }
    
    /// <summary>Nodes explored from target (bidirectional only).</summary>
    public long BackwardNodesExplored { get; init; }
    
    /// <summary>Depth where paths met (bidirectional only).</summary>
    public long MeetingDepth { get; init; }
    
    // Dijkstra/A* specific metrics
    /// <summary>Priority queue operations (Dijkstra/A* only).</summary>
    public long PriorityQueueOperations { get; init; }
    
    /// <summary>Average edge weight encountered.</summary>
    public double AverageEdgeWeight { get; init; }
    
    /// <summary>Total path cost (weighted graphs).</summary>
    public double TotalPathCost { get; init; }
    
    /// <summary>Creates a default empty metrics instance.</summary>
    public static TraversalMetrics Empty => new()
    {
        Timestamp = DateTimeOffset.UtcNow
    };
}
