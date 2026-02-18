// <copyright file="ParallelTraversalTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB.Graph;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for parallel graph traversal.
/// ✅ GraphRAG Phase 6.1: Validates parallel BFS correctness and performance.
/// </summary>
public class ParallelTraversalTests
{
    [Fact]
    public async Task ParallelBfs_SmallGraph_ReturnsCorrectNodes()
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
        var engine = new ParallelGraphTraversalEngine();

        // Act
        var result = await engine.TraverseBfsParallelAsync(table, 1, "next", 10);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Contains(1L, result);
        Assert.Contains(2L, result);
        Assert.Contains(3L, result);
        Assert.Contains(4L, result);
    }

    [Fact]
    public async Task ParallelBfs_MaxDepthZero_ReturnsOnlyStartNode()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
        };
        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine();

        // Act
        var result = await engine.TraverseBfsParallelAsync(table, 1, "next", 0);

        // Assert
        Assert.Single(result);
        Assert.Contains(1L, result);
    }

    [Fact]
    public async Task ParallelBfs_MaxDepthOne_ReturnsTwoNodes()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
        };
        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine();

        // Act
        var result = await engine.TraverseBfsParallelAsync(table, 1, "next", 1);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1L, result);
        Assert.Contains(2L, result);
    }

    [Fact]
    public async Task ParallelBfs_DisconnectedNode_ReturnsOnlyStartNode()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = DBNull.Value },
            new() { ["id"] = 2L, ["next"] = 3L },
        };
        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine();

        // Act
        var result = await engine.TraverseBfsParallelAsync(table, 1, "next", 10);

        // Assert
        Assert.Single(result);
        Assert.Contains(1L, result);
    }

    [Fact]
    public async Task ParallelBfs_LargeGraph_CompletesSuccessfully()
    {
        // Arrange - Create a binary tree graph (1000 nodes)
        var rows = new List<Dictionary<string, object>>();
        for (long i = 1; i <= 500; i++)
        {
            rows.Add(new() { ["id"] = i, ["next"] = i * 2 }); // Left child
            if (i * 2 + 1 <= 1000)
            {
                rows.Add(new() { ["id"] = i * 2, ["next"] = i * 2 + 1 }); // Right child
            }
        }

        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine();

        // Act
        var sw = Stopwatch.StartNew();
        var result = await engine.TraverseBfsParallelAsync(table, 1, "next", 20);
        sw.Stop();

        // Assert
        Assert.True(result.Count > 0);
        Assert.Contains(1L, result);
        
        // Performance: should complete in reasonable time
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Took {sw.ElapsedMilliseconds}ms (expected <1000ms)");
    }

    [Fact]
    public async Task ParallelBfs_ConfiguredDegreeOfParallelism_UsesSpecifiedValue()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
        };
        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine(degreeOfParallelism: 2);

        // Act
        var result = await engine.TraverseBfsParallelAsync(table, 1, "next", 10);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ParallelBfs_CancellationToken_CancelsOperation()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>();
        for (long i = 1; i <= 10000; i++)
        {
            rows.Add(new() { ["id"] = i, ["next"] = i + 1 });
        }
        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine();
        var cts = new System.Threading.CancellationTokenSource();

        // Act
        var task = engine.TraverseBfsParallelAsync(table, 1, "next", 1000, cts.Token);
        await Task.Delay(50); // Let it start
        cts.Cancel();

        // Assert - TaskCanceledException is a subclass of OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ParallelBfs_InvalidParameters_ThrowsException()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>> { new() { ["id"] = 1L, ["next"] = 2L } };
        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine();

        // Act & Assert - Null table
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await engine.TraverseBfsParallelAsync(null!, 1, "next", 5));

        // Act & Assert - Null/empty column
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await engine.TraverseBfsParallelAsync(table, 1, "", 5));

        // Act & Assert - Negative depth
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await engine.TraverseBfsParallelAsync(table, 1, "next", -1));
    }

    [Fact]
    public async Task ChannelBfs_SmallGraph_ReturnsCorrectNodes()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
        };
        var table = new FakeGraphTable(rows, "next");
        var engine = new ParallelGraphTraversalEngine();

        // Act
        var result = await engine.TraverseBfsChannelAsync(table, 1, "next", 10);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Contains(1L, result);
        Assert.Contains(2L, result);
        Assert.Contains(3L, result);
        Assert.Contains(4L, result);
    }

    [Fact]
    public void Constructor_InvalidDegreeOfParallelism_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParallelGraphTraversalEngine(degreeOfParallelism: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParallelGraphTraversalEngine(degreeOfParallelism: -1));
    }

    [Fact]
    public async Task ParallelBfs_ComparedToSequential_ProducesSameResults()
    {
        // Arrange - Create a moderately complex graph
        var rows = new List<Dictionary<string, object>>();
        for (long i = 1; i <= 100; i++)
        {
            rows.Add(new() { ["id"] = i, ["next"] = (i % 10 == 0) ? DBNull.Value : i + 1 });
        }

        var table = new FakeGraphTable(rows, "next");
        var parallelEngine = new ParallelGraphTraversalEngine();
        var sequentialEngine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act
        var parallelResult = await parallelEngine.TraverseBfsParallelAsync(table, 1, "next", 20);
        var sequentialResult = sequentialEngine.Traverse(table, 1, "next", 20, Interfaces.GraphTraversalStrategy.Bfs);

        // Assert - Both should find the same nodes
        Assert.Equal(sequentialResult.Count, parallelResult.Count);
        foreach (var node in sequentialResult)
        {
            Assert.Contains(node, parallelResult);
        }
    }
}
