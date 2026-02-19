namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Calculates median (50th percentile).
/// Requires buffering all values for sorting.
/// C# 14: Uses collection expressions for initialization.
/// </summary>
/// <remarks>
/// Median is the middle value when data is sorted.
/// For even count: returns average of two middle values.
/// For odd count: returns the middle value.
/// Memory: O(n) - buffers all values.
/// Time: O(n log n) - sorting required.
/// </remarks>
public sealed class MedianAggregate : IAggregateFunction
{
    private readonly List<double> _values = [];
    
    public string FunctionName => "MEDIAN";
    
    /// <summary>
    /// Aggregates a single value.
    /// </summary>
    /// <param name="value">Numeric value to aggregate. Null values are ignored.</param>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        _values.Add(Convert.ToDouble(value));
    }
    
    /// <summary>
    /// Returns the median value.
    /// </summary>
    /// <returns>
    /// Median value, or null if no values.
    /// For even count, returns average of two middle values.
    /// </returns>
    public object? GetResult()
    {
        if (_values.Count == 0) return null;
        
        var sorted = _values.ToArray();
        Array.Sort(sorted);
        
        var mid = sorted.Length / 2;
        
        if (sorted.Length % 2 == 0)
        {
            // Even count: average of two middle values
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
        else
        {
            // Odd count: middle value
            return sorted[mid];
        }
    }
    
    /// <summary>
    /// Resets the aggregate state.
    /// </summary>
    public void Reset() => _values.Clear();
}

/// <summary>
/// Calculates arbitrary percentile (0.0 - 1.0).
/// Uses linear interpolation for accuracy.
/// C# 14: Uses primary constructor for configuration.
/// </summary>
/// <remarks>
/// Percentile calculation using linear interpolation:
/// - P0 (0.0) = minimum value
/// - P50 (0.5) = median
/// - P95 (0.95) = 95th percentile (common SLA metric)
/// - P99 (0.99) = 99th percentile (tail latency)
/// - P100 (1.0) = maximum value
/// Formula: value = lower + (upper - lower) * fraction
/// </remarks>
public sealed class PercentileAggregate(double percentile) : IAggregateFunction
{
    private readonly List<double> _values = [];
    
    public string FunctionName => $"PERCENTILE_{percentile * 100:F0}";
    
    /// <summary>
    /// Aggregates a single value.
    /// </summary>
    /// <param name="value">Numeric value to aggregate. Null values are ignored.</param>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        _values.Add(Convert.ToDouble(value));
    }
    
    /// <summary>
    /// Returns the percentile value using linear interpolation.
    /// </summary>
    /// <returns>
    /// Percentile value, or null if no values.
    /// Uses linear interpolation between adjacent values.
    /// </returns>
    public object? GetResult()
    {
        if (_values.Count == 0) return null;
        
        var sorted = _values.ToArray();
        Array.Sort(sorted);
        
        // Handle boundary cases
        if (percentile <= 0.0) return sorted[0];
        if (percentile >= 1.0) return sorted[^1];
        
        // Calculate rank (0-based) with linear interpolation
        var rank = percentile * (sorted.Length - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        
        if (lowerIndex == upperIndex)
        {
            // Exact index - no interpolation needed
            return sorted[lowerIndex];
        }
        
        // Linear interpolation between adjacent values
        var lowerValue = sorted[lowerIndex];
        var upperValue = sorted[upperIndex];
        var fraction = rank - lowerIndex;
        
        return lowerValue + (upperValue - lowerValue) * fraction;
    }
    
    /// <summary>
    /// Resets the aggregate state.
    /// </summary>
    public void Reset() => _values.Clear();
}
