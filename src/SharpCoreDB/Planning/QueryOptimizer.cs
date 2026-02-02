// <copyright file="QueryOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Planning;

using System;
using System.Collections.Generic;
using System.Linq;
using SharpCoreDB.Storage.Columnar;

/// <summary>
/// Cost-based query optimizer.
/// C# 14: Primary constructors, collection expressions, modern patterns.
/// 
/// ✅ SCDB Phase 7.3: Query Plan Optimization
/// 
/// Purpose:
/// - Generate optimal query execution plans
/// - Cost-based plan selection
/// - Predicate pushdown optimization
/// - Join order optimization
/// - Plan caching for repeated queries
/// 
/// Performance Target: 10-100x better query plans
/// </summary>
public sealed class QueryOptimizer
{
    private readonly CardinalityEstimator _estimator;
    private readonly Dictionary<string, QueryPlan> _planCache = [];
    private const int MAX_CACHE_SIZE = 1000;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryOptimizer"/> class.
    /// </summary>
    /// <param name="estimator">Cardinality estimator.</param>
    public QueryOptimizer(CardinalityEstimator estimator)
    {
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
    }

    /// <summary>
    /// Optimizes a query and returns the best execution plan.
    /// </summary>
    /// <param name="query">Query to optimize.</param>
    /// <returns>Optimized query plan.</returns>
    public QueryPlan Optimize(QuerySpec query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Check plan cache
        var cacheKey = query.GetCacheKey();
        if (_planCache.TryGetValue(cacheKey, out var cachedPlan))
        {
            return cachedPlan;
        }

        // Generate candidate plans
        var candidates = GenerateCandidatePlans(query);

        // Select plan with lowest cost
        var bestPlan = candidates.OrderBy(p => p.EstimatedCost).First();

        // Cache the plan
        CachePlan(cacheKey, bestPlan);

        return bestPlan;
    }

    /// <summary>
    /// Generates candidate execution plans for a query.
    /// </summary>
    private List<QueryPlan> GenerateCandidatePlans(QuerySpec query)
    {
        var plans = new List<QueryPlan>();

        // Plan 1: Sequential scan with filter
        plans.Add(GenerateSequentialScanPlan(query));

        // Plan 2: Predicate pushdown to storage layer (Phase 7.2 integration)
        if (query.Predicates.Count > 0)
        {
            plans.Add(GeneratePredicatePushdownPlan(query));
        }

        // Plan 3: SIMD-accelerated scan (if applicable)
        if (CanUseSimd(query))
        {
            plans.Add(GenerateSimdScanPlan(query));
        }

        // Plan 4: Index scan (if indexes available)
        // TODO: Implement when index system is available

        return plans;
    }

    /// <summary>
    /// Generates a sequential scan plan.
    /// </summary>
    private QueryPlan GenerateSequentialScanPlan(QuerySpec query)
    {
        var plan = new QueryPlan
        {
            PlanType = PlanType.SequentialScan,
            TableName = query.TableName,
            Predicates = query.Predicates,
            SelectColumns = query.SelectColumns,
        };

        // Estimate cost
        var totalRows = query.EstimatedRowCount;
        var selectivity = query.Predicates.Count > 0
            ? _estimator.EstimateCombinedSelectivity(query.Predicates)
            : 1.0;

        plan.EstimatedCost = _estimator.EstimateScanCost(
            query.TableName,
            totalRows,
            hasFilter: query.Predicates.Count > 0,
            selectivity: selectivity
        );

        plan.EstimatedRows = (long)(totalRows * selectivity);

        return plan;
    }

    /// <summary>
    /// Generates a predicate pushdown plan (Phase 7.2 integration).
    /// </summary>
    private QueryPlan GeneratePredicatePushdownPlan(QuerySpec query)
    {
        var plan = new QueryPlan
        {
            PlanType = PlanType.PredicatePushdown,
            TableName = query.TableName,
            Predicates = query.Predicates,
            SelectColumns = query.SelectColumns,
            UsePredicatePushdown = true,
        };

        // Predicate pushdown reduces cost significantly
        var totalRows = query.EstimatedRowCount;
        var selectivity = _estimator.EstimateCombinedSelectivity(query.Predicates);

        // Base scan cost, but with early filtering benefit
        var baseCost = _estimator.EstimateScanCost(
            query.TableName,
            totalRows,
            hasFilter: true,
            selectivity: selectivity
        );

        // Pushdown reduces cost by ~5x (early filtering, less data movement)
        plan.EstimatedCost = baseCost / 5.0;
        plan.EstimatedRows = (long)(totalRows * selectivity);

        return plan;
    }

    /// <summary>
    /// Generates a SIMD-accelerated scan plan.
    /// </summary>
    private QueryPlan GenerateSimdScanPlan(QuerySpec query)
    {
        var plan = new QueryPlan
        {
            PlanType = PlanType.SimdScan,
            TableName = query.TableName,
            Predicates = query.Predicates,
            SelectColumns = query.SelectColumns,
            UseSimd = true,
        };

        var totalRows = query.EstimatedRowCount;
        var selectivity = _estimator.EstimateCombinedSelectivity(query.Predicates);

        var baseCost = _estimator.EstimateScanCost(
            query.TableName,
            totalRows,
            hasFilter: true,
            selectivity: selectivity
        );

        // SIMD reduces cost by ~50x (from Phase 7.2)
        plan.EstimatedCost = baseCost / 50.0;
        plan.EstimatedRows = (long)(totalRows * selectivity);

        return plan;
    }

    /// <summary>
    /// Checks if SIMD can be used for this query.
    /// </summary>
    private bool CanUseSimd(QuerySpec query)
    {
        // SIMD requires:
        // 1. Predicates on numeric columns
        // 2. Sufficient row count
        // 3. Column statistics available

        if (query.Predicates.Count == 0)
            return false;

        if (query.EstimatedRowCount < 128)
            return false; // Too small for SIMD

        // Check if all predicates are on columns with statistics
        return query.Predicates.All(p =>
        {
            var stats = _estimator.GetStatistics(p.ColumnName);
            return stats != null && ColumnarSimdBridge.ShouldUseSimd(stats, (int)query.EstimatedRowCount);
        });
    }

    /// <summary>
    /// Caches a query plan.
    /// </summary>
    private void CachePlan(string cacheKey, QueryPlan plan)
    {
        if (_planCache.Count >= MAX_CACHE_SIZE)
        {
            // Simple LRU: remove oldest entry
            var oldestKey = _planCache.Keys.First();
            _planCache.Remove(oldestKey);
        }

        _planCache[cacheKey] = plan;
    }

    /// <summary>
    /// Clears the plan cache.
    /// </summary>
    public void ClearCache()
    {
        _planCache.Clear();
    }

    /// <summary>
    /// Gets the current cache size.
    /// </summary>
    public int CacheSize => _planCache.Count;

    /// <summary>
    /// Optimizes join order for multi-table queries.
    /// Uses greedy algorithm: join smallest intermediate result first.
    /// </summary>
    /// <param name="tables">Tables to join.</param>
    /// <param name="joinConditions">Join conditions.</param>
    /// <returns>Optimized join order.</returns>
    public List<string> OptimizeJoinOrder(List<TableInfo> tables, List<JoinCondition> joinConditions)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(joinConditions);

        if (tables.Count <= 1)
            return tables.Select(t => t.Name).ToList();

        // Greedy join ordering: start with smallest table
        var remainingTables = new List<TableInfo>(tables);
        var joinOrder = new List<string>();

        // Pick smallest table first
        var current = remainingTables.OrderBy(t => t.RowCount).First();
        remainingTables.Remove(current);
        joinOrder.Add(current.Name);

        // Greedily add tables that produce smallest intermediate result
        while (remainingTables.Count > 0)
        {
            TableInfo? bestNext = null;
            long bestIntermediateSize = long.MaxValue;

            foreach (var candidate in remainingTables)
            {
                // Find join condition between current and candidate
                var joinCond = joinConditions.FirstOrDefault(jc =>
                    (jc.LeftTable == current.Name && jc.RightTable == candidate.Name) ||
                    (jc.LeftTable == candidate.Name && jc.RightTable == current.Name)
                );

                if (joinCond == null)
                    continue; // No join condition, skip

                var intermediateSize = _estimator.EstimateJoinSize(
                    current.RowCount,
                    joinCond.LeftColumn,
                    candidate.RowCount,
                    joinCond.RightColumn
                );

                if (intermediateSize < bestIntermediateSize)
                {
                    bestIntermediateSize = intermediateSize;
                    bestNext = candidate;
                }
            }

            if (bestNext == null)
            {
                // No joinable table found, add arbitrarily
                bestNext = remainingTables[0];
            }

            remainingTables.Remove(bestNext);
            joinOrder.Add(bestNext.Name);
            current = bestNext;
        }

        return joinOrder;
    }
}

/// <summary>
/// Query specification for optimization.
/// </summary>
public sealed record QuerySpec
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Columns to select.</summary>
    public List<string> SelectColumns { get; init; } = [];

    /// <summary>Filter predicates.</summary>
    public List<PredicateInfo> Predicates { get; init; } = [];

    /// <summary>Estimated total row count.</summary>
    public long EstimatedRowCount { get; init; }

    /// <summary>Gets cache key for plan caching.</summary>
    public string GetCacheKey()
    {
        var predicateKey = string.Join(",", Predicates.Select(p => $"{p.ColumnName}{p.Operator}{p.Value}"));
        var columnsKey = string.Join(",", SelectColumns);
        return $"{TableName}|{columnsKey}|{predicateKey}";
    }
}

/// <summary>
/// Query execution plan.
/// </summary>
public sealed record QueryPlan
{
    /// <summary>Plan type.</summary>
    public PlanType PlanType { get; init; }

    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Columns to select.</summary>
    public List<string> SelectColumns { get; init; } = [];

    /// <summary>Filter predicates.</summary>
    public List<PredicateInfo> Predicates { get; init; } = [];

    /// <summary>Estimated execution cost.</summary>
    public double EstimatedCost { get; set; }

    /// <summary>Estimated result rows.</summary>
    public long EstimatedRows { get; set; }

    /// <summary>Whether to use predicate pushdown.</summary>
    public bool UsePredicatePushdown { get; init; }

    /// <summary>Whether to use SIMD.</summary>
    public bool UseSimd { get; init; }
}

/// <summary>
/// Plan types.
/// </summary>
public enum PlanType
{
    /// <summary>Sequential scan.</summary>
    SequentialScan,

    /// <summary>Predicate pushdown to storage.</summary>
    PredicatePushdown,

    /// <summary>SIMD-accelerated scan.</summary>
    SimdScan,

    /// <summary>Index scan.</summary>
    IndexScan,
}

/// <summary>
/// Table information for join optimization.
/// </summary>
public sealed record TableInfo
{
    /// <summary>Table name.</summary>
    public required string Name { get; init; }

    /// <summary>Row count.</summary>
    public long RowCount { get; init; }
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
