namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Implements the SUM aggregate function.
/// </summary>
public sealed class SumAggregate : IAggregateFunction
{
    private decimal _sum = 0;
    private bool _hasValue = false;
    
    public string FunctionName => "SUM";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        try
        {
            _sum += Convert.ToDecimal(value);
            _hasValue = true;
        }
        catch (InvalidCastException)
        {
            // Skip non-numeric values
        }
    }
    
    public object? GetResult() => _hasValue ? _sum : null;
    
    public void Reset()
    {
        _sum = 0;
        _hasValue = false;
    }
}

/// <summary>
/// Implements the COUNT aggregate function.
/// </summary>
public sealed class CountAggregate : IAggregateFunction
{
    private long _count = 0;
    
    public string FunctionName => "COUNT";
    
    public void Aggregate(object? value)
    {
        if (value is not null)
        {
            _count++;
        }
    }
    
    public object? GetResult() => _count;
    
    public void Reset() => _count = 0;
}

/// <summary>
/// Implements the AVERAGE aggregate function.
/// </summary>
public sealed class AverageAggregate : IAggregateFunction
{
    private decimal _sum = 0;
    private long _count = 0;
    
    public string FunctionName => "AVERAGE";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        try
        {
            _sum += Convert.ToDecimal(value);
            _count++;
        }
        catch (InvalidCastException)
        {
            // Skip non-numeric values
        }
    }
    
    public object? GetResult() => _count > 0 ? _sum / _count : null;
    
    public void Reset()
    {
        _sum = 0;
        _count = 0;
    }
}

/// <summary>
/// Implements the MIN aggregate function.
/// </summary>
public sealed class MinAggregate : IAggregateFunction
{
    private decimal? _min = null;
    
    public string FunctionName => "MIN";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        try
        {
            var decimalValue = Convert.ToDecimal(value);
            _min = _min is null ? decimalValue : Math.Min(_min.Value, decimalValue);
        }
        catch (InvalidCastException)
        {
            // Skip non-numeric values
        }
    }
    
    public object? GetResult() => _min;
    
    public void Reset() => _min = null;
}

/// <summary>
/// Implements the MAX aggregate function.
/// </summary>
public sealed class MaxAggregate : IAggregateFunction
{
    private decimal? _max = null;
    
    public string FunctionName => "MAX";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        try
        {
            var decimalValue = Convert.ToDecimal(value);
            _max = _max is null ? decimalValue : Math.Max(_max.Value, decimalValue);
        }
        catch (InvalidCastException)
        {
            // Skip non-numeric values
        }
    }
    
    public object? GetResult() => _max;
    
    public void Reset() => _max = null;
}

/// <summary>
/// Factory for creating aggregate function instances.
/// </summary>
public static class AggregateFactory
{
    /// <summary>
    /// Creates an aggregate function by name.
    /// </summary>
    public static IAggregateFunction CreateAggregate(string functionName) =>
        functionName.ToUpperInvariant() switch
        {
            "SUM" => new SumAggregate(),
            "COUNT" => new CountAggregate(),
            "AVG" or "AVERAGE" => new AverageAggregate(),
            "MIN" => new MinAggregate(),
            "MAX" => new MaxAggregate(),
            _ => throw new ArgumentException($"Unknown aggregate function: {functionName}")
        };
}
