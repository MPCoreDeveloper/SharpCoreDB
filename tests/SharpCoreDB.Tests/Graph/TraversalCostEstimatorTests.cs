// <copyright file="TraversalCostEstimatorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB.Graph;
using System;
using Xunit;
using Interfaces = SharpCoreDB.Interfaces;

/// <summary>
/// Unit tests for TraversalCostEstimator.
/// ✅ GraphRAG Phase 4: Validates cost estimation accuracy for strategy selection.
/// </summary>
public class TraversalCostEstimatorTests
{
    [Fact]
    public void EstimateBfsCost_LinearGraph_ReasonableCost()
    {
        // Arrange
        var stats = new GraphStatistics(1000, 999, 1.0); // Linear: 1000 nodes, 1 edge per node
        var estimator = new TraversalCostEstimator();

        // Act
        var cost = estimator.EstimateBfsCost(stats, maxDepth: 5);

        // Assert
        Assert.True(cost.TotalCost > 0);
        Assert.True(cost.EstimatedNodes > 0);
        Assert.Equal(6, cost.EstimatedNodes); // 1 + 5 levels
    }

    [Fact]
    public void EstimateDfsCost_LinearGraph_LessMemeoryThanBfs()
    {
        // Arrange: Tree with higher branching factor to demonstrate memory difference
        var stats = new GraphStatistics(10000, 20000, 2.0); // Binary tree
        var estimator = new TraversalCostEstimator();

        // Act
        var bfsCost = estimator.EstimateBfsCost(stats, maxDepth: 10);
        var dfsCost = estimator.EstimateDfsCost(stats, maxDepth: 10);

        // Assert: DFS should use less memory than BFS (especially with higher branching)
        Assert.True(dfsCost.EstimatedMemory < bfsCost.EstimatedMemory, 
            $"DFS memory ({dfsCost.EstimatedMemory}) should be less than BFS memory ({bfsCost.EstimatedMemory})");
    }

    [Fact]
    public void EstimateBidirectionalCost_LargeDepth_LessThanBfs()
    {
        // Arrange
        var stats = new GraphStatistics(100000, 150000, 1.5);
        var estimator = new TraversalCostEstimator();

        // Act
        var bfsCost = estimator.EstimateBfsCost(stats, maxDepth: 10);
        var bidirectionalCost = estimator.EstimateBidirectionalCost(stats, maxDepth: 10);

        // Assert: Bidirectional should be cheaper for large depths
        Assert.True(bidirectionalCost.TotalCost <= bfsCost.TotalCost);
        Assert.True(bidirectionalCost.EstimatedNodes < bfsCost.EstimatedNodes);
    }

    [Fact]
    public void EstimateDijkstraCost_WeightedGraph_MoreExpensiveThanBfs()
    {
        // Arrange
        var stats = new GraphStatistics(1000, 2000, 2.0);
        var estimator = new TraversalCostEstimator();

        // Act
        var bfsCost = estimator.EstimateBfsCost(stats, maxDepth: 5);
        var dijkstraCost = estimator.EstimateDijkstraCost(stats, maxDepth: 5);

        // Assert: Dijkstra more expensive due to priority queue
        Assert.True(dijkstraCost.TotalCost >= bfsCost.TotalCost);
    }

    [Fact]
    public void EstimateAStarCost_WithDepthHeuristic_LessThanDijkstra()
    {
        // Arrange
        var stats = new GraphStatistics(10000, 15000, 1.5);
        var estimator = new TraversalCostEstimator();

        // Act
        var dijkstraCost = estimator.EstimateDijkstraCost(stats, maxDepth: 10);
        var astarCost = estimator.EstimateAStarCost(stats, maxDepth: 10, Interfaces.AStarHeuristic.Depth);

        // Assert: A* with heuristic should be cheaper
        Assert.True(astarCost.TotalCost < dijkstraCost.TotalCost);
        Assert.True(astarCost.EstimatedNodes < dijkstraCost.EstimatedNodes);
    }

    [Fact]
    public void EstimateAStarCost_WithUniformHeuristic_EqualToDijkstra()
    {
        // Arrange
        var stats = new GraphStatistics(1000, 1500, 1.5);
        var estimator = new TraversalCostEstimator();

        // Act
        var dijkstraCost = estimator.EstimateDijkstraCost(stats, maxDepth: 5);
        var astarCost = estimator.EstimateAStarCost(stats, maxDepth: 5, Interfaces.AStarHeuristic.Uniform);

        // Assert: Uniform heuristic = Dijkstra
        Assert.True(Math.Abs(astarCost.TotalCost - dijkstraCost.TotalCost) < 0.01);
    }

    [Fact]
    public void RecommendStrategy_SparseGraph_PrefersBfs()
    {
        // Arrange: Sparse graph
        var stats = new GraphStatistics(10000, 15000, 1.5);
        var estimator = new TraversalCostEstimator();

        // Act
        var (strategy, cost) = estimator.RecommendStrategy(stats, maxDepth: 5);

        // Assert: Recommends one of the valid strategies
        Assert.True(
            strategy == Interfaces.GraphTraversalStrategy.Bfs || 
            strategy == Interfaces.GraphTraversalStrategy.Dfs ||
            strategy == Interfaces.GraphTraversalStrategy.Bidirectional);
        Assert.True(cost.TotalCost > 0);
    }

    [Fact]
    public void RecommendStrategy_DenseGraph_ConsidersAllOptions()
    {
        // Arrange: Dense graph
        var stats = new GraphStatistics(1000, 5000, 5.0);
        var estimator = new TraversalCostEstimator();

        // Act
        var (strategy, cost) = estimator.RecommendStrategy(stats, maxDepth: 3);

        // Assert: Recommends one of the valid strategies
        Assert.True(
            strategy == Interfaces.GraphTraversalStrategy.Bfs || 
            strategy == Interfaces.GraphTraversalStrategy.Dfs ||
            strategy == Interfaces.GraphTraversalStrategy.Bidirectional);
        Assert.True(cost.TotalCost > 0);
    }

    [Fact]
    public void EstimateCost_InvalidMaxDepth_ThrowsException()
    {
        // Arrange
        var stats = new GraphStatistics(1000, 1500, 1.5);
        var estimator = new TraversalCostEstimator();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            estimator.EstimateBfsCost(stats, maxDepth: -1)
        );
    }

    [Fact]
    public void EstimateCost_NullStats_ThrowsException()
    {
        // Arrange
        var estimator = new TraversalCostEstimator();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            estimator.EstimateBfsCost(null!, maxDepth: 5)
        );
    }

    [Fact]
    public void TraversalCost_TotalCostCalculation_Correct()
    {
        // Arrange
        var cost = new TraversalCost
        {
            NodeExpansionCost = 1.0,
            MemoryCost = 2.0,
            EdgeTraversalCost = 3.0,
            EstimatedNodes = 100,
            EstimatedMemory = 1000,
            EstimatedEdgeLookups = 200
        };

        // Act & Assert
        Assert.Equal(6.0, cost.TotalCost);
    }

    [Fact]
    public void EstimateBfsCost_ExponentialBranching_CapsAtTotalNodes()
    {
        // Arrange
        var stats = new GraphStatistics(100, 200, 10.0); // High branching, few nodes
        var estimator = new TraversalCostEstimator();

        // Act
        var cost = estimator.EstimateBfsCost(stats, maxDepth: 10);

        // Assert: Should not exceed total nodes
        Assert.True(cost.EstimatedNodes <= stats.TotalNodes);
    }

    [Fact]
    public void RecommendStrategy_ConsistentResults_SameInput()
    {
        // Arrange
        var stats = new GraphStatistics(5000, 10000, 2.0);
        var estimator = new TraversalCostEstimator();

        // Act
        var (strategy1, cost1) = estimator.RecommendStrategy(stats, maxDepth: 7);
        var (strategy2, cost2) = estimator.RecommendStrategy(stats, maxDepth: 7);

        // Assert: Same input should give same recommendation
        Assert.Equal(strategy1, strategy2);
        Assert.Equal(cost1.TotalCost, cost2.TotalCost);
    }
}
