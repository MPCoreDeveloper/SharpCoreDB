namespace SharpCoreDB.Analytics.TimeSeries;

/// <summary>
/// Maintains a fixed-size rolling window for numeric aggregation.
/// </summary>
public sealed class RollingWindow(int windowSize)
{
    private readonly int _windowSize = ValidateWindowSize(windowSize);
    private readonly double[] _buffer = new double[windowSize];
    private int _count;
    private int _index;
    private double _sum;

    /// <summary>Gets the configured window size.</summary>
    public int WindowSize => _windowSize;

    /// <summary>Gets the current window count.</summary>
    public int Count => _count;

    /// <summary>Gets the rolling sum.</summary>
    public double? Sum => _count == 0 ? null : _sum;

    /// <summary>Gets the rolling average.</summary>
    public double? Average => _count == 0 ? null : _sum / _count;

    /// <summary>
    /// Adds a value to the rolling window.
    /// </summary>
    /// <param name="value">Value to add.</param>
    public void Add(double value)
    {
        if (_count < _windowSize)
        {
            _buffer[_count] = value;
            _sum += value;
            _count++;
            return;
        }

        var removed = _buffer[_index];
        _buffer[_index] = value;
        _sum += value - removed;
        _index = (_index + 1) % _windowSize;
    }

    private static int ValidateWindowSize(int size)
    {
        return size > 0 ? size : throw new ArgumentOutOfRangeException(nameof(size));
    }
}
