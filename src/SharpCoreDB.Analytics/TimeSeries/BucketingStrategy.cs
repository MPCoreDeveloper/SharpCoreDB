namespace SharpCoreDB.Analytics.TimeSeries;

/// <summary>
/// Provides bucket key calculations for time-series grouping.
/// </summary>
public static class BucketingStrategy
{
    /// <summary>
    /// Gets the bucket start time for a date bucket.
    /// </summary>
    /// <param name="timestamp">Timestamp to bucket.</param>
    /// <param name="bucket">Bucket size.</param>
    /// <returns>Bucket start time in UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when bucket is unknown.</exception>
    public static DateTime GetBucketStart(DateTime timestamp, DateBucket bucket)
    {
        var normalized = NormalizeToUtc(timestamp);

        return bucket switch
        {
            DateBucket.Day => new DateTime(normalized.Year, normalized.Month, normalized.Day, 0, 0, 0, DateTimeKind.Utc),
            DateBucket.Week => StartOfWeek(normalized),
            DateBucket.Month => new DateTime(normalized.Year, normalized.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            DateBucket.Quarter => StartOfQuarter(normalized),
            DateBucket.Year => new DateTime(normalized.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => throw new ArgumentOutOfRangeException(nameof(bucket))
        };
    }

    /// <summary>
    /// Gets the bucket start time for a custom interval.
    /// </summary>
    /// <param name="timestamp">Timestamp to bucket.</param>
    /// <param name="interval">Bucket interval.</param>
    /// <returns>Bucket start time in UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when interval is not positive.</exception>
    public static DateTime GetBucketStart(DateTime timestamp, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        var normalized = NormalizeToUtc(timestamp);
        var ticks = normalized.Ticks / interval.Ticks * interval.Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static DateTime NormalizeToUtc(DateTime timestamp)
    {
        return timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
            _ => timestamp.ToUniversalTime()
        };
    }

    private static DateTime StartOfWeek(DateTime timestamp)
    {
        var diff = (7 + (timestamp.DayOfWeek - DayOfWeek.Monday)) % 7;
        return timestamp.Date.AddDays(-diff);
    }

    private static DateTime StartOfQuarter(DateTime timestamp)
    {
        var quarterMonth = ((timestamp.Month - 1) / 3) * 3 + 1;
        return new DateTime(timestamp.Year, quarterMonth, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
