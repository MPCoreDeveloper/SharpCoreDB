namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Calculates standard deviation using Welford's online algorithm.
/// Supports both population and sample standard deviation.
/// C# 14: Uses primary constructor for immutable configuration.
/// </summary>
/// <remarks>
/// Welford's algorithm provides numerical stability by avoiding
/// catastrophic cancellation that can occur with naive two-pass algorithms.
/// Formula: σ = √(Σ(xi - μ)² / N) for population
///          s = √(Σ(xi - x̄)² / (n-1)) for sample
/// </remarks>
public sealed class StandardDeviationAggregate(bool isSample = true) : IAggregateFunction
{
    private int _count = 0;
    private double _mean = 0.0;
    private double _m2 = 0.0; // Sum of squared differences from mean
    
    public string FunctionName => isSample ? "STDDEV_SAMP" : "STDDEV_POP";
    
    /// <summary>
    /// Aggregates a single value using Welford's online algorithm.
    /// </summary>
    /// <param name="value">Numeric value to aggregate. Null values are ignored.</param>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        var numValue = Convert.ToDouble(value);
        _count++;
        
        // Welford's online algorithm for numerically stable variance calculation
        var delta = numValue - _mean;
        _mean += delta / _count;
        var delta2 = numValue - _mean;
        _m2 += delta * delta2;
    }
    
    /// <summary>
    /// Returns the standard deviation.
    /// </summary>
    /// <returns>
    /// Standard deviation, or null if no values.
    /// Sample stddev returns null for n=1 (undefined).
    /// </returns>
    public object? GetResult()
    {
        if (_count == 0) return null;
        if (_count == 1 && isSample) return null; // Sample stddev undefined for n=1
        
        var divisor = isSample ? _count - 1 : _count;
        var variance = _m2 / divisor;
        return Math.Sqrt(variance);
    }
    
    /// <summary>
    /// Resets the aggregate state.
    /// </summary>
    public void Reset()
    {
        _count = 0;
        _mean = 0.0;
        _m2 = 0.0;
    }
}

/// <summary>
/// Calculates variance (standard deviation squared).
/// Uses Welford's online algorithm for numerical stability.
/// C# 14: Uses primary constructor for configuration.
/// </summary>
/// <remarks>
/// Formula: σ² = Σ(xi - μ)² / N for population variance
///          s² = Σ(xi - x̄)² / (n-1) for sample variance
/// </remarks>
public sealed class VarianceAggregate(bool isSample = true) : IAggregateFunction
{
    private int _count = 0;
    private double _mean = 0.0;
    private double _m2 = 0.0; // Sum of squared differences from mean
    
    public string FunctionName => isSample ? "VAR_SAMP" : "VAR_POP";
    
    /// <summary>
    /// Aggregates a single value using Welford's online algorithm.
    /// </summary>
    /// <param name="value">Numeric value to aggregate. Null values are ignored.</param>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        var numValue = Convert.ToDouble(value);
        _count++;
        
        // Welford's online algorithm
        var delta = numValue - _mean;
        _mean += delta / _count;
        var delta2 = numValue - _mean;
        _m2 += delta * delta2;
    }
    
    /// <summary>
    /// Returns the variance.
    /// </summary>
    /// <returns>
    /// Variance, or null if no values.
    /// Sample variance returns null for n=1 (undefined).
    /// </returns>
    public object? GetResult()
    {
        if (_count == 0) return null;
        if (_count == 1 && isSample) return null; // Sample variance undefined for n=1
        
        var divisor = isSample ? _count - 1 : _count;
        return _m2 / divisor;
    }
    
    /// <summary>
    /// Resets the aggregate state.
    /// </summary>
    public void Reset()
    {
        _count = 0;
        _mean = 0.0;
        _m2 = 0.0;
    }
}
