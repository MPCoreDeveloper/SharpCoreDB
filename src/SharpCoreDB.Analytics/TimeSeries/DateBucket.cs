namespace SharpCoreDB.Analytics.TimeSeries;

/// <summary>
/// Defines date-based bucket sizes for time-series grouping.
/// </summary>
public enum DateBucket
{
    /// <summary>Groups data by day.</summary>
    Day = 1,

    /// <summary>Groups data by week.</summary>
    Week = 2,

    /// <summary>Groups data by month.</summary>
    Month = 3,

    /// <summary>Groups data by quarter.</summary>
    Quarter = 4,

    /// <summary>Groups data by year.</summary>
    Year = 5
}
