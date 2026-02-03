// <copyright file="TimeSeriesBucket.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;

/// <summary>
/// Time-series bucket for partitioned storage.
/// C# 14: Record types, required members, modern patterns.
/// 
/// ✅ SCDB Phase 8.2: Bucket Storage System
/// 
/// Purpose:
/// - Partition time-series data by time intervals
/// - Enable fast time range queries
/// - Support tiered storage (Hot/Warm/Cold)
/// - Track metadata for query optimization
/// </summary>
public sealed record TimeSeriesBucket
{
    /// <summary>Unique bucket identifier.</summary>
    public required string BucketId { get; init; }

    /// <summary>Table this bucket belongs to.</summary>
    public required string TableName { get; init; }

    /// <summary>Bucket start time (inclusive).</summary>
    public required DateTime StartTime { get; init; }

    /// <summary>Bucket end time (exclusive).</summary>
    public required DateTime EndTime { get; init; }

    /// <summary>Bucket time granularity.</summary>
    public required BucketGranularity Granularity { get; init; }

    /// <summary>Storage tier (Hot/Warm/Cold).</summary>
    public BucketTier Tier { get; set; } = BucketTier.Hot;

    /// <summary>Number of data points in bucket.</summary>
    public long RowCount { get; set; }

    /// <summary>Compressed data size in bytes.</summary>
    public long CompressedSize { get; set; }

    /// <summary>Uncompressed data size in bytes.</summary>
    public long UncompressedSize { get; set; }

    /// <summary>Minimum timestamp in bucket (actual data).</summary>
    public DateTime? MinTimestamp { get; set; }

    /// <summary>Maximum timestamp in bucket (actual data).</summary>
    public DateTime? MaxTimestamp { get; set; }

    /// <summary>When the bucket was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>When the bucket was last modified.</summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the bucket is sealed (read-only).</summary>
    public bool IsSealed { get; set; }

    /// <summary>Compression ratio (uncompressed/compressed).</summary>
    public double CompressionRatio => CompressedSize > 0
        ? (double)UncompressedSize / CompressedSize
        : 1.0;

    /// <summary>Checks if a timestamp falls within this bucket's range.</summary>
    public bool ContainsTimestamp(DateTime timestamp)
    {
        return timestamp >= StartTime && timestamp < EndTime;
    }

    /// <summary>Checks if a time range overlaps with this bucket.</summary>
    public bool OverlapsRange(DateTime rangeStart, DateTime rangeEnd)
    {
        return rangeStart < EndTime && rangeEnd > StartTime;
    }
}

/// <summary>
/// Bucket time granularity.
/// </summary>
public enum BucketGranularity
{
    /// <summary>One minute per bucket.</summary>
    Minute = 1,

    /// <summary>One hour per bucket.</summary>
    Hour = 2,

    /// <summary>One day per bucket.</summary>
    Day = 3,

    /// <summary>One week per bucket.</summary>
    Week = 4,

    /// <summary>One month per bucket.</summary>
    Month = 5,
}

/// <summary>
/// Storage tier for bucket data.
/// </summary>
public enum BucketTier
{
    /// <summary>Hot tier: Recent data, uncompressed, fast writes.</summary>
    Hot = 1,

    /// <summary>Warm tier: Compressed data, fast reads.</summary>
    Warm = 2,

    /// <summary>Cold tier: Archived data, slow access.</summary>
    Cold = 3,
}

/// <summary>
/// Time-series data point.
/// </summary>
public sealed record TimeSeriesDataPoint
{
    /// <summary>Timestamp of the data point.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Metric value.</summary>
    public required double Value { get; init; }

    /// <summary>Optional tags for the data point.</summary>
    public Dictionary<string, string>? Tags { get; init; }
}

/// <summary>
/// Batch of time-series data points for efficient storage.
/// </summary>
public sealed record TimeSeriesBatch
{
    /// <summary>Timestamps (sorted ascending).</summary>
    public required long[] Timestamps { get; init; }

    /// <summary>Values corresponding to timestamps.</summary>
    public required double[] Values { get; init; }

    /// <summary>Number of data points.</summary>
    public int Count => Timestamps.Length;

    /// <summary>Creates a batch from data points.</summary>
    public static TimeSeriesBatch FromDataPoints(IEnumerable<TimeSeriesDataPoint> points)
    {
        var sorted = points.OrderBy(p => p.Timestamp).ToArray();
        return new TimeSeriesBatch
        {
            Timestamps = sorted.Select(p => p.Timestamp.Ticks).ToArray(),
            Values = sorted.Select(p => p.Value).ToArray()
        };
    }
}
