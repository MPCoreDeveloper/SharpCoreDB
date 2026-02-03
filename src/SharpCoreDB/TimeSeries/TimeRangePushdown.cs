// <copyright file="TimeRangePushdown.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Time range predicate pushdown optimizer.
/// C# 14: Modern patterns, expression optimization.
/// 
/// ✅ SCDB Phase 8.3: Time Range Queries
/// 
/// Purpose:
/// - Push time predicates to storage layer
/// - Eliminate buckets early (before decompression)
/// - Minimize data movement
/// - Integration with Phase 7.3 QueryOptimizer
/// </summary>
public static class TimeRangePushdown
{
    /// <summary>
    /// Analyzes predicates and extracts time range constraints.
    /// </summary>
    public static TimeRangeConstraint ExtractTimeConstraints(IEnumerable<QueryPredicate> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);

        DateTime? minTime = null;
        DateTime? maxTime = null;
        bool minInclusive = true;
        bool maxInclusive = false;

        foreach (var predicate in predicates.Where(p => p.IsTimeColumn))
        {
            switch (predicate.Operator)
            {
                case PredicateOperator.Equal:
                    // timestamp = X means [X, X+1tick)
                    minTime = MaxDateTime(minTime, predicate.DateTimeValue);
                    maxTime = MinDateTime(maxTime, predicate.DateTimeValue.AddTicks(1));
                    minInclusive = true;
                    maxInclusive = false;
                    break;

                case PredicateOperator.GreaterThan:
                    minTime = MaxDateTime(minTime, predicate.DateTimeValue.AddTicks(1));
                    minInclusive = true;
                    break;

                case PredicateOperator.GreaterThanOrEqual:
                    minTime = MaxDateTime(minTime, predicate.DateTimeValue);
                    minInclusive = true;
                    break;

                case PredicateOperator.LessThan:
                    maxTime = MinDateTime(maxTime, predicate.DateTimeValue);
                    maxInclusive = false;
                    break;

                case PredicateOperator.LessThanOrEqual:
                    maxTime = MinDateTime(maxTime, predicate.DateTimeValue.AddTicks(1));
                    maxInclusive = false;
                    break;

                case PredicateOperator.Between:
                    minTime = MaxDateTime(minTime, predicate.DateTimeValue);
                    maxTime = MinDateTime(maxTime, predicate.DateTimeValue2);
                    minInclusive = true;
                    maxInclusive = false;
                    break;
            }
        }

        return new TimeRangeConstraint
        {
            MinTime = minTime,
            MaxTime = maxTime,
            MinInclusive = minInclusive,
            MaxInclusive = maxInclusive,
            HasConstraints = minTime.HasValue || maxTime.HasValue
        };
    }

    /// <summary>
    /// Filters buckets based on time constraints.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static IEnumerable<TimeSeriesBucket> FilterBuckets(
        IEnumerable<TimeSeriesBucket> buckets,
        TimeRangeConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        if (!constraint.HasConstraints)
        {
            foreach (var bucket in buckets)
            {
                yield return bucket;
            }
            yield break;
        }

        foreach (var bucket in buckets)
        {
            if (BucketMatchesConstraint(bucket, constraint))
            {
                yield return bucket;
            }
        }
    }

    /// <summary>
    /// Filters buckets using a Bloom filter for additional pruning.
    /// </summary>
    public static IEnumerable<TimeSeriesBucket> FilterBucketsWithBloom(
        IEnumerable<TimeSeriesBucket> buckets,
        TimeRangeConstraint constraint,
        TimeBloomFilter bloomFilter)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(bloomFilter);

        var constraintFiltered = FilterBuckets(buckets, constraint);

        foreach (var bucket in constraintFiltered)
        {
            // Use Bloom filter for additional pruning
            if (bloomFilter.MightOverlap(
                constraint.MinTime ?? bucket.StartTime,
                constraint.MaxTime ?? bucket.EndTime))
            {
                yield return bucket;
            }
        }
    }

    /// <summary>
    /// Estimates the selectivity of a time constraint.
    /// </summary>
    public static double EstimateSelectivity(
        TimeRangeConstraint constraint,
        DateTime dataMinTime,
        DateTime dataMaxTime)
    {
        if (!constraint.HasConstraints)
            return 1.0;

        var totalRange = (dataMaxTime - dataMinTime).Ticks;
        if (totalRange <= 0)
            return 1.0;

        var constraintMin = constraint.MinTime ?? dataMinTime;
        var constraintMax = constraint.MaxTime ?? dataMaxTime;

        var effectiveMin = constraintMin > dataMinTime ? constraintMin : dataMinTime;
        var effectiveMax = constraintMax < dataMaxTime ? constraintMax : dataMaxTime;

        if (effectiveMax <= effectiveMin)
            return 0.0;

        var selectedRange = (effectiveMax - effectiveMin).Ticks;
        return (double)selectedRange / totalRange;
    }

    /// <summary>
    /// Creates a query spec from time constraints.
    /// </summary>
    public static TimeSeriesQuerySpec CreateQuerySpec(
        string tableName,
        TimeRangeConstraint constraint,
        IEnumerable<QueryPredicate>? valuePredicates = null)
    {
        var spec = new TimeSeriesQuerySpec
        {
            TableName = tableName,
            StartTime = constraint.MinTime ?? DateTime.MinValue,
            EndTime = constraint.MaxTime ?? DateTime.MaxValue
        };

        if (valuePredicates != null)
        {
            foreach (var pred in valuePredicates.Where(p => !p.IsTimeColumn))
            {
                // Apply value constraints
                spec = pred.Operator switch
                {
                    PredicateOperator.GreaterThan => spec with { MinValue = pred.DoubleValue },
                    PredicateOperator.GreaterThanOrEqual => spec with { MinValue = pred.DoubleValue },
                    PredicateOperator.LessThan => spec with { MaxValue = pred.DoubleValue },
                    PredicateOperator.LessThanOrEqual => spec with { MaxValue = pred.DoubleValue },
                    _ => spec
                };
            }
        }

        return spec;
    }

    /// <summary>
    /// Optimizes a list of predicates by merging overlapping time constraints.
    /// </summary>
    public static List<QueryPredicate> OptimizePredicates(IEnumerable<QueryPredicate> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);

        var predicateList = predicates.ToList();
        var timeConstraint = ExtractTimeConstraints(predicateList);

        // Remove original time predicates and add optimized one
        var nonTimePredicates = predicateList.Where(p => !p.IsTimeColumn).ToList();

        if (timeConstraint.HasConstraints)
        {
            if (timeConstraint.MinTime.HasValue)
            {
                nonTimePredicates.Add(new QueryPredicate
                {
                    ColumnName = "timestamp",
                    Operator = PredicateOperator.GreaterThanOrEqual,
                    Value = timeConstraint.MinTime.Value
                });
            }

            if (timeConstraint.MaxTime.HasValue)
            {
                nonTimePredicates.Add(new QueryPredicate
                {
                    ColumnName = "timestamp",
                    Operator = PredicateOperator.LessThan,
                    Value = timeConstraint.MaxTime.Value
                });
            }
        }

        return nonTimePredicates;
    }

    // Private helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool BucketMatchesConstraint(TimeSeriesBucket bucket, TimeRangeConstraint constraint)
    {
        // Check if bucket overlaps with constraint range
        var bucketStart = bucket.StartTime;
        var bucketEnd = bucket.EndTime;

        if (constraint.MaxTime.HasValue)
        {
            if (constraint.MaxInclusive)
            {
                if (bucketStart > constraint.MaxTime.Value)
                    return false;
            }
            else
            {
                if (bucketStart >= constraint.MaxTime.Value)
                    return false;
            }
        }

        if (constraint.MinTime.HasValue)
        {
            if (constraint.MinInclusive)
            {
                if (bucketEnd <= constraint.MinTime.Value)
                    return false;
            }
            else
            {
                if (bucketEnd < constraint.MinTime.Value)
                    return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static DateTime MaxDateTime(DateTime? a, DateTime b)
    {
        return a.HasValue && a.Value > b ? a.Value : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static DateTime MinDateTime(DateTime? a, DateTime b)
    {
        return a.HasValue && a.Value < b ? a.Value : b;
    }
}

/// <summary>
/// Time range constraint extracted from predicates.
/// </summary>
public sealed record TimeRangeConstraint
{
    /// <summary>Minimum time (start of range).</summary>
    public DateTime? MinTime { get; init; }

    /// <summary>Maximum time (end of range).</summary>
    public DateTime? MaxTime { get; init; }

    /// <summary>Whether minimum is inclusive.</summary>
    public bool MinInclusive { get; init; } = true;

    /// <summary>Whether maximum is inclusive.</summary>
    public bool MaxInclusive { get; init; }

    /// <summary>Whether any constraints exist.</summary>
    public bool HasConstraints { get; init; }

    /// <summary>Gets the duration of the constraint range.</summary>
    public TimeSpan? Duration => MinTime.HasValue && MaxTime.HasValue
        ? MaxTime.Value - MinTime.Value
        : null;
}

/// <summary>
/// Query predicate for filtering.
/// </summary>
public sealed record QueryPredicate
{
    /// <summary>Column name.</summary>
    public required string ColumnName { get; init; }

    /// <summary>Comparison operator.</summary>
    public required PredicateOperator Operator { get; init; }

    /// <summary>Comparison value.</summary>
    public required object Value { get; init; }

    /// <summary>Second value (for BETWEEN).</summary>
    public object? Value2 { get; init; }

    /// <summary>Whether this is a time column predicate.</summary>
    public bool IsTimeColumn => ColumnName.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                                ColumnName.Equals("time", StringComparison.OrdinalIgnoreCase) ||
                                ColumnName.Equals("ts", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets value as DateTime.</summary>
    public DateTime DateTimeValue => Value switch
    {
        DateTime dt => dt,
        long ticks => new DateTime(ticks, DateTimeKind.Utc),
        string s => DateTime.Parse(s),
        _ => throw new InvalidOperationException($"Cannot convert {Value.GetType()} to DateTime")
    };

    /// <summary>Gets second value as DateTime (for BETWEEN).</summary>
    public DateTime DateTimeValue2 => Value2 switch
    {
        DateTime dt => dt,
        long ticks => new DateTime(ticks, DateTimeKind.Utc),
        string s => DateTime.Parse(s),
        null => throw new InvalidOperationException("Value2 is null"),
        _ => throw new InvalidOperationException($"Cannot convert {Value2.GetType()} to DateTime")
    };

    /// <summary>Gets value as double.</summary>
    public double DoubleValue => Value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s => double.Parse(s),
        _ => throw new InvalidOperationException($"Cannot convert {Value.GetType()} to double")
    };
}

/// <summary>
/// Predicate operators.
/// </summary>
public enum PredicateOperator
{
    /// <summary>Equality.</summary>
    Equal,

    /// <summary>Not equal.</summary>
    NotEqual,

    /// <summary>Greater than.</summary>
    GreaterThan,

    /// <summary>Greater than or equal.</summary>
    GreaterThanOrEqual,

    /// <summary>Less than.</summary>
    LessThan,

    /// <summary>Less than or equal.</summary>
    LessThanOrEqual,

    /// <summary>Between two values.</summary>
    Between,

    /// <summary>In a list of values.</summary>
    In,

    /// <summary>Not in a list of values.</summary>
    NotIn
}
