namespace SharpCoreDB.Analytics.TimeSeries;

/// <summary>
/// Provides streaming time-series aggregation helpers.
/// </summary>
public static class TimeSeriesAggregator
{
    /// <summary>
    /// Buckets a sequence of items by date bucket.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Source sequence.</param>
    /// <param name="timestampSelector">Timestamp selector.</param>
    /// <param name="bucket">Date bucket.</param>
    /// <returns>Grouped sequence by bucket start.</returns>
    public static IEnumerable<IGrouping<DateTime, T>> BucketByDate<T>(
        IEnumerable<T> source,
        Func<T, DateTime> timestampSelector,
        DateBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timestampSelector);

        return source.GroupBy(item => BucketingStrategy.GetBucketStart(timestampSelector(item), bucket));
    }

    /// <summary>
    /// Buckets a sequence of items by custom interval.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Source sequence.</param>
    /// <param name="timestampSelector">Timestamp selector.</param>
    /// <param name="interval">Bucket interval.</param>
    /// <returns>Grouped sequence by bucket start.</returns>
    public static IEnumerable<IGrouping<DateTime, T>> BucketByTime<T>(
        IEnumerable<T> source,
        Func<T, DateTime> timestampSelector,
        TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timestampSelector);

        return source.GroupBy(item => BucketingStrategy.GetBucketStart(timestampSelector(item), interval));
    }

    /// <summary>
    /// Computes a rolling sum for a sequence.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Source sequence.</param>
    /// <param name="valueSelector">Value selector.</param>
    /// <param name="windowSize">Window size.</param>
    /// <returns>Rolling sum values aligned to the source order.</returns>
    public static IEnumerable<double?> RollingSum<T>(
        IEnumerable<T> source,
        Func<T, double> valueSelector,
        int windowSize)
    {
        return ComputeRolling(source, valueSelector, windowSize, static window => window.Sum);
    }

    /// <summary>
    /// Computes a rolling average for a sequence.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Source sequence.</param>
    /// <param name="valueSelector">Value selector.</param>
    /// <param name="windowSize">Window size.</param>
    /// <returns>Rolling average values aligned to the source order.</returns>
    public static IEnumerable<double?> RollingAverage<T>(
        IEnumerable<T> source,
        Func<T, double> valueSelector,
        int windowSize)
    {
        return ComputeRolling(source, valueSelector, windowSize, static window => window.Average);
    }

    /// <summary>
    /// Computes a cumulative sum for a sequence.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Source sequence.</param>
    /// <param name="valueSelector">Value selector.</param>
    /// <returns>Cumulative sum values aligned to the source order.</returns>
    public static IEnumerable<double?> CumulativeSum<T>(
        IEnumerable<T> source,
        Func<T, double> valueSelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(valueSelector);

        double sum = 0;
        foreach (var item in source)
        {
            sum += valueSelector(item);
            yield return sum;
        }
    }

    /// <summary>
    /// Computes a cumulative average for a sequence.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">Source sequence.</param>
    /// <param name="valueSelector">Value selector.</param>
    /// <returns>Cumulative average values aligned to the source order.</returns>
    public static IEnumerable<double?> CumulativeAverage<T>(
        IEnumerable<T> source,
        Func<T, double> valueSelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(valueSelector);

        double sum = 0;
        var count = 0;
        foreach (var item in source)
        {
            sum += valueSelector(item);
            count++;
            yield return sum / count;
        }
    }

    private static IEnumerable<double?> ComputeRolling<T>(
        IEnumerable<T> source,
        Func<T, double> valueSelector,
        int windowSize,
        Func<RollingWindow, double?> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(valueSelector);
        ArgumentNullException.ThrowIfNull(selector);

        var window = new RollingWindow(windowSize);
        foreach (var item in source)
        {
            window.Add(valueSelector(item));
            yield return selector(window);
        }
    }
}
