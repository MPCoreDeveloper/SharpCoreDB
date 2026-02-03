// <copyright file="BucketPartitioner.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Time-based bucket partitioner.
/// C# 14: Static class, aggressive optimization, modern patterns.
/// 
/// ✅ SCDB Phase 8.2: Bucket Storage System
/// 
/// Purpose:
/// - Calculate bucket boundaries from timestamps
/// - Map timestamps to bucket IDs
/// - Support multiple granularities
/// - Enable efficient time range queries
/// </summary>
public static class BucketPartitioner
{
    /// <summary>
    /// Gets the bucket start time for a given timestamp and granularity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime GetBucketStart(DateTime timestamp, BucketGranularity granularity)
    {
        return granularity switch
        {
            BucketGranularity.Minute => new DateTime(
                timestamp.Year, timestamp.Month, timestamp.Day,
                timestamp.Hour, timestamp.Minute, 0, timestamp.Kind),

            BucketGranularity.Hour => new DateTime(
                timestamp.Year, timestamp.Month, timestamp.Day,
                timestamp.Hour, 0, 0, timestamp.Kind),

            BucketGranularity.Day => new DateTime(
                timestamp.Year, timestamp.Month, timestamp.Day,
                0, 0, 0, timestamp.Kind),

            BucketGranularity.Week => GetWeekStart(timestamp),

            BucketGranularity.Month => new DateTime(
                timestamp.Year, timestamp.Month, 1,
                0, 0, 0, timestamp.Kind),

            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };
    }

    /// <summary>
    /// Gets the bucket end time (exclusive) for a given bucket start and granularity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime GetBucketEnd(DateTime bucketStart, BucketGranularity granularity)
    {
        return granularity switch
        {
            BucketGranularity.Minute => bucketStart.AddMinutes(1),
            BucketGranularity.Hour => bucketStart.AddHours(1),
            BucketGranularity.Day => bucketStart.AddDays(1),
            BucketGranularity.Week => bucketStart.AddDays(7),
            BucketGranularity.Month => bucketStart.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };
    }

    /// <summary>
    /// Generates a unique bucket ID for a given table, timestamp, and granularity.
    /// Format: {tableName}_{granularity}_{yyyyMMddHHmm}
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetBucketId(string tableName, DateTime timestamp, BucketGranularity granularity)
    {
        var bucketStart = GetBucketStart(timestamp, granularity);
        var suffix = granularity switch
        {
            BucketGranularity.Minute => bucketStart.ToString("yyyyMMddHHmm"),
            BucketGranularity.Hour => bucketStart.ToString("yyyyMMddHH"),
            BucketGranularity.Day => bucketStart.ToString("yyyyMMdd"),
            BucketGranularity.Week => $"{bucketStart:yyyyMMdd}W",
            BucketGranularity.Month => bucketStart.ToString("yyyyMM"),
            _ => bucketStart.ToString("yyyyMMddHHmmss")
        };

        return $"{tableName}_{granularity}_{suffix}";
    }

    /// <summary>
    /// Gets all bucket IDs that overlap with a time range.
    /// </summary>
    public static IEnumerable<string> GetBucketIdsForRange(
        string tableName,
        DateTime startTime,
        DateTime endTime,
        BucketGranularity granularity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (endTime <= startTime)
        {
            yield break;
        }

        var current = GetBucketStart(startTime, granularity);
        var end = endTime;

        while (current < end)
        {
            yield return GetBucketId(tableName, current, granularity);
            current = GetBucketEnd(current, granularity);
        }
    }

    /// <summary>
    /// Creates bucket metadata for a timestamp range.
    /// </summary>
    public static IEnumerable<TimeSeriesBucket> CreateBucketsForRange(
        string tableName,
        DateTime startTime,
        DateTime endTime,
        BucketGranularity granularity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (endTime <= startTime)
        {
            yield break;
        }

        var current = GetBucketStart(startTime, granularity);
        var end = endTime;

        while (current < end)
        {
            var bucketEnd = GetBucketEnd(current, granularity);

            yield return new TimeSeriesBucket
            {
                BucketId = GetBucketId(tableName, current, granularity),
                TableName = tableName,
                StartTime = current,
                EndTime = bucketEnd,
                Granularity = granularity,
                Tier = BucketTier.Hot
            };

            current = bucketEnd;
        }
    }

    /// <summary>
    /// Determines the optimal bucket granularity based on data characteristics.
    /// </summary>
    public static BucketGranularity RecommendGranularity(
        TimeSpan expectedQueryRange,
        long expectedPointsPerDay)
    {
        // Heuristics:
        // - For high-frequency data (>100K points/day), use Hour buckets
        // - For medium-frequency data (1K-100K points/day), use Day buckets
        // - For low-frequency data (<1K points/day), use Week/Month buckets

        if (expectedPointsPerDay > 100_000)
        {
            return BucketGranularity.Hour;
        }

        if (expectedPointsPerDay > 10_000)
        {
            return BucketGranularity.Day;
        }

        if (expectedPointsPerDay > 1_000)
        {
            return expectedQueryRange.TotalDays > 30
                ? BucketGranularity.Week
                : BucketGranularity.Day;
        }

        return expectedQueryRange.TotalDays > 90
            ? BucketGranularity.Month
            : BucketGranularity.Week;
    }

    /// <summary>
    /// Gets the duration of a bucket at the given granularity.
    /// </summary>
    public static TimeSpan GetBucketDuration(BucketGranularity granularity)
    {
        return granularity switch
        {
            BucketGranularity.Minute => TimeSpan.FromMinutes(1),
            BucketGranularity.Hour => TimeSpan.FromHours(1),
            BucketGranularity.Day => TimeSpan.FromDays(1),
            BucketGranularity.Week => TimeSpan.FromDays(7),
            BucketGranularity.Month => TimeSpan.FromDays(30), // Approximate
            _ => TimeSpan.FromDays(1)
        };
    }

    // Helper: Get Monday of the week containing timestamp
    private static DateTime GetWeekStart(DateTime timestamp)
    {
        int diff = (7 + (timestamp.DayOfWeek - DayOfWeek.Monday)) % 7;
        return new DateTime(
            timestamp.Year, timestamp.Month, timestamp.Day,
            0, 0, 0, timestamp.Kind).AddDays(-diff);
    }
}
