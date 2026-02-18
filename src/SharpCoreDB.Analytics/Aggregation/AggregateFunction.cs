namespace SharpCoreDB.Analytics;

/// <summary>
/// Base interface for all aggregate functions.
/// Supports streaming aggregation over partitions.
/// </summary>
public interface IAggregateFunction
{
    /// <summary>Gets the name of the aggregate function.</summary>
    string FunctionName { get; }
    
    /// <summary>Processes a single value in the aggregation.</summary>
    void Aggregate(object? value);
    
    /// <summary>Gets the final aggregate result.</summary>
    object? GetResult();
    
    /// <summary>Resets the aggregation state for a new group.</summary>
    void Reset();
}

/// <summary>
/// Context for executing aggregation operations.
/// </summary>
public class AggregationContext
{
    /// <summary>Gets the grouping key for this aggregation context.</summary>
    public object? GroupKey { get; set; }
    
    /// <summary>Gets the dictionary of aggregate functions being computed.</summary>
    public Dictionary<string, IAggregateFunction> Aggregates { get; } = new();
    
    /// <summary>Gets the count of items in this group.</summary>
    public long ItemCount { get; set; }
}

/// <summary>
/// Enumeration of standard aggregation strategies.
/// </summary>
public enum AggregationStrategy
{
    /// <summary>Stream-based aggregation (minimal memory).</summary>
    Streaming,
    
    /// <summary>Materialized aggregation (full data in memory).</summary>
    Materialized,
    
    /// <summary>Adaptive - choose based on data size.</summary>
    Adaptive
}
