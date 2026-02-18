// <copyright file="TraversalStrategyOptimizerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB;
using SharpCoreDB.Graph;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Unit tests for TraversalStrategyOptimizer.
/// Validates strategy selection based on graph characteristics.
/// ✅ GraphRAG Phase 3: Optimizer selection tests.
/// </summary>
public class TraversalStrategyOptimizerTests
{
    [Fact]
    public void RecommendStrategy_ShallowGraph_RecommendsBfs()
    {
        var table = CreateLinearGraphTable();
        var optimizer = new TraversalStrategyOptimizer(table, "next", 2, tableRowCount: 4);

        var recommendation = optimizer.RecommendStrategy();

        Assert.Equal(GraphTraversalStrategy.Bfs, recommendation.RecommendedStrategy);
    }

    [Fact]
    public void RecommendStrategy_WithStatistics_EstimatesCardinality()
    {
        var table = CreateLinearGraphTable();
        var stats = new GraphStatistics(
            totalNodes: 1000,
            totalEdges: 1500,
            estimatedDegree: 1.5);

        var optimizer = new TraversalStrategyOptimizer(table, "next", 3, stats, tableRowCount: 1000);
        var cardinality = optimizer.EstimateCardinality(GraphTraversalStrategy.Bfs);

        Assert.True(cardinality > 0);
        Assert.True(cardinality <= 1000);
    }

    [Fact]
    public void RecommendStrategy_AllStrategies_ProvideCostEstimates()
    {
        var table = CreateLinearGraphTable();
        var optimizer = new TraversalStrategyOptimizer(table, "next", 2, tableRowCount: 4);

        var recommendation = optimizer.RecommendStrategy();

        Assert.NotNull(recommendation.Cost);
        Assert.True(recommendation.Cost.TotalCost >= 0);
        Assert.True(recommendation.Cost.EstimatedCardinality >= 0);
    }

    [Fact]
    public void EstimateCardinality_ZeroDepth_ReturnsOne()
    {
        var table = CreateLinearGraphTable();
        var optimizer = new TraversalStrategyOptimizer(table, "next", 0, tableRowCount: 4);

        var cardinality = optimizer.EstimateCardinality(GraphTraversalStrategy.Bfs);

        Assert.Equal(1, cardinality);
    }

    [Fact]
    public void EstimateCardinality_DijkstraStrategy_ReturnsEstimate()
    {
        var table = CreateLinearGraphTable();
        var stats = new GraphStatistics(100, 150, 1.5);
        var optimizer = new TraversalStrategyOptimizer(table, "next", 3, stats, tableRowCount: 100);

        var cardinality = optimizer.EstimateCardinality(GraphTraversalStrategy.Dijkstra);

        Assert.True(cardinality > 0);
    }

    [Fact]
    public void GraphStatistics_CalculatesAverageDegree()
    {
        var stats = new GraphStatistics(totalNodes: 100, totalEdges: 150);

        Assert.Equal(1.5, stats.EstimatedDegree);
    }

    [Fact]
    public void GraphStatistics_ZeroNodes_AverageDegreeIsZero()
    {
        var stats = new GraphStatistics(totalNodes: 0, totalEdges: 0);

        Assert.Equal(0, stats.EstimatedDegree);
    }

    private static FakeGraphTable CreateLinearGraphTable()
    {
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
            new() { ["id"] = 4L, ["next"] = DBNull.Value }
        };

        return new FakeGraphTable(rows, "next");
    }
}
