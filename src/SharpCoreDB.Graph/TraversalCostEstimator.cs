// <copyright file="TraversalCostEstimator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Interfaces;
using System;

/// <summary>
/// Estimated cost of a traversal operation.
/// </summary>
public record TraversalCost
{
    /// <summary>
    /// Gets the estimated cost of expanding nodes during traversal.
    /// </summary>
    public double NodeExpansionCost { get; init; }

    /// <summary>
    /// Gets the estimated memory cost (based on frontier size).
    /// </summary>
    public double MemoryCost { get; init; }

    /// <summary>
    /// Gets the estimated cost of traversing edges.
    /// </summary>
    public double EdgeTraversalCost { get; init; }

    /// <summary>
    /// Gets the total estimated cost (sum of all components).
    /// </summary>
    public double TotalCost => NodeExpansionCost + MemoryCost + EdgeTraversalCost;

    /// <summary>
    /// Gets the estimated number of nodes that will be expanded.
    /// </summary>
    public long EstimatedNodes { get; init; }

    /// <summary>
    /// Gets the estimated memory required (in bytes).
    /// </summary>
    public long EstimatedMemory { get; init; }

    /// <summary>
    /// Gets the estimated number of edge lookups required.
    /// </summary>
    public long EstimatedEdgeLookups { get; init; }
}

/// <summary>
/// Estimates traversal costs for different strategies.
/// ✅ GraphRAG Phase 4: Cost-based strategy selection.
/// </summary>
public sealed class TraversalCostEstimator
{
    /// <summary>
    /// Cost of a single node expansion (in milliseconds).
    /// </summary>
    private const double NodeExpansionCostPerNode = 0.001; // ~1μs

    /// <summary>
    /// Cost of edge traversal/lookup (in milliseconds).
    /// </summary>
    private const double EdgeTraversalCostPerEdge = 0.0001; // ~0.1μs

    /// <summary>
    /// Memory cost per node in frontier (in milliseconds equivalent).
    /// </summary>
    private const double MemoryCostPerNode = 0.0001; // ~0.1μs

    /// <summary>
    /// Initializes a new traversal cost estimator.
    /// </summary>
    public TraversalCostEstimator()
    {
    }

    /// <summary>
    /// Estimates the cost of BFS traversal.
    /// </summary>
    /// <param name="stats">Graph statistics.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <returns>Estimated cost.</returns>
    public TraversalCost EstimateBfsCost(GraphStatistics stats, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));

        // BFS expands roughly branching_factor^depth nodes
        var bf = stats.EstimatedDegree;
        long estimatedNodes;

        if (bf <= 1.0)
        {
            // Linear or tree-like
            estimatedNodes = maxDepth + 1;
        }
        else if (bf < 2.0)
        {
            // Exponential but bounded
            estimatedNodes = (long)Math.Min(
                Math.Pow(bf, maxDepth + 1),
                stats.TotalNodes
            );
        }
        else
        {
            // Rapid expansion
            estimatedNodes = (long)Math.Min(
                Math.Pow(bf, maxDepth),
                stats.TotalNodes
            );
        }

        var frontierSize = (long)(Math.Sqrt(estimatedNodes) * bf);
        var edgeLookups = estimatedNodes * (long)bf;

        return new TraversalCost
        {
            EstimatedNodes = estimatedNodes,
            EstimatedMemory = frontierSize * 32, // Rough: 32 bytes per node in queue
            EstimatedEdgeLookups = edgeLookups,
            NodeExpansionCost = estimatedNodes * NodeExpansionCostPerNode,
            MemoryCost = frontierSize * MemoryCostPerNode,
            EdgeTraversalCost = edgeLookups * EdgeTraversalCostPerEdge
        };
    }

    /// <summary>
    /// Estimates the cost of DFS traversal.
    /// </summary>
    /// <param name="stats">Graph statistics.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <returns>Estimated cost.</returns>
    public TraversalCost EstimateDfsCost(GraphStatistics stats, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));

        // DFS visits similar number of nodes as BFS but with less memory
        var bfsCost = EstimateBfsCost(stats, maxDepth);

        // DFS memory is proportional to depth (call stack), not frontier
        var stackMemory = maxDepth * 16; // Rough: 16 bytes per stack frame

        return new TraversalCost
        {
            EstimatedNodes = bfsCost.EstimatedNodes,
            EstimatedMemory = stackMemory,
            EstimatedEdgeLookups = bfsCost.EstimatedEdgeLookups,
            NodeExpansionCost = bfsCost.NodeExpansionCost,
            MemoryCost = (maxDepth * MemoryCostPerNode) * 0.1, // Much less than BFS
            EdgeTraversalCost = bfsCost.EdgeTraversalCost
        };
    }

    /// <summary>
    /// Estimates the cost of bidirectional traversal.
    /// </summary>
    /// <param name="stats">Graph statistics.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <returns>Estimated cost.</returns>
    public TraversalCost EstimateBidirectionalCost(GraphStatistics stats, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));

        // Bidirectional: explores ~sqrt(bfs_nodes) from each direction
        var bfsCost = EstimateBfsCost(stats, maxDepth);

        // Bidirectional reduces node expansion due to early meeting
        // Approximation: sqrt of BFS nodes from each side
        var reducedNodes = (long)Math.Sqrt(bfsCost.EstimatedNodes * 2);
        var reductionFactor = Math.Min(1.0, (double)reducedNodes / bfsCost.EstimatedNodes);

        return new TraversalCost
        {
            EstimatedNodes = (long)(bfsCost.EstimatedNodes * reductionFactor),
            EstimatedMemory = bfsCost.EstimatedMemory, // Same memory requirement
            EstimatedEdgeLookups = (long)(bfsCost.EstimatedEdgeLookups * reductionFactor),
            NodeExpansionCost = bfsCost.NodeExpansionCost * reductionFactor,
            MemoryCost = bfsCost.MemoryCost,
            EdgeTraversalCost = bfsCost.EdgeTraversalCost * reductionFactor
        };
    }

    /// <summary>
    /// Estimates the cost of Dijkstra traversal.
    /// </summary>
    /// <param name="stats">Graph statistics.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <returns>Estimated cost.</returns>
    public TraversalCost EstimateDijkstraCost(GraphStatistics stats, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));

        // Dijkstra may visit all nodes (worst case)
        var bfsCost = EstimateBfsCost(stats, maxDepth);
        var estimatedNodes = Math.Min(stats.TotalNodes, (long)Math.Pow(10, maxDepth));

        // Priority queue operations are O(log n)
        var queueOperations = estimatedNodes * Math.Log2(estimatedNodes);

        return new TraversalCost
        {
            EstimatedNodes = estimatedNodes,
            EstimatedMemory = estimatedNodes * 48, // Larger: 48 bytes per priority queue entry
            EstimatedEdgeLookups = estimatedNodes * (long)stats.EstimatedDegree,
            NodeExpansionCost = estimatedNodes * NodeExpansionCostPerNode,
            MemoryCost = estimatedNodes * MemoryCostPerNode * 1.5,
            EdgeTraversalCost = (long)queueOperations * EdgeTraversalCostPerEdge
        };
    }

    /// <summary>
    /// Estimates the cost of A* traversal with heuristic guidance.
    /// </summary>
    /// <param name="stats">Graph statistics.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <param name="heuristic">Heuristic type (affects pruning effectiveness).</param>
    /// <returns>Estimated cost.</returns>
    public TraversalCost EstimateAStarCost(GraphStatistics stats, int maxDepth, AStarHeuristic heuristic = AStarHeuristic.Depth)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));

        // A* with good heuristic can reduce node expansion significantly
        var dijkstraCost = EstimateDijkstraCost(stats, maxDepth);

        // Heuristic effectiveness (pruning factor)
        var pruningFactor = heuristic switch
        {
            AStarHeuristic.Depth => 0.3,    // Depth heuristic prunes 70% of nodes
            AStarHeuristic.Uniform => 1.0,  // No pruning (equivalent to Dijkstra)
            _ => 0.5
        };

        return new TraversalCost
        {
            EstimatedNodes = (long)(dijkstraCost.EstimatedNodes * pruningFactor),
            EstimatedMemory = dijkstraCost.EstimatedMemory,
            EstimatedEdgeLookups = (long)(dijkstraCost.EstimatedEdgeLookups * pruningFactor),
            NodeExpansionCost = dijkstraCost.NodeExpansionCost * pruningFactor,
            MemoryCost = dijkstraCost.MemoryCost,
            EdgeTraversalCost = dijkstraCost.EdgeTraversalCost * pruningFactor
        };
    }

    /// <summary>
    /// Recommends the best strategy based on graph characteristics and traversal parameters.
    /// </summary>
    /// <param name="stats">Graph statistics.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <returns>Recommended strategy and its estimated cost.</returns>
    public (GraphTraversalStrategy Strategy, TraversalCost Cost) RecommendStrategy(
        GraphStatistics stats,
        int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));

        var bfsCost = EstimateBfsCost(stats, maxDepth);
        var dfsCost = EstimateDfsCost(stats, maxDepth);
        var bidirectionalCost = EstimateBidirectionalCost(stats, maxDepth);

        // Cost comparison
        var minCost = Math.Min(
            Math.Min(bfsCost.TotalCost, dfsCost.TotalCost),
            bidirectionalCost.TotalCost
        );

        if (bfsCost.TotalCost == minCost)
        {
            return (GraphTraversalStrategy.Bfs, bfsCost);
        }
        else if (dfsCost.TotalCost == minCost)
        {
            return (GraphTraversalStrategy.Dfs, dfsCost);
        }
        else
        {
            return (GraphTraversalStrategy.Bidirectional, bidirectionalCost);
        }
    }
}
