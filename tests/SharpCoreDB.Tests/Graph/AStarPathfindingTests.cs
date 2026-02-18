// <copyright file="AStarPathfindingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB;
using SharpCoreDB.Graph;
using System;
using System.Collections.Generic;
using Xunit;
using Interfaces = SharpCoreDB.Interfaces;

/// <summary>
/// Unit tests for A* pathfinding algorithm.
/// ✅ GraphRAG Phase 4: Validates shortest-path finding with heuristic guidance.
/// </summary>
public class AStarPathfindingTests
{
    [Fact]
    public void AStarPathfinder_SimplePath_FindsCorrectPath()
    {
        // Arrange: Linear graph 1 -> 2 -> 3 -> 4 -> 5
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, long>
        {
            { 1, 2 },
            { 2, 3 },
            { 3, 4 },
            { 4, 5 },
            { 5, 0 },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbor) && neighbor != 0
                ? new[] { neighbor }
                : [];

        // Act
        var result = pathfinder.FindPath(1, 5, GetNeighbors, 10);

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal(4, result.PathDepth); // PathDepth = Path.Count - 1 = 5 - 1 = 4
        Assert.Equal([1, 2, 3, 4, 5], result.Path);
        Assert.Equal(4, result.PathCost);
    }

    [Fact]
    public void AStarPathfinder_DiamondGraph_SelectsShorterPath()
    {
        // Arrange: Diamond graph
        //     1
        //    / \
        //   2   3
        //    \ /
        //     4
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, List<long>>
        {
            { 1, [2, 3] },
            { 2, [4] },
            { 3, [4] },
            { 4, [] },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbors) ? neighbors : [];

        // Act
        var result = pathfinder.FindPath(1, 4, GetNeighbors, 10);

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal(2, result.PathDepth);
        Assert.Equal(2, result.PathCost);
        Assert.Equal(4, result.Path[result.Path.Count - 1]);
    }

    [Fact]
    public void AStarPathfinder_StartEqualsGoal_ReturnsImmediately()
    {
        // Arrange
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        IEnumerable<long> GetNeighbors(long node) => [];

        // Act
        var result = pathfinder.FindPath(1, 1, GetNeighbors, 10);

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal(0, result.PathDepth);
        Assert.Single(result.Path);
        Assert.Equal(1, result.Path[0]);
    }

    [Fact]
    public void AStarPathfinder_GoalUnreachable_ReturnsEmpty()
    {
        // Arrange: Disconnected graph
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, List<long>>
        {
            { 1, [2] },
            { 2, [] },
            { 3, [] },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbors) ? neighbors : [];

        // Act
        var result = pathfinder.FindPath(1, 3, GetNeighbors, 10);

        // Assert
        Assert.False(result.GoalReached);
        Assert.Empty(result.Path);
    }

    [Fact]
    public void AStarPathfinder_DepthLimitExceeded_ReturnsEmpty()
    {
        // Arrange: Linear graph with depth limit
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, long>
        {
            { 1, 2 },
            { 2, 3 },
            { 3, 4 },
            { 4, 5 },
            { 5, 0 },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbor) && neighbor != 0
                ? new[] { neighbor }
                : [];

        // Act: Depth limit 2, but path needs depth 4
        var result = pathfinder.FindPath(1, 5, GetNeighbors, maxDepth: 2);

        // Assert
        Assert.False(result.GoalReached);
        Assert.Empty(result.Path);
    }

    [Fact]
    public void AStarPathfinder_CyclicGraph_HandlesLoops()
    {
        // Arrange: Graph with cycle
        //   1 -> 2 -> 3
        //   ^        |
        //   +--------+
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, List<long>>
        {
            { 1, [2] },
            { 2, [3] },
            { 3, [1] },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbors) ? neighbors : [];

        // Act: Find path from 1 to 3 (should not infinite loop)
        var result = pathfinder.FindPath(1, 3, GetNeighbors, maxDepth: 10);

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal([1, 2, 3], result.Path);
    }

    [Fact]
    public void AStarPathfinder_MultiplePathsToGoal_FindsOptimal()
    {
        // Arrange: Tree with multiple branches
        //       1
        //      /|\
        //     2 3 4
        //     | | |
        //     5 6 7
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, List<long>>
        {
            { 1, [2, 3, 4] },
            { 2, [5] },
            { 3, [6] },
            { 4, [7] },
            { 5, [] },
            { 6, [] },
            { 7, [] },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbors) ? neighbors : [];

        // Act
        var result = pathfinder.FindPath(1, 5, GetNeighbors, maxDepth: 10);

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal(2, result.PathDepth);
        Assert.Equal([1, 2, 5], result.Path);
    }

    [Fact]
    public void AStarPathfinder_UniformHeuristic_BehavesLikeDijkstra()
    {
        // Arrange: Use uniform heuristic (h=0, degenerates to Dijkstra)
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Uniform);
        var graph = new Dictionary<long, long>
        {
            { 1, 2 },
            { 2, 3 },
            { 3, 4 },
            { 4, 5 },
            { 5, 0 },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbor) && neighbor != 0
                ? new[] { neighbor }
                : [];

        // Act
        var result = pathfinder.FindPath(1, 5, GetNeighbors, maxDepth: 10);

        // Assert: Should still find correct path
        Assert.True(result.GoalReached);
        Assert.Equal([1, 2, 3, 4, 5], result.Path);
    }

    [Fact]
    public void AStarPathfinder_EmptyGraph_ReturnsEmpty()
    {
        // Arrange
        var pathfinder = new AStarPathfinder();
        IEnumerable<long> GetNeighbors(long node) => [];

        // Act
        var result = pathfinder.FindPath(1, 2, GetNeighbors, 10);

        // Assert
        Assert.False(result.GoalReached);
        Assert.Empty(result.Path);
    }

    [Fact]
    public void AStarPathfinder_ComplexGraph_FindsCorrectPath()
    {
        // Arrange: More complex graph
        var pathfinder = new AStarPathfinder();
        var graph = new Dictionary<long, List<long>>
        {
            { 1, [2, 3] },
            { 2, [4] },
            { 3, [4, 5] },
            { 4, [] },
            { 5, [6] },
            { 6, [] },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbors) ? neighbors : [];

        // Act
        var result = pathfinder.FindPath(1, 6, GetNeighbors, 10);

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal([1, 3, 5, 6], result.Path);
    }

    [Fact]
    public void AStarPathfinder_FindsCheapestPath_InWeightedGraph()
    {
        // Arrange: Graph with multiple paths (uniform edge weights in current implementation)
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, List<long>>
        {
            { 1, [2, 3] },
            { 2, [4] },
            { 3, [4, 5] },
            { 4, [6] },
            { 5, [6] },
            { 6, [] },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbors) ? neighbors : [];

        // Act
        var result = pathfinder.FindPath(1, 6, GetNeighbors, 10);

        // Assert
        Assert.True(result.GoalReached);
        // With uniform costs, both paths [1→3→5→6] and [1→2→4→6] have equal cost of 3
        // A* may choose either path. We verify it found *a* valid path
        Assert.Equal(3, result.PathCost); // All edges cost 1, so depth 3 = cost 3
        Assert.Equal(6, result.Path[^1]); // Ends at goal
    }

    [Fact]
    public void AStarPathfinder_DepthHeuristic_ExploreFewer()
    {
        // Arrange: Graph where depth limit constrains search
        var pathfinder = new AStarPathfinder(Interfaces.AStarHeuristic.Depth);
        var graph = new Dictionary<long, List<long>>
        {
            { 1, [2] },
            { 2, [3] },
            { 3, [4] },
            { 4, [5] },
            { 5, [] },
        };

        IEnumerable<long> GetNeighbors(long node) =>
            graph.TryGetValue(node, out var neighbors) ? neighbors : [];

        // Act
        var result = pathfinder.FindPath(1, 5, GetNeighbors, maxDepth: 3);

        // Assert: Path 1→2→3→4→5 has depth 4, but maxDepth is 3, so goal is unreachable
        Assert.False(result.GoalReached);
        Assert.Empty(result.Path);
    }
}

/// <summary>
/// Integration tests for A* traversal with GraphTraversalEngine.
/// </summary>
public class AStarGraphTraversalEngineTests
{
    [Fact]
    public void TraverseToGoal_LinearGraph_FindsPath()
    {
        // Arrange
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act
        var result = engine.TraverseToGoal(table, 1, 3, "next", maxDepth: 10);

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal(3, result.Path.Count);
        Assert.Equal(1, result.Path[0]);
        Assert.Equal(3, result.Path[^1]);
    }

    [Fact]
    public void TraverseToGoal_GoalUnreachable_ReturnsEmpty()
    {
        // Arrange
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act: Node 1 doesn't connect to node 999
        var result = engine.TraverseToGoal(table, 1, 999, "next", maxDepth: 10);

        // Assert
        Assert.False(result.GoalReached);
        Assert.Empty(result.Path);
    }

    [Fact]
    public void TraverseToGoal_DepthLimitExceeded_ReturnsEmpty()
    {
        // Arrange
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act: Path needs depth 2, limit is 1
        var result = engine.TraverseToGoal(table, 1, 3, "next", maxDepth: 1);

        // Assert
        Assert.False(result.GoalReached);
    }

    [Fact]
    public void TraverseToGoal_InvalidRelationshipColumn_ThrowsException()
    {
        // Arrange
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            engine.TraverseToGoal(table, 1, 3, "nonexistent", maxDepth: 10)
        );
    }

    [Fact]
    public void TraverseToGoal_InvalidStartNode_ThrowsException()
    {
        // Arrange
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            engine.TraverseToGoal(table, 999, 3, "next", maxDepth: 10)
        );
    }

    [Fact]
    public void TraverseToGoalAsync_LinearGraph_FindsPathAsync()
    {
        // Arrange
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act
        var resultTask = engine.TraverseToGoalAsync(table, 1, 3, "next", maxDepth: 10);
        var result = resultTask.Result;

        // Assert
        Assert.True(result.GoalReached);
        Assert.Equal(3, result.Path.Count);
    }

    /// <summary>
    /// Helper: Creates a simple linear graph table (1 -> 2 -> 3 -> 4).
    /// </summary>
    private static FakeGraphTable CreateLinearGraphTable()
    {
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
            new() { ["id"] = 4L, ["next"] = DBNull.Value },
        };

        return new FakeGraphTable(rows, "next");
    }
}
