// <copyright file="TimeSeriesQuery.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Time-series query execution engine.
/// C# 14: Modern patterns, streaming results, aggressive optimization.
/// 
/// ✅ SCDB Phase 8.3: Time Range Queries
/// 
/// Purpose:
/// - Unified query interface for time-series data
/// - Multi-bucket scanning with optimization
/// - Streaming results for large datasets
/// - Aggregation support
/// </summary>
public sealed class TimeSeriesQuery
{
    private readonly BucketManager _bucketManager;
    private readonly TimeRangeIndex? _index;
    private readonly TimeBloomFilter? _bloomFilter;

    /// <summary>
    /// Initializes a new query engine.
    /// </summary>
    public TimeSeriesQuery(
        BucketManager bucketManager,
        TimeRangeIndex? index = null,
        TimeBloomFilter? bloomFilter = null)
    {
        _bucketManager = bucketManager ?? throw new ArgumentNullException(nameof(bucketManager));
        _index = index;
        _bloomFilter = bloomFilter;
    }

    /// <summary>
    /// Executes a time range query.
    /// </summary>
    public QueryResult Execute(TimeSeriesQuerySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.TableName);

        var startTicks = spec.StartTime.Ticks;
        var endTicks = spec.EndTime.Ticks;

        // Get candidate buckets
        var buckets = GetCandidateBuckets(spec.TableName, spec.StartTime, spec.EndTime);

        // Filter using Bloom filter if available
        if (_bloomFilter != null)
        {
            buckets = buckets.Where(b => _bloomFilter.MightOverlap(spec.StartTime, spec.EndTime)).ToList();
        }

        // Collect results
        var results = new List<TimeSeriesDataPoint>();
        int bucketsScanned = buckets.Count;
        int pointsScanned = 0;

        // Query all data once from the bucket manager
        var allPoints = _bucketManager.Query(spec.TableName, spec.StartTime, spec.EndTime)
            .Where(p => p.Timestamp.Ticks >= startTicks && p.Timestamp.Ticks < endTicks);

        foreach (var point in allPoints)
        {
            pointsScanned++;

            // Apply value filter if specified
            if (spec.MinValue.HasValue && point.Value < spec.MinValue.Value)
                continue;
            if (spec.MaxValue.HasValue && point.Value > spec.MaxValue.Value)
                continue;

            results.Add(point);

            // Apply limit
            if (spec.Limit.HasValue && results.Count >= spec.Limit.Value)
                break;
        }

        // Apply ordering
        var orderedResults = spec.OrderDescending
            ? results.OrderByDescending(p => p.Timestamp).ToList()
            : results.OrderBy(p => p.Timestamp).ToList();

        return new QueryResult
        {
            Points = orderedResults,
            BucketsScanned = bucketsScanned,
            PointsScanned = pointsScanned,
            PointsReturned = orderedResults.Count
        };
    }

    /// <summary>
    /// Executes a streaming query (yields results as they're found).
    /// </summary>
    public IEnumerable<TimeSeriesDataPoint> ExecuteStreaming(TimeSeriesQuerySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.TableName);

        var startTicks = spec.StartTime.Ticks;
        var endTicks = spec.EndTime.Ticks;
        int count = 0;

        var buckets = GetCandidateBuckets(spec.TableName, spec.StartTime, spec.EndTime);

        foreach (var bucket in buckets)
        {
            var points = _bucketManager.Query(spec.TableName, spec.StartTime, spec.EndTime);

            foreach (var point in points)
            {
                if (point.Timestamp.Ticks < startTicks || point.Timestamp.Ticks >= endTicks)
                    continue;

                if (spec.MinValue.HasValue && point.Value < spec.MinValue.Value)
                    continue;
                if (spec.MaxValue.HasValue && point.Value > spec.MaxValue.Value)
                    continue;

                yield return point;
                count++;

                if (spec.Limit.HasValue && count >= spec.Limit.Value)
                    yield break;
            }
        }
    }

    /// <summary>
    /// Executes an aggregation query.
    /// </summary>
    public AggregationResult ExecuteAggregation(TimeSeriesQuerySpec spec, AggregationType aggregation)
    {
        var queryResult = Execute(spec);
        var values = queryResult.Points.Select(p => p.Value).ToList();

        if (values.Count == 0)
        {
            return new AggregationResult
            {
                Aggregation = aggregation,
                Value = null,
                Count = 0,
                StartTime = spec.StartTime,
                EndTime = spec.EndTime
            };
        }

        double? result = aggregation switch
        {
            AggregationType.Count => values.Count,
            AggregationType.Sum => values.Sum(),
            AggregationType.Average => values.Average(),
            AggregationType.Min => values.Min(),
            AggregationType.Max => values.Max(),
            AggregationType.First => values.First(),
            AggregationType.Last => values.Last(),
            AggregationType.Stddev => CalculateStdDev(values),
            _ => throw new ArgumentOutOfRangeException(nameof(aggregation))
        };

        return new AggregationResult
        {
            Aggregation = aggregation,
            Value = result,
            Count = values.Count,
            StartTime = spec.StartTime,
            EndTime = spec.EndTime
        };
    }

    /// <summary>
    /// Executes a downsampling query (aggregate by time intervals).
    /// </summary>
    public IEnumerable<AggregationResult> ExecuteDownsample(
        TimeSeriesQuerySpec spec,
        TimeSpan interval,
        AggregationType aggregation)
    {
        var current = spec.StartTime;

        while (current < spec.EndTime)
        {
            var intervalEnd = current + interval;
            if (intervalEnd > spec.EndTime)
                intervalEnd = spec.EndTime;

            var intervalSpec = spec with
            {
                StartTime = current,
                EndTime = intervalEnd
            };

            yield return ExecuteAggregation(intervalSpec, aggregation);

            current = intervalEnd;
        }
    }

    /// <summary>
    /// Estimates the number of points in a range without scanning.
    /// </summary>
    public long EstimateCount(string tableName, DateTime startTime, DateTime endTime)
    {
        if (_index != null)
        {
            return _index.EstimateCountInRange(startTime.Ticks, endTime.Ticks);
        }

        // Fallback: count buckets * average points per bucket
        var buckets = GetCandidateBuckets(tableName, startTime, endTime);
        return buckets.Sum(b => b.RowCount);
    }

    // Private helpers

    private List<TimeSeriesBucket> GetCandidateBuckets(string tableName, DateTime startTime, DateTime endTime)
    {
        if (_index != null)
        {
            // Use index to find relevant buckets
            var bucketIds = _index.GetBucketsInRange(startTime, endTime).ToHashSet();
            return _bucketManager.GetBuckets(tableName)
                .Where(b => bucketIds.Contains(b.BucketId))
                .ToList();
        }

        // Fallback: get all buckets that overlap the range
        return _bucketManager.GetBuckets(tableName)
            .Where(b => b.OverlapsRange(startTime, endTime))
            .OrderBy(b => b.StartTime)
            .ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        double mean = values.Average();
        double sumSquares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }
}

/// <summary>
/// Query specification.
/// </summary>
public sealed record TimeSeriesQuerySpec
{
    /// <summary>Table name to query.</summary>
    public required string TableName { get; init; }

    /// <summary>Query start time (inclusive).</summary>
    public required DateTime StartTime { get; init; }

    /// <summary>Query end time (exclusive).</summary>
    public required DateTime EndTime { get; init; }

    /// <summary>Optional minimum value filter.</summary>
    public double? MinValue { get; init; }

    /// <summary>Optional maximum value filter.</summary>
    public double? MaxValue { get; init; }

    /// <summary>Optional result limit.</summary>
    public int? Limit { get; init; }

    /// <summary>Whether to order results descending.</summary>
    public bool OrderDescending { get; init; }
}

/// <summary>
/// Query result.
/// </summary>
public sealed record QueryResult
{
    /// <summary>Result data points.</summary>
    public required List<TimeSeriesDataPoint> Points { get; init; }

    /// <summary>Number of buckets scanned.</summary>
    public required int BucketsScanned { get; init; }

    /// <summary>Number of points scanned.</summary>
    public required int PointsScanned { get; init; }

    /// <summary>Number of points returned.</summary>
    public required int PointsReturned { get; init; }

    /// <summary>Whether more results exist (if limit was applied).</summary>
    public bool HasMore => PointsScanned > PointsReturned;
}

/// <summary>
/// Aggregation result.
/// </summary>
public sealed record AggregationResult
{
    /// <summary>Aggregation type performed.</summary>
    public required AggregationType Aggregation { get; init; }

    /// <summary>Aggregation result value (null if no data).</summary>
    public required double? Value { get; init; }

    /// <summary>Number of points aggregated.</summary>
    public required int Count { get; init; }

    /// <summary>Start time of aggregation window.</summary>
    public required DateTime StartTime { get; init; }

    /// <summary>End time of aggregation window.</summary>
    public required DateTime EndTime { get; init; }
}

/// <summary>
/// Aggregation types.
/// </summary>
public enum AggregationType
{
    /// <summary>Count of values.</summary>
    Count,

    /// <summary>Sum of values.</summary>
    Sum,

    /// <summary>Average of values.</summary>
    Average,

    /// <summary>Minimum value.</summary>
    Min,

    /// <summary>Maximum value.</summary>
    Max,

    /// <summary>First value in range.</summary>
    First,

    /// <summary>Last value in range.</summary>
    Last,

    /// <summary>Standard deviation.</summary>
    Stddev
}
