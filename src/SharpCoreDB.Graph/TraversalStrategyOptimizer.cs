// <copyright file="TraversalStrategyOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;

/// <summary>
/// Selects the optimal traversal strategy (BFS/DFS/Bidirectional/Dijkstra) 
/// based on graph topology and query characteristics.
/// ✅ GraphRAG Phase 3: Automatic strategy selection for optimal performance.
/// </summary>
public sealed class TraversalStrategyOptimizer
{
    private readonly ITable _table;
    private readonly string _relationshipColumn;
    private readonly int _maxDepth;
    private readonly GraphStatistics? _statistics;
    private readonly long _tableRowCount;

    /// <summary>
    /// Initializes a new traversal optimizer instance.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="statistics">Optional graph statistics for cost estimation.</param>
    /// <param name="tableRowCount">The total row count of the table (for cardinality estimates).</param>
    public TraversalStrategyOptimizer(
        ITable table,
        string relationshipColumn,
        int maxDepth,
        GraphStatistics? statistics = null,
        long tableRowCount = 10_000)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _relationshipColumn = relationshipColumn ?? throw new ArgumentNullException(nameof(relationshipColumn));
        _maxDepth = maxDepth >= 0 ? maxDepth : throw new ArgumentOutOfRangeException(nameof(maxDepth));
        _statistics = statistics;
        _tableRowCount = tableRowCount > 0 ? tableRowCount : throw new ArgumentOutOfRangeException(nameof(tableRowCount));
    }

    /// <summary>
    /// Recommends the optimal traversal strategy based on graph characteristics.
    /// </summary>
    /// <returns>The recommended strategy and estimated cost.</returns>
    public TraversalRecommendation RecommendStrategy()
    {
        var bfsEstimate = EstimateBfsCost();
        var dfsEstimate = EstimateDfsCost();
        var bidirectionalEstimate = EstimateBidirectionalCost();
        var dijkstraEstimate = EstimateDijkstraCost();

        var strategies = new[]
        {
            (Strategy: GraphTraversalStrategy.Bfs, Cost: bfsEstimate),
            (Strategy: GraphTraversalStrategy.Dfs, Cost: dfsEstimate),
            (Strategy: GraphTraversalStrategy.Bidirectional, Cost: bidirectionalEstimate),
            (Strategy: GraphTraversalStrategy.Dijkstra, Cost: dijkstraEstimate),
        };

        var best = strategies[0];
        foreach (var (strategy, cost) in strategies)
        {
            if (cost.TotalCost < best.Cost.TotalCost)
            {
                best = (strategy, cost);
            }
        }

        return new TraversalRecommendation(best.Strategy, best.Cost);
    }

    /// <summary>
    /// Recommends the optimal strategy including A* for goal-directed traversal.
    /// ✅ GraphRAG Phase 4: A* with heuristic guidance for shortest-path queries.
    /// </summary>
    /// <param name="heuristic">The A* heuristic to use.</param>
    /// <returns>The recommended strategy and estimated cost.</returns>
    public TraversalRecommendation RecommendStrategyWithAStar(AStarHeuristic heuristic = AStarHeuristic.Depth)
    {
        var basedRecommendation = RecommendStrategy();

        // Only consider A* if we have weighted edges or need shortest paths
        // For now, we add A* as an additional option if it's beneficial
        var astarEstimate = EstimateAStarCost(heuristic);

        // Compare with current best
        if (astarEstimate.TotalCost < basedRecommendation.Cost.TotalCost)
        {
            return new TraversalRecommendation(GraphTraversalStrategy.AStar, astarEstimate);
        }

        return basedRecommendation;
    }

    /// <summary>
    /// Gets the estimated result cardinality for a given strategy.
    /// </summary>
    /// <param name="strategy">The traversal strategy.</param>
    /// <returns>Estimated number of reachable nodes.</returns>
    public long EstimateCardinality(GraphTraversalStrategy strategy)
    {
        if (_statistics?.EstimatedDegree == null)
        {
            // Conservative default: assume 10% of nodes per depth level
            return Math.Min(
                (long)Math.Pow(_tableRowCount * 0.1, _maxDepth),
                _tableRowCount);
        }

        var degree = _statistics.EstimatedDegree;
        var estimate = (long)Math.Pow(degree, _maxDepth);
        return Math.Min(estimate, _tableRowCount);
    }

    private StrategyCost EstimateBfsCost()
    {
        var cardinality = EstimateCardinality(GraphTraversalStrategy.Bfs);
        var nodeReadCost = cardinality * 0.001; // ~1μs per node read (O(1) ROWREF)
        var queueManagementCost = cardinality * 0.0001; // ~0.1μs per enqueue/dequeue
        var totalCost = nodeReadCost + queueManagementCost;

        return new StrategyCost(
            GraphTraversalStrategy.Bfs,
            cardinality,
            totalCost,
            "Breadth-first search. Best for shallow graphs with many paths. Guarantees shortest paths.",
            ScoreFactors.LevelBased);
    }

    private StrategyCost EstimateDfsCost()
    {
        var cardinality = EstimateCardinality(GraphTraversalStrategy.Dfs);
        var nodeReadCost = cardinality * 0.001; // ~1μs per node read
        var stackManagementCost = cardinality * 0.00008; // ~0.08μs per push/pop (slightly faster than queue)
        var totalCost = nodeReadCost + stackManagementCost;

        return new StrategyCost(
            GraphTraversalStrategy.Dfs,
            cardinality,
            totalCost,
            "Depth-first search. Best for deep hierarchies. Lower memory overhead than BFS.",
            ScoreFactors.DepthBased);
    }

    private StrategyCost EstimateBidirectionalCost()
    {
        // Bidirectional explores outgoing + incoming, potentially doubling edge access
        var singleCardinality = EstimateCardinality(GraphTraversalStrategy.Bfs);
        var cardinality = Math.Min(singleCardinality * 2, _tableRowCount);
        var nodeReadCost = cardinality * 0.001;
        var incomingEdgeLookupCost = cardinality * 0.005; // ~5μs per reverse edge lookup
        var totalCost = nodeReadCost + incomingEdgeLookupCost;

        return new StrategyCost(
            GraphTraversalStrategy.Bidirectional,
            cardinality,
            totalCost,
            "Bidirectional traversal. Best for discovering all connected nodes (both directions).",
            ScoreFactors.Undirected);
    }

    private StrategyCost EstimateDijkstraCost()
    {
        var cardinality = EstimateCardinality(GraphTraversalStrategy.Bfs);
        var nodeReadCost = cardinality * 0.001;
        var priorityQueueCost = cardinality * Math.Log(cardinality) * 0.0002; // O(n log n) for priority queue
        var totalCost = nodeReadCost + priorityQueueCost;

        return new StrategyCost(
            GraphTraversalStrategy.Dijkstra,
            cardinality,
            totalCost,
            "Dijkstra's algorithm. Best for weighted graphs. Guarantees shortest weighted paths.",
            ScoreFactors.Weighted);
    }

    private StrategyCost EstimateAStarCost(AStarHeuristic heuristic)
    {
        // A* combines node exploration with heuristic guidance
        var cardinality = EstimateCardinality(GraphTraversalStrategy.Bfs);
        var nodeReadCost = cardinality * 0.001;
        var heuristicCost = cardinality * 0.0005; // Assume heuristic evaluation is inexpensive
        var totalCost = nodeReadCost + heuristicCost;

        return new StrategyCost(
            GraphTraversalStrategy.AStar,
            cardinality,
            totalCost,
            "A* search. Best for goal-directed traversal with heuristic guidance.",
            ScoreFactors.Weighted | ScoreFactors.Sparse | ScoreFactors.Dense);
    }
}

/// <summary>
/// Recommendation for optimal traversal strategy.
/// </summary>
public readonly record struct TraversalRecommendation(
    GraphTraversalStrategy RecommendedStrategy,
    StrategyCost Cost);

/// <summary>
/// Cost breakdown for a traversal strategy.
/// </summary>
public sealed class StrategyCost
{
    public StrategyCost(
        GraphTraversalStrategy strategy,
        long estimatedCardinality,
        double totalCost,
        string rationale,
        ScoreFactors applicableFactors)
    {
        Strategy = strategy;
        EstimatedCardinality = estimatedCardinality;
        TotalCost = totalCost;
        Rationale = rationale;
        ApplicableFactors = applicableFactors;
    }

    /// <summary>
    /// The traversal strategy this cost applies to.
    /// </summary>
    public GraphTraversalStrategy Strategy { get; }

    /// <summary>
    /// Estimated number of nodes to visit.
    /// </summary>
    public long EstimatedCardinality { get; }

    /// <summary>
    /// Total estimated cost (in milliseconds, approximate).
    /// </summary>
    public double TotalCost { get; }

    /// <summary>
    /// Human-readable explanation of why this strategy is recommended/discouraged.
    /// </summary>
    public string Rationale { get; }

    /// <summary>
    /// Factors that make this strategy suitable.
    /// </summary>
    public ScoreFactors ApplicableFactors { get; }
}

/// <summary>
/// Graph statistics used for cost estimation.
/// </summary>
public sealed class GraphStatistics
{
    public GraphStatistics(
        long totalNodes,
        long totalEdges,
        double? estimatedDegree = null,
        double? cycleRatio = null)
    {
        TotalNodes = totalNodes;
        TotalEdges = totalEdges;
        EstimatedDegree = estimatedDegree ?? CalculateAverageDegree(totalNodes, totalEdges);
        CycleRatio = cycleRatio ?? 0.1; // Assume 10% of edges form cycles
    }

    /// <summary>
    /// Total number of nodes in the graph.
    /// </summary>
    public long TotalNodes { get; }

    /// <summary>
    /// Total number of edges in the graph.
    /// </summary>
    public long TotalEdges { get; }

    /// <summary>
    /// Estimated average out-degree of nodes.
    /// </summary>
    public double EstimatedDegree { get; }

    /// <summary>
    /// Estimated ratio of edges that form cycles (0.0 to 1.0).
    /// </summary>
    public double CycleRatio { get; }

    private static double CalculateAverageDegree(long totalNodes, long totalEdges)
    {
        if (totalNodes == 0)
            return 0;
        return (double)totalEdges / totalNodes;
    }
}

/// <summary>
/// Characteristics that favor a particular strategy.
/// </summary>
[Flags]
public enum ScoreFactors
{
    /// <summary>No specific factors.</summary>
    None = 0,

    /// <summary>Graph is organized by levels (breadth emphasis).</summary>
    LevelBased = 1,

    /// <summary>Graph is deep (depth emphasis).</summary>
    DepthBased = 2,

    /// <summary>Graph edges are bidirectional.</summary>
    Undirected = 4,

    /// <summary>Graph edges have weights.</summary>
    Weighted = 8,

    /// <summary>Many cycles in the graph.</summary>
    Cyclic = 16,

    /// <summary>Graph is sparse (low average degree).</summary>
    Sparse = 32,

    /// <summary>Graph is dense (high average degree).</summary>
    Dense = 64,
}
