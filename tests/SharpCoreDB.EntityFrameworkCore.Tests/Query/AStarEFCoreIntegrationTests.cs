// <copyright file="AStarEFCoreIntegrationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.EntityFrameworkCore.Tests.Query;

using SharpCoreDB.EntityFrameworkCore.Query;
using SharpCoreDB.Graph;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// Integration tests for A* pathfinding in EF Core LINQ queries.
/// ✅ GraphRAG Phase 5: Advanced EF Core integration with strategy selection.
/// </summary>
public class AStarEFCoreIntegrationTests
{
    [Fact]
    public void GraphTraverse_WithStrategyBfs_ConfiguresCorrectly()
    {
        // Arrange
        var data = new List<TestNode>
        {
            new() { Id = 1, Next = 2 },
            new() { Id = 2, Next = 3 },
            new() { Id = 3, Next = 4 },
        }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(startNodeId: 1, "Next", maxDepth: 2);
        var configured = traversal.WithStrategy(GraphTraversalStrategy.Bfs);

        // Assert
        Assert.NotNull(configured);
        Assert.Equal(GraphTraversalStrategy.Bfs, configured.GetStrategy());
    }

    [Fact]
    public void GraphTraverse_WithStrategyDfs_ConfiguresCorrectly()
    {
        // Arrange
        var data = new List<TestNode>
        {
            new() { Id = 1, Next = 2 },
            new() { Id = 2, Next = 3 },
        }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 3);
        var configured = traversal.WithStrategy(GraphTraversalStrategy.Dfs);

        // Assert
        Assert.NotNull(configured);
        Assert.Equal(GraphTraversalStrategy.Dfs, configured.GetStrategy());
    }

    [Fact]
    public void GraphTraverse_WithStrategyAStar_ConfiguresCorrectly()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5);
        var configured = traversal.WithStrategy(GraphTraversalStrategy.AStar);

        // Assert
        Assert.Equal(GraphTraversalStrategy.AStar, configured.GetStrategy());
    }

    [Fact]
    public void GraphTraverse_WithHeuristicDepth_ConfiguresCorrectly()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5);
        var configured = traversal
            .WithStrategy(GraphTraversalStrategy.AStar)
            .WithHeuristic(AStarHeuristic.Depth);

        // Assert
        Assert.Equal(AStarHeuristic.Depth, configured.GetHeuristic());
    }

    [Fact]
    public void GraphTraverse_WithHeuristicUniform_ConfiguresCorrectly()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5);
        var configured = traversal
            .WithStrategy(GraphTraversalStrategy.AStar)
            .WithHeuristic(AStarHeuristic.Uniform);

        // Assert
        Assert.Equal(AStarHeuristic.Uniform, configured.GetHeuristic());
        Assert.Equal(GraphTraversalStrategy.AStar, configured.GetStrategy());
    }

    [Fact]
    public void GraphTraverse_WithAutoStrategy_EnablesOptimization()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5);
        var configured = traversal.WithAutoStrategy();

        // Assert
        Assert.True(configured.IsAutoStrategyEnabled());
    }

    [Fact]
    public void GraphTraverse_WithAutoStrategyAndStatistics_UsesProvidedStats()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();
        var stats = new GraphStatistics(totalNodes: 1000, totalEdges: 1500, estimatedDegree: 1.5);

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5);
        var configured = traversal.WithAutoStrategy(stats);

        // Assert
        Assert.True(configured.IsAutoStrategyEnabled());
        // Strategy will be auto-selected based on stats
    }

    [Fact]
    public void GraphTraverse_FluentChaining_AllowsMultipleConfigurations()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5)
            .WithStrategy(GraphTraversalStrategy.AStar)
            .WithHeuristic(AStarHeuristic.Depth);

        // Assert
        Assert.Equal(GraphTraversalStrategy.AStar, traversal.GetStrategy());
        Assert.Equal(AStarHeuristic.Depth, traversal.GetHeuristic());
    }

    [Fact]
    public void GraphTraverse_DefaultStrategy_IsBfs()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5);

        // Assert
        Assert.Equal(GraphTraversalStrategy.Bfs, traversal.GetStrategy());
    }

    [Fact]
    public void GraphTraverse_DefaultHeuristic_IsDepth()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5);

        // Assert
        Assert.Equal(AStarHeuristic.Depth, traversal.GetHeuristic());
    }

    [Fact]
    public void GraphTraverse_WithStrategyAfterAutoStrategy_DisablesAutoSelection()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act
        var traversal = data.GraphTraverse(1, "Next", 5)
            .WithAutoStrategy()
            .WithStrategy(GraphTraversalStrategy.Dfs);

        // Assert
        Assert.False(traversal.IsAutoStrategyEnabled());
        Assert.Equal(GraphTraversalStrategy.Dfs, traversal.GetStrategy());
    }

    [Fact]
    public void GraphTraverse_NullSource_ThrowsArgumentNullException()
    {
        // Arrange
        IQueryable<TestNode> nullSource = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            nullSource.GraphTraverse(1, "Next", 5));
    }

    [Fact]
    public void GraphTraverse_NullRelationshipColumn_ThrowsArgumentException()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            data.GraphTraverse(1, null!, 5));
    }

    [Fact]
    public void GraphTraverse_NegativeMaxDepth_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            data.GraphTraverse(1, "Next", -1));
    }

    [Fact]
    public void GraphTraverse_AllStrategies_CanBeConfigured()
    {
        // Arrange
        var data = new List<TestNode> { new() { Id = 1, Next = 2 } }.AsQueryable();

        // Act & Assert - Test each strategy
        var strategies = new[]
        {
            GraphTraversalStrategy.Bfs,
            GraphTraversalStrategy.Dfs,
            GraphTraversalStrategy.Bidirectional,
            GraphTraversalStrategy.Dijkstra,
            GraphTraversalStrategy.AStar
        };

        foreach (var strategy in strategies)
        {
            var traversal = data.GraphTraverse(1, "Next", 5).WithStrategy(strategy);
            Assert.Equal(strategy, traversal.GetStrategy());
        }
    }

    /// <summary>
    /// Test node entity for traversal tests.
    /// </summary>
    private class TestNode
    {
        public long Id { get; set; }
        public long Next { get; set; }
    }
}
