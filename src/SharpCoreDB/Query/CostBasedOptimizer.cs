// <copyright file="CostBasedOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Query;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Cost-based query optimizer.
/// C# 14: Modern patterns, cost estimation, join ordering.
/// 
/// ✅ SCDB Phase 7.3: Advanced Query Optimization - Cost-Based Optimizer
/// 
/// Purpose:
/// - Estimate query execution cost
/// - Optimize join ordering
/// - Push down predicates
/// - Choose optimal execution plan
/// </summary>
public sealed class CostBasedOptimizer
{
    private readonly Dictionary<string, TableStatistics> _tableStats = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Registers table statistics.
    /// </summary>
    public void RegisterTable(string tableName, TableStatistics stats)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(stats);

        lock (_lock)
        {
            _tableStats[tableName] = stats;
        }
    }

    /// <summary>
    /// Estimates cardinality (result size) for a query.
    /// </summary>
    public long EstimateCardinality(QueryPlan query)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_lock)
        {
            if (!_tableStats.TryGetValue(query.TableName, out var stats))
            {
                return 1000; // Default estimate
            }

            long cardinality = stats.RowCount;

            // Apply selectivity for each filter
            foreach (var filter in query.Filters)
            {
                cardinality = (long)(cardinality * EstimateSelectivity(filter, stats));
            }

            return Math.Max(1, cardinality);
        }
    }

    /// <summary>
    /// Estimates execution cost for a query.
    /// </summary>
    public double EstimateCost(QueryPlan query)
    {
        ArgumentNullException.ThrowIfNull(query);

        double cost = 0;

        // Scan cost
        var cardinality = EstimateCardinality(query);
        cost += cardinality * CostConstants.ScanCostPerRow;

        // Filter cost
        cost += query.Filters.Count * cardinality * CostConstants.FilterCostPerRow;

        // Sort cost (if needed)
        if (query.RequiresSort)
        {
            cost += cardinality * Math.Log(cardinality) * CostConstants.SortCostFactor;
        }

        // Aggregation cost
        if (query.HasAggregation)
        {
            cost += cardinality * CostConstants.AggregationCostPerRow;
        }

        return cost;
    }

    /// <summary>
    /// Optimizes join order for multiple tables.
    /// </summary>
    public List<string> OptimizeJoinOrder(List<string> tables, List<JoinCondition> joins)
    {
        ArgumentNullException.ThrowIfNull(tables);

        if (tables.Count <= 2)
        {
            return tables; // No optimization needed
        }

        // Greedy join ordering: start with smallest table
        var remaining = new HashSet<string>(tables);
        var ordered = new List<string>();

        // Pick smallest table first
        var smallest = tables.OrderBy(t => GetTableSize(t)).First();
        ordered.Add(smallest);
        remaining.Remove(smallest);

        // Iteratively add table with lowest join cost
        while (remaining.Count > 0)
        {
            string? best = null;
            double bestCost = double.MaxValue;

            foreach (var table in remaining)
            {
                var cost = EstimateJoinCost(ordered[^1], table, joins);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = table;
                }
            }

            if (best != null)
            {
                ordered.Add(best);
                remaining.Remove(best);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Pushes predicates down to table scans.
    /// </summary>
    public QueryPlan PushDownPredicates(QueryPlan query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Separate predicates by table
        var tablePredicates = query.Filters
            .Where(f => f.TableName == query.TableName)
            .ToList();

        return query with
        {
            Filters = tablePredicates,
            PushedDown = true
        };
    }

    /// <summary>
    /// Chooses the best index for a query.
    /// </summary>
    public string? ChooseBestIndex(QueryPlan query, List<string> availableIndexes)
    {
        if (availableIndexes.Count == 0)
            return null;

        // Simple heuristic: choose index on first filter column
        if (query.Filters.Count > 0)
        {
            var firstColumn = query.Filters[0].ColumnName;
            return availableIndexes.FirstOrDefault(idx => idx.Contains(firstColumn));
        }

        return availableIndexes[0];
    }

    // Private helpers

    private double EstimateSelectivity(FilterExpression filter, TableStatistics stats)
    {
        return filter.Operator switch
        {
            FilterOperator.Equals => 1.0 / Math.Max(1, stats.DistinctValues.GetValueOrDefault(filter.ColumnName, 10)),
            FilterOperator.Range => 0.1, // Assume 10% selectivity for ranges
            FilterOperator.In => Math.Min(1.0, filter.InValues?.Count ?? 1 / 10.0),
            FilterOperator.Like => 0.5, // 50% for LIKE patterns
            _ => 0.1
        };
    }

    private long GetTableSize(string tableName)
    {
        lock (_lock)
        {
            return _tableStats.TryGetValue(tableName, out var stats) ? stats.RowCount : 1000;
        }
    }

    private double EstimateJoinCost(string leftTable, string rightTable, List<JoinCondition> joins)
    {
        var leftSize = GetTableSize(leftTable);
        var rightSize = GetTableSize(rightTable);

        // Nested loop join cost: O(n * m)
        return leftSize * rightSize * CostConstants.JoinCostPerRow;
    }
}

/// <summary>
/// Query execution plan.
/// </summary>
public sealed record QueryPlan
{
    /// <summary>Primary table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Filter expressions.</summary>
    public required List<FilterExpression> Filters { get; init; }

    /// <summary>Whether sorting is required.</summary>
    public bool RequiresSort { get; init; }

    /// <summary>Whether aggregation is needed.</summary>
    public bool HasAggregation { get; init; }

    /// <summary>Whether predicates have been pushed down.</summary>
    public bool PushedDown { get; init; }
}

/// <summary>
/// Filter expression.
/// </summary>
public sealed record FilterExpression
{
    /// <summary>Table name (for joins).</summary>
    public string? TableName { get; init; }

    /// <summary>Column name.</summary>
    public required string ColumnName { get; init; }

    /// <summary>Filter operator.</summary>
    public required FilterOperator Operator { get; init; }

    /// <summary>Comparison value.</summary>
    public object? Value { get; init; }

    /// <summary>Values for IN operator.</summary>
    public List<object>? InValues { get; init; }
}

/// <summary>
/// Filter operators.
/// </summary>
public enum FilterOperator
{
    /// <summary>Equality.</summary>
    Equals,

    /// <summary>Range.</summary>
    Range,

    /// <summary>IN clause.</summary>
    In,

    /// <summary>LIKE pattern.</summary>
    Like,

    /// <summary>Greater than.</summary>
    GreaterThan,

    /// <summary>Less than.</summary>
    LessThan
}

/// <summary>
/// Join condition.
/// </summary>
public sealed record JoinCondition
{
    /// <summary>Left table.</summary>
    public required string LeftTable { get; init; }

    /// <summary>Left column.</summary>
    public required string LeftColumn { get; init; }

    /// <summary>Right table.</summary>
    public required string RightTable { get; init; }

    /// <summary>Right column.</summary>
    public required string RightColumn { get; init; }
}

/// <summary>
/// Table statistics for cost estimation.
/// </summary>
public sealed record TableStatistics
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Total row count.</summary>
    public required long RowCount { get; init; }

    /// <summary>Average row size in bytes.</summary>
    public int AverageRowSize { get; init; } = 100;

    /// <summary>Distinct values per column.</summary>
    public Dictionary<string, long> DistinctValues { get; init; } = [];
}

/// <summary>
/// Cost constants for estimation.
/// </summary>
internal static class CostConstants
{
    public const double ScanCostPerRow = 1.0;
    public const double FilterCostPerRow = 0.1;
    public const double SortCostFactor = 0.5;
    public const double AggregationCostPerRow = 0.2;
    public const double JoinCostPerRow = 0.05;
}
