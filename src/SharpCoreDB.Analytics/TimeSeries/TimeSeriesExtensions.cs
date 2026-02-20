namespace SharpCoreDB.Analytics.TimeSeries;

/// <summary>
/// Extension methods for time-series analytics.
/// </summary>
public static class TimeSeriesExtensions
{
    /// <summary>
    /// Groups a sequence into date buckets.
    /// </summary>
    public static IEnumerable<IGrouping<DateTime, T>> BucketByDate<T>(
        this IEnumerable<T> source,
        Func<T, DateTime> timestampSelector,
        DateBucket bucket)
    {
        return TimeSeriesAggregator.BucketByDate(source, timestampSelector, bucket);
    }

    /// <summary>
    /// Groups a sequence into custom time buckets.
    /// </summary>
    public static IEnumerable<IGrouping<DateTime, T>> BucketByTime<T>(
        this IEnumerable<T> source,
        Func<T, DateTime> timestampSelector,
        TimeSpan interval)
    {
        return TimeSeriesAggregator.BucketByTime(source, timestampSelector, interval);
    }

    /// <summary>
    /// Computes a rolling sum for a sequence.
    /// </summary>
    public static IEnumerable<double?> RollingSum<T>(
        this IEnumerable<T> source,
        Func<T, double> valueSelector,
        int windowSize)
    {
        return TimeSeriesAggregator.RollingSum(source, valueSelector, windowSize);
    }

    /// <summary>
    /// Computes a rolling average for a sequence.
    /// </summary>
    public static IEnumerable<double?> RollingAverage<T>(
        this IEnumerable<T> source,
        Func<T, double> valueSelector,
        int windowSize)
    {
        return TimeSeriesAggregator.RollingAverage(source, valueSelector, windowSize);
    }

    /// <summary>
    /// Computes a cumulative sum for a sequence.
    /// </summary>
    public static IEnumerable<double?> CumulativeSum<T>(
        this IEnumerable<T> source,
        Func<T, double> valueSelector)
    {
        return TimeSeriesAggregator.CumulativeSum(source, valueSelector);
    }

    /// <summary>
    /// Computes a cumulative average for a sequence.
    /// </summary>
    public static IEnumerable<double?> CumulativeAverage<T>(
        this IEnumerable<T> source,
        Func<T, double> valueSelector)
    {
        return TimeSeriesAggregator.CumulativeAverage(source, valueSelector);
    }
}
