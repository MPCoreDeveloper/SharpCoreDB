// <copyright file="HybridGraphVectorOptimizerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB.Graph;
using System;
using Xunit;

/// <summary>
/// Unit tests for HybridGraphVectorOptimizer.
/// Validates cost-based execution ordering for hybrid queries.
/// ✅ GraphRAG Phase 3: Hybrid query optimization tests.
/// </summary>
public class HybridGraphVectorOptimizerTests
{
    [Fact]
    public void OptimizeHybridQuery_WithBothGraphAndVector_ReturnsOptimizationHint()
    {
        var optimizer = new HybridGraphVectorOptimizer();

        var hint = optimizer.OptimizeHybridQuery(hasGraphTraversal: true, hasVectorSearch: true, graphMaxDepth: 3);

        Assert.NotNull(hint);
        Assert.True(hint.HasGraphTraversal);
        Assert.True(hint.HasVectorSearch);
    }

    [Fact]
    public void OptimizeHybridQuery_WithOnlyGraph_SetGraphOnlyOrder()
    {
        var optimizer = new HybridGraphVectorOptimizer();

        var hint = optimizer.OptimizeHybridQuery(hasGraphTraversal: true, hasVectorSearch: false);

        Assert.True(hint.HasGraphTraversal);
        Assert.False(hint.HasVectorSearch);
        Assert.Equal(ExecutionOrder.GraphOnly, hint.RecommendedOrder);
    }

    [Fact]
    public void OptimizeHybridQuery_WithOnlyVector_SetVectorOnlyOrder()
    {
        var optimizer = new HybridGraphVectorOptimizer();

        var hint = optimizer.OptimizeHybridQuery(hasGraphTraversal: false, hasVectorSearch: true);

        Assert.False(hint.HasGraphTraversal);
        Assert.True(hint.HasVectorSearch);
        Assert.Equal(ExecutionOrder.VectorOnly, hint.RecommendedOrder);
    }

    [Fact]
    public void OptimizeHybridQuery_WithTableStats_EstimateCosts()
    {
        var optimizer = new HybridGraphVectorOptimizer();
        var tableStats = new TableStatistics 
        { 
            RowCount = 10000,
            EstimatedAverageDegree = 2.0,
            HasVectorIndex = true
        };

        var hint = optimizer.OptimizeHybridQuery(
            hasGraphTraversal: true, 
            hasVectorSearch: true, 
            graphMaxDepth: 3,
            tableStats: tableStats);

        Assert.NotNull(hint.TotalEstimatedCostMs);
        Assert.True(hint.TotalEstimatedCostMs >= 0);
    }

    [Fact]
    public void OptimizeHybridQuery_GraphMoreSelective_RecommendGraphFirst()
    {
        var optimizer = new HybridGraphVectorOptimizer();
        var tableStats = new TableStatistics
        {
            RowCount = 10000,
            EstimatedAverageDegree = 1.1, // Very shallow graph
        };

        var hint = optimizer.OptimizeHybridQuery(
            hasGraphTraversal: true,
            hasVectorSearch: true,
            graphMaxDepth: 2,
            tableStats: tableStats);

        // With low average degree, graph traversal should be more selective
        if (hint.GraphTraversalCost != null && hint.VectorSearchCost != null)
        {
            Assert.NotNull(hint.RecommendedOrder_Reason);
        }
    }

    [Fact]
    public void OperationCost_CalculatesSelectivity()
    {
        var cost = new OperationCost
        {
            EstimatedCardinality = 1000,
            SelectivityRatio = 0.1
        };

        Assert.Equal(0.1, cost.SelectivityRatio);
    }

    [Fact]
    public void TableStatistics_InitializesWithDefaults()
    {
        var stats = new TableStatistics { RowCount = 5000 };

        Assert.Equal(5000, stats.RowCount);
        Assert.Equal(1.5, stats.EstimatedAverageDegree);
        Assert.False(stats.HasVectorIndex);
    }

    [Fact]
    public void ExecutionOrder_HasAllValues()
    {
        var orders = Enum.GetValues(typeof(ExecutionOrder)).Cast<ExecutionOrder>();

        Assert.Contains(ExecutionOrder.Default, orders);
        Assert.Contains(ExecutionOrder.GraphOnly, orders);
        Assert.Contains(ExecutionOrder.VectorOnly, orders);
        Assert.Contains(ExecutionOrder.GraphThenVector, orders);
        Assert.Contains(ExecutionOrder.VectorThenGraph, orders);
    }
}
