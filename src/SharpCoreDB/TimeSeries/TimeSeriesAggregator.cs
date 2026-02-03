// <copyright file="TimeSeriesAggregator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Time-series data aggregation engine.
/// C# 14: Static class, aggressive optimization, SIMD-friendly.
/// 
/// ✅ SCDB Phase 8.4: Downsampling & Retention
/// 
/// Purpose:
/// - Multiple aggregation strategies
/// - Efficient batch processing
/// - Percentile calculation
/// - Weighted averages
/// </summary>
public static class TimeSeriesAggregator
{
    /// <summary>
    /// Aggregates values using the specified strategy.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Aggregate(ReadOnlySpan<double> values, AggregationType strategy)
    {
        if (values.IsEmpty)
            return double.NaN;

        return strategy switch
        {
            AggregationType.Count => values.Length,
            AggregationType.Sum => Sum(values),
            AggregationType.Average => Average(values),
            AggregationType.Min => Min(values),
            AggregationType.Max => Max(values),
            AggregationType.First => values[0],
            AggregationType.Last => values[^1],
            AggregationType.Stddev => StandardDeviation(values),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    /// <summary>
    /// Calculates the sum of values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Sum(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
            return 0;

        double sum = 0;
        foreach (var v in values)
        {
            sum += v;
        }

        return sum;
    }

    /// <summary>
    /// Calculates the average of values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Average(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
            return double.NaN;

        return Sum(values) / values.Length;
    }

    /// <summary>
    /// Finds the minimum value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Min(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
            return double.NaN;

        double min = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min)
                min = values[i];
        }

        return min;
    }

    /// <summary>
    /// Finds the maximum value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Max(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
            return double.NaN;

        double max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > max)
                max = values[i];
        }

        return max;
    }

    /// <summary>
    /// Calculates standard deviation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double StandardDeviation(ReadOnlySpan<double> values)
    {
        if (values.Length < 2)
            return 0;

        double mean = Average(values);
        double sumSquares = 0;

        foreach (var v in values)
        {
            double diff = v - mean;
            sumSquares += diff * diff;
        }

        return Math.Sqrt(sumSquares / (values.Length - 1));
    }

    /// <summary>
    /// Calculates a percentile value.
    /// </summary>
    public static double Percentile(ReadOnlySpan<double> values, double percentile)
    {
        if (values.IsEmpty)
            return double.NaN;

        if (percentile < 0 || percentile > 100)
            throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be between 0 and 100");

        if (values.Length == 1)
            return values[0];

        // Copy and sort
        var sorted = values.ToArray();
        Array.Sort(sorted);

        // Calculate index
        double index = (percentile / 100.0) * (sorted.Length - 1);
        int lowerIndex = (int)Math.Floor(index);
        int upperIndex = (int)Math.Ceiling(index);

        if (lowerIndex == upperIndex)
            return sorted[lowerIndex];

        // Linear interpolation
        double fraction = index - lowerIndex;
        return sorted[lowerIndex] + fraction * (sorted[upperIndex] - sorted[lowerIndex]);
    }

    /// <summary>
    /// Calculates the median (50th percentile).
    /// </summary>
    public static double Median(ReadOnlySpan<double> values)
    {
        return Percentile(values, 50);
    }

    /// <summary>
    /// Calculates a weighted average.
    /// </summary>
    public static double WeightedAverage(ReadOnlySpan<double> values, ReadOnlySpan<double> weights)
    {
        if (values.IsEmpty || weights.IsEmpty)
            return double.NaN;

        if (values.Length != weights.Length)
            throw new ArgumentException("Values and weights must have the same length");

        double sum = 0;
        double weightSum = 0;

        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i] * weights[i];
            weightSum += weights[i];
        }

        return weightSum > 0 ? sum / weightSum : double.NaN;
    }

    /// <summary>
    /// Aggregates time-series data points into intervals.
    /// </summary>
    public static IEnumerable<AggregatedPoint> AggregateToIntervals(
        IEnumerable<TimeSeriesDataPoint> points,
        TimeSpan interval,
        AggregationType strategy)
    {
        var groups = points
            .GroupBy(p => new DateTime(
                p.Timestamp.Ticks / interval.Ticks * interval.Ticks,
                DateTimeKind.Utc))
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var values = group.Select(p => p.Value).ToArray();
            var aggregatedValue = Aggregate(values, strategy);

            yield return new AggregatedPoint
            {
                Timestamp = group.Key,
                Value = aggregatedValue,
                Count = values.Length,
                Min = Min(values),
                Max = Max(values),
                Sum = Sum(values)
            };
        }
    }

    /// <summary>
    /// Aggregates raw arrays into intervals.
    /// </summary>
    public static (long[] Timestamps, double[] Values) AggregateArraysToIntervals(
        long[] timestamps,
        double[] values,
        long intervalTicks,
        AggregationType strategy)
    {
        if (timestamps.Length != values.Length)
            throw new ArgumentException("Timestamps and values must have the same length");

        if (timestamps.Length == 0)
            return ([], []);

        var resultTimestamps = new List<long>();
        var resultValues = new List<double>();

        int start = 0;
        long currentBucket = (timestamps[0] / intervalTicks) * intervalTicks;

        for (int i = 1; i <= timestamps.Length; i++)
        {
            long bucket = i < timestamps.Length
                ? (timestamps[i] / intervalTicks) * intervalTicks
                : -1;

            if (bucket != currentBucket || i == timestamps.Length)
            {
                // Aggregate the current bucket
                var bucketValues = new ReadOnlySpan<double>(values, start, i - start);
                double aggregated = Aggregate(bucketValues, strategy);

                resultTimestamps.Add(currentBucket);
                resultValues.Add(aggregated);

                start = i;
                currentBucket = bucket;
            }
        }

        return (resultTimestamps.ToArray(), resultValues.ToArray());
    }

    /// <summary>
    /// Calculates multiple aggregations at once (more efficient for multiple stats).
    /// </summary>
    public static AggregationSummary CalculateSummary(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
        {
            return new AggregationSummary
            {
                Count = 0,
                Sum = 0,
                Min = double.NaN,
                Max = double.NaN,
                Average = double.NaN,
                Stddev = double.NaN
            };
        }

        double sum = 0;
        double min = values[0];
        double max = values[0];

        foreach (var v in values)
        {
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        double mean = sum / values.Length;

        double sumSquares = 0;
        foreach (var v in values)
        {
            double diff = v - mean;
            sumSquares += diff * diff;
        }

        double stddev = values.Length > 1
            ? Math.Sqrt(sumSquares / (values.Length - 1))
            : 0;

        return new AggregationSummary
        {
            Count = values.Length,
            Sum = sum,
            Min = min,
            Max = max,
            Average = mean,
            Stddev = stddev
        };
    }
}

/// <summary>
/// Aggregated data point with statistics.
/// </summary>
public sealed record AggregatedPoint
{
    /// <summary>Timestamp of the aggregation bucket.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Aggregated value.</summary>
    public required double Value { get; init; }

    /// <summary>Number of points aggregated.</summary>
    public required int Count { get; init; }

    /// <summary>Minimum value in bucket.</summary>
    public required double Min { get; init; }

    /// <summary>Maximum value in bucket.</summary>
    public required double Max { get; init; }

    /// <summary>Sum of values in bucket.</summary>
    public required double Sum { get; init; }
}

/// <summary>
/// Summary of aggregation statistics.
/// </summary>
public sealed record AggregationSummary
{
    /// <summary>Number of values.</summary>
    public required int Count { get; init; }

    /// <summary>Sum of values.</summary>
    public required double Sum { get; init; }

    /// <summary>Minimum value.</summary>
    public required double Min { get; init; }

    /// <summary>Maximum value.</summary>
    public required double Max { get; init; }

    /// <summary>Average value.</summary>
    public required double Average { get; init; }

    /// <summary>Standard deviation.</summary>
    public required double Stddev { get; init; }
}
