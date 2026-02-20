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
/// Supports both basic and advanced aggregates.
/// C# 14: Uses switch expressions for clean factory pattern.
/// </summary>
public static class AggregateFactory
{
    /// <summary>
    /// Creates an aggregate function by name.
    /// </summary>
    /// <param name="functionName">
    /// Name of the aggregate function (case-insensitive).
    /// Supported functions:
    /// - Basic: SUM, COUNT, AVG/AVERAGE, MIN, MAX
    /// - Statistical: STDDEV_SAMP, STDDEV_POP, VAR_SAMP, VAR_POP
    /// - Percentile: MEDIAN, PERCENTILE_* (e.g., PERCENTILE_95)
    /// - Frequency: MODE
    /// - Bivariate: CORR, COVAR_SAMP, COVAR_POP
    /// </param>
    /// <param name="parameters">
    /// Optional parameters for specific functions:
    /// - Percentile functions: percentile value (0.0 - 1.0)
    /// </param>
    /// <returns>Aggregate function instance.</returns>
    /// <exception cref="ArgumentException">If function name is unknown.</exception>
    public static IAggregateFunction CreateAggregate(string functionName, params object[] parameters)
    {
        var upperName = functionName.ToUpperInvariant();
        
        // Handle parameterized percentile functions (e.g., PERCENTILE_95)
        if (upperName.StartsWith("PERCENTILE_"))
        {
            var percentileStr = upperName["PERCENTILE_".Length..];
            if (double.TryParse(percentileStr, out var percentileValue))
            {
                return new PercentileAggregate(percentileValue / 100.0);
            }
        }
        
        return upperName switch
        {
            // Basic aggregates (Phase 9.1)
            "SUM" => new SumAggregate(),
            "COUNT" => new CountAggregate(),
            "AVG" or "AVERAGE" => new AverageAggregate(),
            "MIN" => new MinAggregate(),
            "MAX" => new MaxAggregate(),
            
            // Statistical aggregates (Phase 9.2)
            "STDDEV" or "STDDEV_SAMP" => new StandardDeviationAggregate(isSample: true),
            "STDDEV_POP" => new StandardDeviationAggregate(isSample: false),
            "VAR" or "VAR_SAMP" or "VARIANCE" => new VarianceAggregate(isSample: true),
            "VAR_POP" => new VarianceAggregate(isSample: false),
            
            // Percentile aggregates (Phase 9.2)
            "MEDIAN" => new MedianAggregate(),
            "PERCENTILE" => parameters.Length > 0 && parameters[0] is double p
                ? new PercentileAggregate(p)
                : throw new ArgumentException("PERCENTILE requires a percentile value (0.0-1.0)"),
            
            // Frequency aggregates (Phase 9.2)
            "MODE" => new ModeAggregate(),
            
            // Bivariate aggregates (Phase 9.2)
            "CORR" or "CORRELATION" => new CorrelationAggregate(),
            "COVAR" or "COVAR_SAMP" or "COVARIANCE" => new CovarianceAggregate(isSample: true),
            "COVAR_POP" => new CovarianceAggregate(isSample: false),
            
            _ => throw new ArgumentException($"Unknown aggregate function: {functionName}")
        };
    }
}
