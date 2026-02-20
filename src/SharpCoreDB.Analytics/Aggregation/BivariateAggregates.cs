namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Calculates Pearson correlation coefficient between two variables.
/// Uses online algorithm to avoid buffering all values.
/// C# 14: Uses primary constructor for configuration.
/// </summary>
/// <remarks>
/// Pearson correlation measures linear relationship between two variables:
/// - r = 1: Perfect positive correlation
/// - r = 0: No linear correlation
/// - r = -1: Perfect negative correlation
/// Formula: r = Σ((xi - x̄)(yi - ȳ)) / √(Σ(xi - x̄)² × Σ(yi - ȳ)²)
/// This is computed using an online algorithm for numerical stability.
/// </remarks>
public sealed class CorrelationAggregate : IAggregateFunction
{
    private int _count = 0;
    private double _meanX = 0.0;
    private double _meanY = 0.0;
    private double _m2X = 0.0;  // Sum of squared differences for X
    private double _m2Y = 0.0;  // Sum of squared differences for Y
    private double _coProduct = 0.0;  // Sum of products of differences
    private readonly List<(double x, double y)> _pairs = [];
    
    public string FunctionName => "CORR";
    
    /// <summary>
    /// Aggregates a pair of values (x, y).
    /// </summary>
    /// <param name="value">
    /// A tuple (x, y) or array [x, y] representing paired values.
    /// Null values are ignored.
    /// </param>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        // Extract x and y from tuple or array
        double x, y;
        if (value is ValueTuple<double, double> tuple)
        {
            (x, y) = tuple;
        }
        else if (value is double[] array && array.Length >= 2)
        {
            x = array[0];
            y = array[1];
        }
        else
        {
            // Store for later processing
            _pairs.Add((0, 0));
            return;
        }
        
        _count++;
        
        // Online algorithm for correlation (Welford-style)
        var deltaX = x - _meanX;
        var deltaY = y - _meanY;
        
        _meanX += deltaX / _count;
        _meanY += deltaY / _count;
        
        var deltaX2 = x - _meanX;
        var deltaY2 = y - _meanY;
        
        _m2X += deltaX * deltaX2;
        _m2Y += deltaY * deltaY2;
        _coProduct += deltaX * deltaY2;
    }
    
    /// <summary>
    /// Returns the Pearson correlation coefficient.
    /// </summary>
    /// <returns>
    /// Correlation coefficient between -1 and 1, or null if insufficient data.
    /// Returns null for n &lt; 2 or if standard deviation is zero.
    /// </returns>
    public object? GetResult()
    {
        if (_count < 2) return null;
        
        var stdX = Math.Sqrt(_m2X / _count);
        var stdY = Math.Sqrt(_m2Y / _count);
        
        if (stdX == 0 || stdY == 0) return null; // Undefined correlation
        
        return _coProduct / Math.Sqrt(_m2X * _m2Y);
    }
    
    /// <summary>
    /// Resets the aggregate state.
    /// </summary>
    public void Reset()
    {
        _count = 0;
        _meanX = 0.0;
        _meanY = 0.0;
        _m2X = 0.0;
        _m2Y = 0.0;
        _coProduct = 0.0;
        _pairs.Clear();
    }
}

/// <summary>
/// Calculates covariance between two variables.
/// Supports both population and sample covariance.
/// C# 14: Uses primary constructor for configuration.
/// </summary>
/// <remarks>
/// Covariance measures how two variables vary together:
/// - Positive: Variables tend to increase together
/// - Negative: One increases as the other decreases
/// - Zero: No linear relationship
/// Formula (population): Cov(X,Y) = Σ((xi - μx)(yi - μy)) / N
/// Formula (sample): Cov(X,Y) = Σ((xi - x̄)(yi - ȳ)) / (n-1)
/// </remarks>
public sealed class CovarianceAggregate(bool isSample = true) : IAggregateFunction
{
    private int _count = 0;
    private double _meanX = 0.0;
    private double _meanY = 0.0;
    private double _coProduct = 0.0;  // Sum of products of differences
    
    public string FunctionName => isSample ? "COVAR_SAMP" : "COVAR_POP";
    
    /// <summary>
    /// Aggregates a pair of values (x, y).
    /// </summary>
    /// <param name="value">
    /// A tuple (x, y) or array [x, y] representing paired values.
    /// Null values are ignored.
    /// </param>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        // Extract x and y from tuple or array
        double x, y;
        if (value is ValueTuple<double, double> tuple)
        {
            (x, y) = tuple;
        }
        else if (value is double[] array && array.Length >= 2)
        {
            x = array[0];
            y = array[1];
        }
        else
        {
            return;
        }
        
        _count++;
        
        // Online algorithm for covariance
        var deltaX = x - _meanX;
        var deltaY = y - _meanY;
        
        _meanX += deltaX / _count;
        _meanY += deltaY / _count;
        
        var deltaY2 = y - _meanY;
        _coProduct += deltaX * deltaY2;
    }
    
    /// <summary>
    /// Returns the covariance.
    /// </summary>
    /// <returns>
    /// Covariance, or null if insufficient data.
    /// Sample covariance returns null for n &lt; 2.
    /// </returns>
    public object? GetResult()
    {
        if (_count == 0) return null;
        if (_count == 1 && isSample) return null; // Sample covariance undefined for n=1
        
        var divisor = isSample ? _count - 1 : _count;
        return _coProduct / divisor;
    }
    
    /// <summary>
    /// Resets the aggregate state.
    /// </summary>
    public void Reset()
    {
        _count = 0;
        _meanX = 0.0;
        _meanY = 0.0;
        _coProduct = 0.0;
    }
}
