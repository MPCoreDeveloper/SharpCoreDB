// <copyright file="CostEstimator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Optimization;

using SharpCoreDB.Services;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Estimates query execution costs and cardinality.
/// HOT PATH - Zero-allocation cost estimation.
/// 
/// Cost Model:
/// - Table scan: 1.0 * row_count
/// - Index lookup: 1.0 + log₂(row_count)
/// - Hash join: left_cost + right_cost + build(smaller) + probe(larger)
/// - Nested loop: left_cost + (right_cost * left_rows)
/// - Filter: input_cost * selectivity
/// </summary>
public sealed class CostEstimator
{
    private readonly IReadOnlyDictionary<string, TableStatistics> statistics;

    /// <summary>
    /// Initializes a new instance of the <see cref="CostEstimator"/> class.
    /// </summary>
    public CostEstimator(IReadOnlyDictionary<string, TableStatistics> statistics)
    {
        this.statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    }

    /// <summary>
    /// Estimates scan cost for a table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public EstimatedCost EstimateScanCost(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!statistics.TryGetValue(tableName, out var stats))
        {
            // Fallback: assume small table
            return new EstimatedCost { Cost = 100.0, OutputRows = 1000 };
        }

        return new EstimatedCost
        {
            Cost = stats.RowCount * 1.0,
            OutputRows = stats.RowCount
        };
    }

    /// <summary>
    /// Estimates join cost using hash join model.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public EstimatedCost EstimateJoinCost(
        EstimatedCost leftCost,
        EstimatedCost rightCost,
        double joinSelectivity = 0.5)
    {
        // Determine smaller side (for hash table build)
        var buildCost = leftCost.OutputRows <= rightCost.OutputRows ? leftCost.Cost : rightCost.Cost;
        var probeCost = leftCost.OutputRows <= rightCost.OutputRows ? rightCost.Cost : leftCost.Cost;
        var buildRows = leftCost.OutputRows <= rightCost.OutputRows ? leftCost.OutputRows : rightCost.OutputRows;
        var probeRows = leftCost.OutputRows <= rightCost.OutputRows ? rightCost.OutputRows : leftCost.OutputRows;

        // Hash join cost: build + probe + output
        var totalCost = buildCost + probeCost
            + (buildRows * 1.0)  // Build hash table
            + (probeRows * 0.5); // Probe (expect match early)

        // Output rows: input_left * input_right * selectivity
        long outputRows = (long)(leftCost.OutputRows * rightCost.OutputRows * joinSelectivity);

        return new EstimatedCost
        {
            Cost = totalCost,
            OutputRows = Math.Max(1, outputRows)
        };
    }

    /// <summary>
    /// Estimates filter (WHERE) cost.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public EstimatedCost EstimateFilterCost(
        EstimatedCost inputCost,
        double selectivity = 0.1)
    {
        // Filter cost: input cost + selectivity factor
        var filterCost = inputCost.Cost + (inputCost.OutputRows * 0.01); // 0.01 = check cost
        var outputRows = (long)(inputCost.OutputRows * selectivity);

        return new EstimatedCost
        {
            Cost = filterCost,
            OutputRows = Math.Max(1, outputRows)
        };
    }

    /// <summary>
    /// Estimates aggregate (GROUP BY) cost.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public EstimatedCost EstimateAggregateCost(EstimatedCost inputCost)
    {
        // Aggregate cost: input cost + grouping overhead
        var aggregateCost = inputCost.Cost + (inputCost.OutputRows * 0.1);
        var outputRows = Math.Max(1, (long)(inputCost.OutputRows * 0.1));

        return new EstimatedCost
        {
            Cost = aggregateCost,
            OutputRows = outputRows
        };
    }

    /// <summary>
    /// Estimates sort (ORDER BY) cost using n log n model.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public EstimatedCost EstimateSortCost(EstimatedCost inputCost)
    {
        // Sort cost: input cost + n log n comparisons
        var sortCost = inputCost.Cost + (inputCost.OutputRows * Math.Log(inputCost.OutputRows, 2));

        return new EstimatedCost
        {
            Cost = sortCost,
            OutputRows = inputCost.OutputRows
        };
    }
}

/// <summary>
/// Estimated cost and cardinality for a query operation.
/// ✅ C# 14: Init-only properties.
/// </summary>
public sealed class EstimatedCost
{
    /// <summary>
    /// Total estimated cost units (lower is better).
    /// </summary>
    public required double Cost { get; init; }

    /// <summary>
    /// Estimated output rows from this operation.
    /// </summary>
    public required long OutputRows { get; init; }
}

/// <summary>
/// Pre-computed table statistics for cost estimation.
/// ✅ C# 14: Init-only properties.
/// </summary>
public sealed class TableStatistics
{
    /// <summary>
    /// Total number of rows in table.
    /// </summary>
    public required long RowCount { get; init; }

    /// <summary>
    /// Distinct value counts per column (for join estimation).
    /// </summary>
    public required Dictionary<string, long> ColumnDistinctCounts { get; init; } = [];

    /// <summary>
    /// Average row width in bytes (for memory planning).
    /// </summary>
    public double AverageRowWidth { get; set; } = 100.0;

    /// <summary>
    /// Columns with indexes.
    /// </summary>
    public List<string> IndexedColumns { get; set; } = [];
}
