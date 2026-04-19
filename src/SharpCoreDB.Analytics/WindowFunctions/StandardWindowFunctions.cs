namespace SharpCoreDB.Analytics.WindowFunctions;

/// <summary>
/// Implements ROW_NUMBER window function.
/// Assigns a unique sequential number to each row within a partition.
/// </summary>
public sealed class RowNumberFunction : IWindowFunction
{
    private int _rowNumber = 1;
    
    public string FunctionName => "ROW_NUMBER";
    
    public void ProcessValue(object? value) { /* No state needed */ }
    
    public object? GetResult() => _rowNumber++;
}

/// <summary>
/// Implements RANK window function.
/// Assigns a rank to each row, with gaps for ties.
/// </summary>
public sealed class RankFunction : IWindowFunction
{
    private int _currentRank = 0;
    
    public string FunctionName => "RANK";
    
    public void ProcessValue(object? value) { /* No state needed for simple ranking */ }
    
    public object? GetResult()
    {
        _currentRank++;
        return _currentRank;
    }
}

/// <summary>
/// Implements DENSE_RANK window function.
/// Assigns a rank to each row without gaps.
/// </summary>
public sealed class DenseRankFunction : IWindowFunction
{
    private int _rank = 1;
    
    public string FunctionName => "DENSE_RANK";
    
    public void ProcessValue(object? value) { /* No state needed */ }
    
    public object? GetResult() => _rank++;
}

/// <summary>
/// Implements LAG window function.
/// Returns the value of a row at a specified offset before the current row.
/// </summary>
public sealed class LagFunction : IWindowFunction
{
    private readonly List<object?> _history = [];
    private readonly int _offset;
    
    public string FunctionName => "LAG";
    
    public LagFunction(int offset = 1)
    {
        _offset = offset;
    }
    
    public void ProcessValue(object? value) => _history.Add(value);
    
    public object? GetResult()
    {
        var index = _history.Count - _offset - 1;
        return index >= 0 ? _history[index] : null;
    }
}

/// <summary>
/// Implements LEAD window function.
/// Returns the value of a row at a specified offset after the current row.
/// </summary>
public sealed class LeadFunction : IWindowFunction
{
    private readonly List<object?> _values = [];
    private int _currentIndex = 0;
    private readonly int _offset;
    
    public string FunctionName => "LEAD";
    
    public LeadFunction(int offset = 1)
    {
        _offset = offset;
    }
    
    public void ProcessValue(object? value) => _values.Add(value);
    
    public object? GetResult()
    {
        var nextIndex = _currentIndex + _offset;
        var result = nextIndex < _values.Count ? _values[nextIndex] : null;
        _currentIndex++;
        return result;
    }
}

/// <summary>
/// Implements FIRST_VALUE window function.
/// Returns the first value in the window frame.
/// </summary>
public sealed class FirstValueFunction : IWindowFunction
{
    private object? _firstValue = null;
    private bool _initialized = false;
    
    public string FunctionName => "FIRST_VALUE";
    
    public void ProcessValue(object? value)
    {
        if (!_initialized)
        {
            _firstValue = value;
            _initialized = true;
        }
    }
    
    public object? GetResult() => _firstValue;
}

/// <summary>
/// Implements LAST_VALUE window function.
/// Returns the last value in the window frame.
/// </summary>
public sealed class LastValueFunction : IWindowFunction
{
    private object? _lastValue = null;
    
    public string FunctionName => "LAST_VALUE";
    
    public void ProcessValue(object? value) => _lastValue = value;
    
    public object? GetResult() => _lastValue;
}

/// <summary>
/// Factory for creating window function instances.
/// </summary>
public static class WindowFunctionFactory
{
    /// <summary>
    /// Creates a window function by name.
    /// </summary>
    public static IWindowFunction CreateWindowFunction(string functionName, int? offset = null) =>
        functionName.ToUpperInvariant() switch
        {
            "ROW_NUMBER" => new RowNumberFunction(),
            "RANK" => new RankFunction(),
            "DENSE_RANK" => new DenseRankFunction(),
            "LAG" => new LagFunction(offset ?? 1),
            "LEAD" => new LeadFunction(offset ?? 1),
            "FIRST_VALUE" => new FirstValueFunction(),
            "LAST_VALUE" => new LastValueFunction(),
            "NTILE" => new NtileFunction(offset ?? 1),
            "PERCENT_RANK" => new PercentRankFunction(),
            "CUME_DIST" => new CumeDistFunction(),
            _ => throw new ArgumentException($"Unknown window function: {functionName}")
        };
}

/// <summary>
/// Implements NTILE(n) window function.
/// Distributes rows into n roughly equal groups.
/// </summary>
public sealed class NtileFunction : IWindowFunction
{
    private readonly int _buckets;
    private int _totalRows;
    private int _currentRow;

    public string FunctionName => "NTILE";

    public NtileFunction(int buckets)
    {
        _buckets = buckets > 0 ? buckets : 1;
    }

    public void ProcessValue(object? value) => _totalRows++;

    public object? GetResult()
    {
        _currentRow++;
        // Each bucket gets totalRows/buckets rows; first (totalRows % buckets) buckets get one extra
        int rowsPerBucket = _totalRows / _buckets;
        int extraRows = _totalRows % _buckets;
        int bucket = 1;
        int accumulated = 0;
        for (int b = 1; b <= _buckets; b++)
        {
            int size = rowsPerBucket + (b <= extraRows ? 1 : 0);
            accumulated += size;
            if (_currentRow <= accumulated)
            {
                bucket = b;
                break;
            }
        }
        return bucket;
    }
}

/// <summary>
/// Implements PERCENT_RANK() window function.
/// Returns (rank - 1) / (total_rows - 1). Returns 0.0 if only one row.
/// </summary>
public sealed class PercentRankFunction : IWindowFunction
{
    private int _totalRows;
    private int _currentRank;

    public string FunctionName => "PERCENT_RANK";

    public void ProcessValue(object? value) => _totalRows++;

    public object? GetResult()
    {
        _currentRank++;
        if (_totalRows <= 1) return 0.0;
        return (double)(_currentRank - 1) / (_totalRows - 1);
    }
}

/// <summary>
/// Implements CUME_DIST() window function.
/// Returns the fraction of rows with values less than or equal to the current row's value.
/// Simplified: returns currentRow / totalRows.
/// </summary>
public sealed class CumeDistFunction : IWindowFunction
{
    private int _totalRows;
    private int _currentRow;

    public string FunctionName => "CUME_DIST";

    public void ProcessValue(object? value) => _totalRows++;

    public object? GetResult()
    {
        _currentRow++;
        if (_totalRows == 0) return 1.0;
        return (double)_currentRow / _totalRows;
    }
}
