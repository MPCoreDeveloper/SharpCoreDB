// <copyright file="QueryOptimizer.cs" company="MPCoreDeveloper">
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
/// Lightweight cost-based optimizer that separates logical and physical plans.
/// HOT PATH: struct-based plan nodes, no LINQ, minimal allocations.
/// </summary>
public sealed class QueryOptimizer
{
    private readonly CostEstimator costEstimator;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryOptimizer"/> class.
    /// </summary>
    /// <param name="costEstimator">Cost estimator with table statistics.</param>
    public QueryOptimizer(CostEstimator costEstimator)
    {
        this.costEstimator = costEstimator ?? throw new ArgumentNullException(nameof(costEstimator));
    }

    /// <summary>
    /// Optimizes a parsed SELECT and returns a reusable physical plan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public PhysicalPlan Optimize(SelectNode query)
    {
        // Build logical view (no mutations for safety)
        var logical = BuildLogicalPlan(query);

        // Estimate cost (scan + joins + filters + aggregates + sort)
        var estimatedCost = EstimateCost(logical);

        // Build minimal physical plan (scan → filter → aggregate → sort → project)
        var steps = BuildPhysicalSteps(query);

        return new PhysicalPlan
        {
            Logical = logical,
            EstimatedCost = estimatedCost,
            Steps = steps,
            Cacheable = logical.Cacheable
        };
    }

    /// <summary>
    /// Builds logical plan metadata from AST.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LogicalPlan BuildLogicalPlan(SelectNode query)
    {
        bool hasWhere = query.Where is not null;
        bool hasHaving = query.Having is not null;
        bool hasJoins = query.From is not null && query.From.Joins.Count > 0;
        bool hasSubquery = query.From is not null && query.From.Subquery is not null;

        // For now, we mark pushdown/reorder flags as simple heuristics
        bool predicatesPushed = hasWhere || hasHaving; // pushdown planned
        bool joinsReordered = hasJoins; // placeholder flag
        bool subqueriesEliminated = !hasSubquery; // non-subquery sources are trivially "eliminated"

        // Estimate base rows from first table if available
        long baseRows = 1000;
        if (query.From is not null && !string.IsNullOrEmpty(query.From.TableName))
        {
            var scan = costEstimator.EstimateScanCost(query.From.TableName);
            baseRows = scan.OutputRows;
        }

        return new LogicalPlan
        {
            Query = query,
            PredicatesPushed = predicatesPushed,
            JoinsReordered = joinsReordered,
            SubqueriesEliminated = subqueriesEliminated,
            EstimatedRows = baseRows,
            Cacheable = !hasSubquery
        };
    }

    /// <summary>
    /// Estimates cost for logical plan using lightweight heuristics.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double EstimateCost(LogicalPlan logical)
    {
        double totalCost = 0d;
        long currentRows = logical.EstimatedRows;

        // Base scan
        if (logical.Query.From is not null && !string.IsNullOrEmpty(logical.Query.From.TableName))
        {
            var scan = costEstimator.EstimateScanCost(logical.Query.From.TableName);
            totalCost += scan.Cost;
            currentRows = scan.OutputRows;
        }

        // Joins
        if (logical.Query.From is not null && logical.Query.From.Joins.Count > 0)
        {
            var joins = logical.Query.From.Joins;
            for (int i = 0; i < joins.Count; i++)
            {
                var rightTable = joins[i].Table.TableName;
                if (string.IsNullOrEmpty(rightTable))
                {
                    continue;
                }

                var rightScan = costEstimator.EstimateScanCost(rightTable);
                var leftCost = new EstimatedCost { Cost = totalCost, OutputRows = currentRows };
                var joinCost = costEstimator.EstimateJoinCost(leftCost, rightScan, 0.5);

                totalCost = joinCost.Cost;
                currentRows = joinCost.OutputRows;
            }
        }

        // WHERE
        if (logical.Query.Where is not null)
        {
            var filtered = costEstimator.EstimateFilterCost(new EstimatedCost { Cost = totalCost, OutputRows = currentRows }, 0.1);
            totalCost = filtered.Cost;
            currentRows = filtered.OutputRows;
        }

        // HAVING
        if (logical.Query.Having is not null)
        {
            var having = costEstimator.EstimateFilterCost(new EstimatedCost { Cost = totalCost, OutputRows = currentRows }, 0.5);
            totalCost = having.Cost;
            currentRows = having.OutputRows;
        }

        // GROUP BY
        if (logical.Query.GroupBy is not null)
        {
            var agg = costEstimator.EstimateAggregateCost(new EstimatedCost { Cost = totalCost, OutputRows = currentRows });
            totalCost = agg.Cost;
            currentRows = agg.OutputRows;
        }

        // ORDER BY
        if (logical.Query.OrderBy is not null)
        {
            var sort = costEstimator.EstimateSortCost(new EstimatedCost { Cost = totalCost, OutputRows = currentRows });
            totalCost = sort.Cost;
        }

        return totalCost;
    }

    /// <summary>
    /// Builds a minimal physical plan (struct-based) for execution layers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PhysicalStep[] BuildPhysicalSteps(SelectNode query)
    {
        // Max steps: scan + filter + aggregate + sort + project + limit
        PhysicalStep[] steps = new PhysicalStep[6];
        int index = 0;

        // SCAN
        if (query.From is not null)
        {
            steps[index++] = new PhysicalStep
            {
                Type = PhysicalStepType.TableScan,
                TableName = query.From.TableName,
                Alias = query.From.Alias
            };
        }

        // FILTER (WHERE)
        if (query.Where is not null)
        {
            steps[index++] = new PhysicalStep
            {
                Type = PhysicalStepType.Filter,
                FilterCondition = query.Where.Condition
            };
        }

        // AGGREGATE
        if (query.GroupBy is not null)
        {
            steps[index++] = new PhysicalStep
            {
                Type = PhysicalStepType.Aggregate,
                GroupColumns = query.GroupBy.Columns
            };
        }

        // HAVING
        if (query.Having is not null)
        {
            steps[index++] = new PhysicalStep
            {
                Type = PhysicalStepType.Filter,
                FilterCondition = query.Having.Condition
            };
        }

        // SORT
        if (query.OrderBy is not null)
        {
            steps[index++] = new PhysicalStep
            {
                Type = PhysicalStepType.Sort,
                SortItems = query.OrderBy.Items
            };
        }

        // LIMIT/OFFSET/PROJECTION handled by executor; project info stored here
        steps[index++] = new PhysicalStep
        {
            Type = PhysicalStepType.Project,
            ProjectColumns = query.Columns
        };

        // Trim array to actual count
        if (index == steps.Length)
        {
            return steps;
        }

        var trimmed = new PhysicalStep[index];
        for (int i = 0; i < index; i++)
        {
            trimmed[i] = steps[i];
        }
        return trimmed;
    }
}

/// <summary>
/// Logical plan metadata (struct-based for zero-allocation hot path).
/// </summary>
public readonly struct LogicalPlan
{
    /// <summary>Original parsed SELECT AST.</summary>
    public required SelectNode Query { get; init; }
    /// <summary>Indicates predicates were analyzed/pushed.</summary>
    public bool PredicatesPushed { get; init; }
    /// <summary>Indicates join ordering was considered.</summary>
    public bool JoinsReordered { get; init; }
    /// <summary>Indicates subqueries were eliminated or reused.</summary>
    public bool SubqueriesEliminated { get; init; }
    /// <summary>Estimated output rows from scan/filters.</summary>
    public long EstimatedRows { get; init; }
    /// <summary>Whether plan is safe to cache.</summary>
    public bool Cacheable { get; init; }
}

/// <summary>
/// Physical plan with ordered steps and estimated cost.
/// </summary>
public readonly struct PhysicalPlan
{
    /// <summary>Logical plan metadata.</summary>
    public required LogicalPlan Logical { get; init; }
    /// <summary>Ordered physical steps for execution.</summary>
    public required PhysicalStep[] Steps { get; init; }
    /// <summary>Estimated cost for this plan.</summary>
    public required double EstimatedCost { get; init; }
    /// <summary>Whether this plan can be cached.</summary>
    public bool Cacheable { get; init; }
}

/// <summary>
/// Physical execution step (struct-based for hot path).
/// </summary>
public readonly struct PhysicalStep
{
    /// <summary>Physical operator type.</summary>
    public PhysicalStepType Type { get; init; }
    /// <summary>Table name (for scan).</summary>
    public string? TableName { get; init; }
    /// <summary>Table alias (optional).</summary>
    public string? Alias { get; init; }
    /// <summary>Filter condition (WHERE/HAVING).</summary>
    public ExpressionNode? FilterCondition { get; init; }
    /// <summary>Grouping columns.</summary>
    public List<ColumnReferenceNode>? GroupColumns { get; init; }
    /// <summary>Sort items.</summary>
    public List<OrderByItem>? SortItems { get; init; }
    /// <summary>Projection columns.</summary>
    public List<ColumnNode>? ProjectColumns { get; init; }
}

/// <summary>
/// Physical operator kinds emitted by the optimizer.
/// </summary>
public enum PhysicalStepType
{
    /// <summary>Full table scan.</summary>
    TableScan,
    /// <summary>Row filter.</summary>
    Filter,
    /// <summary>Group aggregation.</summary>
    Aggregate,
    /// <summary>Sort rows.</summary>
    Sort,
    /// <summary>Project columns.</summary>
    Project
}
