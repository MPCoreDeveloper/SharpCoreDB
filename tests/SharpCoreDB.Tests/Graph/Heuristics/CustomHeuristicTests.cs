// <copyright file="CustomHeuristicTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph.Heuristics;

using SharpCoreDB.Graph.Heuristics;
using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Tests for custom A* heuristics.
/// ✅ GraphRAG Phase 6.2: Validates custom heuristic pathfinding.
/// </summary>
public class CustomHeuristicTests
{
    [Fact]
    public void CustomAStarPathfinder_WithLambdaHeuristic_FindsPath()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
            new() { ["id"] = 4L, ["next"] = DBNull.Value },
        };
        var table = new FakeGraphTable(rows, "next");

        // Simple depth-based heuristic
        CustomHeuristicFunction heuristic = (current, goal, depth, maxDepth, context) => maxDepth - depth;

        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act
        var result = pathfinder.FindPath(table, 1, 4, "next", 10);

        // Assert
        Assert.True(result.Success);
        Assert.Equal([1, 2, 3, 4], result.Path);
        Assert.Equal(3.0, result.TotalCost); // 3 edges
        Assert.True(result.NodesExplored >= 4);
    }

    [Fact]
    public void ManhattanDistanceHeuristic_WithGridGraph_FindsOptimalPath()
    {
        // Arrange: Simple grid graph
        // (0,0) -> (1,0) -> (2,0)
        //   |        |        |
        // (0,1) -> (1,1) -> (2,1)
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L }, // (0,0) -> (1,0)
            new() { ["id"] = 2L, ["next"] = 3L }, // (1,0) -> (2,0)
            new() { ["id"] = 3L, ["next"] = 6L }, // (2,0) -> (2,1)
            new() { ["id"] = 4L, ["next"] = 5L }, // (0,1) -> (1,1)
            new() { ["id"] = 5L, ["next"] = 6L }, // (1,1) -> (2,1)
            new() { ["id"] = 6L, ["next"] = DBNull.Value }, // Goal
        };
        var table = new FakeGraphTable(rows, "next");

        var positions = new Dictionary<long, (int X, int Y)>
        {
            [1] = (0, 0),
            [2] = (1, 0),
            [3] = (2, 0),
            [4] = (0, 1),
            [5] = (1, 1),
            [6] = (2, 1),
        };

        var context = new HeuristicContext { ["positions"] = positions };
        var heuristic = BuiltInHeuristics.ManhattanDistance();
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act
        var result = pathfinder.FindPath(table, 1, 6, "next", 10, context);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(1L, result.Path);
        Assert.Contains(6L, result.Path);
        Assert.Equal(6L, result.Path[^1]); // Last element is goal
    }

    [Fact]
    public void EuclideanDistanceHeuristic_WithContinuousGraph_FindsPath()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = DBNull.Value },
        };
        var table = new FakeGraphTable(rows, "next");

        var positions = new Dictionary<long, (double X, double Y)>
        {
            [1] = (0.0, 0.0),
            [2] = (3.0, 4.0),
            [3] = (6.0, 8.0),
        };

        var context = new HeuristicContext { ["positions"] = positions };
        var heuristic = BuiltInHeuristics.EuclideanDistance();
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act
        var result = pathfinder.FindPath(table, 1, 3, "next", 10, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal([1, 2, 3], result.Path);
    }

    [Fact]
    public void UniformCostHeuristic_BehavesLikeDijkstra()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = DBNull.Value },
        };
        var table = new FakeGraphTable(rows, "next");

        var heuristic = BuiltInHeuristics.UniformCost;
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act
        var result = pathfinder.FindPath(table, 1, 3, "next", 10);

        // Assert
        Assert.True(result.Success);
        Assert.Equal([1, 2, 3], result.Path);
    }

    [Fact]
    public void CustomHeuristic_WithContext_AccessesContextData()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = DBNull.Value },
        };
        var table = new FakeGraphTable(rows, "next");

        var priorities = new Dictionary<long, int>
        {
            [1] = 10,
            [2] = 5,
        };

        var context = new HeuristicContext { ["priorities"] = priorities };

        // Custom heuristic using priorities
        CustomHeuristicFunction priorityHeuristic = (current, goal, depth, maxDepth, ctx) =>
        {
            var prios = (Dictionary<long, int>)ctx["priorities"];
            return prios.TryGetValue(goal, out var prio) ? -prio : 0; // Negative to prioritize higher values
        };

        var pathfinder = new CustomAStarPathfinder(priorityHeuristic);

        // Act
        var result = pathfinder.FindPath(table, 1, 2, "next", 10, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal([1, 2], result.Path);
    }

    [Fact]
    public void CustomAStarPathfinder_NoPathExists_ReturnsFailure()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = DBNull.Value },
            new() { ["id"] = 2L, ["next"] = DBNull.Value },
        };
        var table = new FakeGraphTable(rows, "next");

        var heuristic = BuiltInHeuristics.UniformCost;
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act
        var result = pathfinder.FindPath(table, 1, 2, "next", 10);

        // Assert
        Assert.False(result.Success);
        Assert.Empty(result.Path);
        Assert.Equal(0, result.TotalCost);
    }

    [Fact]
    public void CustomAStarPathfinder_MaxDepthReached_ReturnsFailure()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
            new() { ["id"] = 4L, ["next"] = DBNull.Value },
        };
        var table = new FakeGraphTable(rows, "next");

        var heuristic = BuiltInHeuristics.DepthBased;
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act - max depth too small to reach goal
        var result = pathfinder.FindPath(table, 1, 4, "next", maxDepth: 2);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void HeuristicContext_GetTyped_ReturnsCorrectType()
    {
        // Arrange
        var context = new HeuristicContext
        {
            ["number"] = 42,
            ["text"] = "hello",
        };

        // Act
        var number = context.Get<int>("number");
        var text = context.Get<string>("text");

        // Assert
        Assert.Equal(42, number);
        Assert.Equal("hello", text);
    }

    [Fact]
    public void HeuristicContext_TryGetTyped_SucceedsWithValidData()
    {
        // Arrange
        var context = new HeuristicContext
        {
            ["value"] = 123,
        };

        // Act
        var success = context.TryGet<int>("value", out var result);

        // Assert
        Assert.True(success);
        Assert.Equal(123, result);
    }

    [Fact]
    public void HeuristicContext_TryGetTyped_FailsWithInvalidKey()
    {
        // Arrange
        var context = new HeuristicContext();

        // Act
        var success = context.TryGet<int>("missing", out var result);

        // Assert
        Assert.False(success);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ManhattanDistance_MissingPosition_ThrowsException()
    {
        // Arrange
        var positions = new Dictionary<long, (int X, int Y)>
        {
            [1] = (0, 0),
            // Missing node 2
        };

        var context = new HeuristicContext { ["positions"] = positions };
        var heuristic = BuiltInHeuristics.ManhattanDistance();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => heuristic(1, 2, 0, 10, context));
    }

    [Fact]
    public void CustomAStarPathfinder_WithWeightedEdges_CalculatesCorrectCost()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L, ["cost"] = 5.0 },
            new() { ["id"] = 2L, ["next"] = 3L, ["cost"] = 3.0 },
            new() { ["id"] = 3L, ["next"] = DBNull.Value, ["cost"] = 0.0 },
        };
        var table = new FakeGraphTable(rows, "next");

        var heuristic = BuiltInHeuristics.UniformCost;
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act
        var result = pathfinder.FindPathWithCosts(table, 1, 3, "next", "cost", 10);

        // Assert
        Assert.True(result.Success);
        Assert.Equal([1, 2, 3], result.Path);
        Assert.Equal(8.0, result.TotalCost); // 5 + 3
    }

    [Fact]
    public void CustomAStarPathfinder_Constructor_ThrowsOnNullHeuristic()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CustomAStarPathfinder(null!));
    }

    [Fact]
    public void CustomAStarPathfinder_FindPath_ThrowsOnNullTable()
    {
        // Arrange
        var heuristic = BuiltInHeuristics.UniformCost;
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => pathfinder.FindPath(null!, 1, 2, "next", 10));
    }

    [Fact]
    public void CustomAStarPathfinder_FindPath_ThrowsOnNegativeMaxDepth()
    {
        // Arrange
        var table = new FakeGraphTable([], "next");
        var heuristic = BuiltInHeuristics.UniformCost;
        var pathfinder = new CustomAStarPathfinder(heuristic);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => pathfinder.FindPath(table, 1, 2, "next", -1));
    }
}
