namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Calculates the mode (most frequently occurring value).
/// Uses Dictionary to track value frequencies.
/// C# 14: Uses collection expressions for initialization.
/// </summary>
/// <remarks>
/// Mode is the value that appears most often in a dataset.
/// For ties (multimodal data), returns the first value to reach max frequency.
/// Memory: O(n) - tracks all unique values and their counts.
/// Time: O(n) - single pass through data.
/// </remarks>
public sealed class ModeAggregate : IAggregateFunction
{
    private readonly Dictionary<double, int> _frequencies = [];
    private double _currentMode = 0.0;
    private int _maxFrequency = 0;
    
    public string FunctionName => "MODE";
    
    /// <summary>
    /// Aggregates a single value and updates frequency tracking.
    /// </summary>
    /// <param name="value">Numeric value to aggregate. Null values are ignored.</param>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        var numValue = Convert.ToDouble(value);
        
        // Update frequency count
        if (_frequencies.TryGetValue(numValue, out var count))
        {
            _frequencies[numValue] = count + 1;
        }
        else
        {
            _frequencies[numValue] = 1;
        }
        
        // Track mode (most frequent value)
        if (_frequencies[numValue] > _maxFrequency)
        {
            _maxFrequency = _frequencies[numValue];
            _currentMode = numValue;
        }
    }
    
    /// <summary>
    /// Returns the mode (most frequent value).
    /// </summary>
    /// <returns>
    /// Most frequent value, or null if no values.
    /// For ties, returns the first value to reach maximum frequency.
    /// </returns>
    public object? GetResult()
    {
        if (_frequencies.Count == 0) return null;
        return _currentMode;
    }
    
    /// <summary>
    /// Resets the aggregate state.
    /// </summary>
    public void Reset()
    {
        _frequencies.Clear();
        _currentMode = 0.0;
        _maxFrequency = 0;
    }
}
