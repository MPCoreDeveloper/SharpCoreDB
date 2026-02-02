// <copyright file="ColumnStatistics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Columnar;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Statistics collection and analysis for columnar data.
/// C# 14: Records, primary constructors, collection expressions.
/// 
/// ✅ SCDB Phase 7: Advanced Query Optimization
/// 
/// Statistics tracked:
/// - Min/max values for range optimization
/// - Cardinality (distinct value count)
/// - Null count for selectivity estimation
/// - Histogram for value distribution
/// - Column correlation analysis
/// 
/// Used by query optimizer for:
/// - Filter selectivity estimation
/// - Join predicate pushdown
/// - Index recommendation
/// - Query plan cost calculation
/// </summary>
public static class ColumnStatistics
{
    /// <summary>Statistics for a single column.</summary>
    public sealed record ColumnStats
    {
        /// <summary>Column name.</summary>
        public required string ColumnName { get; init; }
        
        /// <summary>Total value count (including NULLs).</summary>
        public required int ValueCount { get; init; }
        
        /// <summary>NULL value count.</summary>
        public required int NullCount { get; init; }
        
        /// <summary>Distinct value count (cardinality).</summary>
        public required int DistinctCount { get; init; }
        
        /// <summary>Minimum value (for comparable types).</summary>
        public IComparable? MinValue { get; init; }
        
        /// <summary>Maximum value (for comparable types).</summary>
        public IComparable? MaxValue { get; init; }
        
        /// <summary>Average string length (for string columns).</summary>
        public double? AvgStringLength { get; init; }
        
        /// <summary>Value distribution histogram.</summary>
        public HistogramBucket[]? Histogram { get; init; }

        /// <summary>Gets selectivity of NULL values (0.0 - 1.0).</summary>
        public double NullSelectivity => ValueCount > 0 ? (double)NullCount / ValueCount : 0.0;

        /// <summary>Gets selectivity of distinct values.</summary>
        public double DistinctSelectivity => ValueCount > 0 ? (double)DistinctCount / ValueCount : 0.0;

        /// <summary>Validates statistics correctness.</summary>
        public bool Validate()
        {
            return ValueCount >= 0 &&
                   NullCount >= 0 &&
                   NullCount <= ValueCount &&
                   DistinctCount > 0 &&
                   DistinctCount <= ValueCount - NullCount;
        }
    }

    /// <summary>Histogram bucket for value distribution.</summary>
    public sealed record HistogramBucket
    {
        /// <summary>Lower bound (inclusive) of bucket.</summary>
        public required IComparable BoundLower { get; init; }
        
        /// <summary>Upper bound (exclusive) of bucket.</summary>
        public required IComparable BoundUpper { get; init; }
        
        /// <summary>Number of values in this bucket.</summary>
        public required int Count { get; init; }

        /// <summary>Gets fraction of values in bucket (0.0 - 1.0).</summary>
        public double Fraction(int totalCount) => totalCount > 0 ? (double)Count / totalCount : 0.0;
    }

    /// <summary>Builds statistics for integer column.</summary>
    public static ColumnStats BuildStats(string columnName, int[] values)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        ArgumentNullException.ThrowIfNull(values);
        
        if (values.Length == 0)
        {
            return new ColumnStats
            {
                ColumnName = columnName,
                ValueCount = 0,
                NullCount = 0,
                DistinctCount = 0,
            };
        }

        var nonNullValues = values.Where(v => v != int.MinValue).ToList(); // Assume int.MinValue = NULL
        var distinctValues = new HashSet<int>(nonNullValues);
        var nullCount = values.Length - nonNullValues.Count;

        var minValue = nonNullValues.Count > 0 ? nonNullValues.Min() : (int?)null;
        var maxValue = nonNullValues.Count > 0 ? nonNullValues.Max() : (int?)null;

        // Build histogram with 10 buckets
        var histogram = BuildHistogram(nonNullValues, minValue, maxValue, bucketCount: 10);

        return new ColumnStats
        {
            ColumnName = columnName,
            ValueCount = values.Length,
            NullCount = nullCount,
            DistinctCount = distinctValues.Count,
            MinValue = minValue,
            MaxValue = maxValue,
            Histogram = histogram.ToArray(),
        };
    }

    /// <summary>Builds statistics for long column.</summary>
    public static ColumnStats BuildStats(string columnName, long[] values)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        ArgumentNullException.ThrowIfNull(values);
        
        if (values.Length == 0)
        {
            return new ColumnStats
            {
                ColumnName = columnName,
                ValueCount = 0,
                NullCount = 0,
                DistinctCount = 0,
            };
        }

        var nonNullValues = values.Where(v => v != long.MinValue).ToList();
        var distinctValues = new HashSet<long>(nonNullValues);
        var nullCount = values.Length - nonNullValues.Count;

        var minValue = nonNullValues.Count > 0 ? nonNullValues.Min() : (long?)null;
        var maxValue = nonNullValues.Count > 0 ? nonNullValues.Max() : (long?)null;

        var histogram = BuildHistogram(nonNullValues, minValue, maxValue, bucketCount: 10);

        return new ColumnStats
        {
            ColumnName = columnName,
            ValueCount = values.Length,
            NullCount = nullCount,
            DistinctCount = distinctValues.Count,
            MinValue = minValue,
            MaxValue = maxValue,
            Histogram = histogram.ToArray(),
        };
    }

    /// <summary>Builds statistics for double column.</summary>
    public static ColumnStats BuildStats(string columnName, double[] values)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        ArgumentNullException.ThrowIfNull(values);
        
        if (values.Length == 0)
        {
            return new ColumnStats
            {
                ColumnName = columnName,
                ValueCount = 0,
                NullCount = 0,
                DistinctCount = 0,
            };
        }

        var nonNullValues = values.Where(v => !double.IsNaN(v)).ToList();
        var distinctValues = new HashSet<double>(nonNullValues);
        var nullCount = values.Length - nonNullValues.Count;

        var minValue = nonNullValues.Count > 0 ? nonNullValues.Min() : (double?)null;
        var maxValue = nonNullValues.Count > 0 ? nonNullValues.Max() : (double?)null;

        var histogram = BuildHistogram(nonNullValues, minValue, maxValue, bucketCount: 10);

        return new ColumnStats
        {
            ColumnName = columnName,
            ValueCount = values.Length,
            NullCount = nullCount,
            DistinctCount = distinctValues.Count,
            MinValue = minValue,
            MaxValue = maxValue,
            Histogram = histogram.ToArray(),
        };
    }

    /// <summary>Builds statistics for string column.</summary>
    public static ColumnStats BuildStats(string columnName, string[] values)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        ArgumentNullException.ThrowIfNull(values);
        
        if (values.Length == 0)
        {
            return new ColumnStats
            {
                ColumnName = columnName,
                ValueCount = 0,
                NullCount = 0,
                DistinctCount = 0,
            };
        }

        var nonNullValues = values.Where(v => v != null).ToList();
        var distinctValues = new HashSet<string>(nonNullValues);
        var nullCount = values.Length - nonNullValues.Count;
        var avgLength = nonNullValues.Count > 0 
            ? nonNullValues.Average(s => s?.Length ?? 0) 
            : 0.0;

        // Get lexicographic min/max
        var minValue = nonNullValues.Count > 0 ? nonNullValues.Min() : null;
        var maxValue = nonNullValues.Count > 0 ? nonNullValues.Max() : null;

        return new ColumnStats
        {
            ColumnName = columnName,
            ValueCount = values.Length,
            NullCount = nullCount,
            DistinctCount = distinctValues.Count,
            MinValue = minValue,
            MaxValue = maxValue,
            AvgStringLength = avgLength,
        };
    }

    /// <summary>Estimates selectivity for a filter predicate.</summary>
    public static double EstimateSelectivity(ColumnStats stats, 
                                            ColumnFormat.ColumnEncoding encoding,
                                            string? predicateOperator,
                                            object? predicateValue)
    {
        ArgumentNullException.ThrowIfNull(stats);
        
        if (stats.ValueCount == 0)
            return 0.0;

        // NULL handling
        if (predicateValue == null && predicateOperator == "IS NULL")
            return stats.NullSelectivity;
        
        if (predicateValue == null && predicateOperator == "IS NOT NULL")
            return 1.0 - stats.NullSelectivity;

        // For dictionary-encoded columns, use distinct count
        if (encoding == ColumnFormat.ColumnEncoding.Dictionary)
            return stats.DistinctSelectivity;

        // Range predicates - estimate using histogram if available
        if (predicateOperator == ">" || predicateOperator == ">=" || 
            predicateOperator == "<" || predicateOperator == "<=")
        {
            if (stats.Histogram != null && predicateValue is IComparable comparable)
            {
                return EstimateRangeSelectivity(stats.Histogram, predicateOperator, comparable);
            }
        }

        // Default estimate: 10% selectivity (conservative)
        return 0.1;
    }

    /// <summary>Helper: Builds histogram from values.</summary>
    private static List<HistogramBucket> BuildHistogram<T>(
        List<T> values,
        T? minValue,
        T? maxValue,
        int bucketCount) where T : IComparable
    {
        var result = new List<HistogramBucket>();

        if (values.Count == 0 || minValue == null || maxValue == null)
            return result;

        var sorted = values.OrderBy(v => v).ToList();
        var bucketSize = (values.Count + bucketCount - 1) / bucketCount;

        for (int i = 0; i < bucketCount && i * bucketSize < values.Count; i++)
        {
            var lower = sorted[i * bucketSize];
            var upper = i < bucketCount - 1 
                ? sorted[Math.Min((i + 1) * bucketSize, values.Count - 1)]
                : maxValue;
            
            var count = Math.Min(bucketSize, values.Count - (i * bucketSize));

            result.Add(new HistogramBucket
            {
                BoundLower = lower,
                BoundUpper = upper,
                Count = count,
            });
        }

        return result;
    }

    /// <summary>Helper: Estimates selectivity for range predicates.</summary>
    private static double EstimateRangeSelectivity(HistogramBucket[] histogram,
                                                   string predicateOperator,
                                                   IComparable value)
    {
        double selectivity = 0.0;

        foreach (var bucket in histogram)
        {
            bool bucketMatches = predicateOperator switch
            {
                ">" => bucket.BoundUpper.CompareTo(value) > 0,
                ">=" => bucket.BoundUpper.CompareTo(value) >= 0,
                "<" => bucket.BoundLower.CompareTo(value) < 0,
                "<=" => bucket.BoundLower.CompareTo(value) <= 0,
                "=" => bucket.BoundLower.CompareTo(value) <= 0 && bucket.BoundUpper.CompareTo(value) > 0,
                _ => false
            };

            if (bucketMatches)
                selectivity += bucket.Fraction(histogram.Sum(b => b.Count));
        }

        return selectivity;
    }
}
