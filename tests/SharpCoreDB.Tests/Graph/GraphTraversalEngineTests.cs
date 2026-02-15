// <copyright file="GraphTraversalEngineTests.cs" company="MPCoreDeveloper">
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
/// Unit tests for GraphTraversalEngine.
/// Validates BFS/DFS traversal logic with simple graphs.
/// </summary>
public class GraphTraversalEngineTests
{
    [Fact]
    public void Traverse_BfsDepthTwo_ReturnsExpectedCount()
    {
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        var result = engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Traverse_BfsDepthTwo_IncludesStartNode()
    {
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        var result = engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);

        Assert.Contains(1, result);
    }

    [Fact]
    public void Traverse_DfsDepthTwo_ReturnsExpectedCount()
    {
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        var result = engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Dfs);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Traverse_WithDepthZero_ReturnsOnlyStartNode()
    {
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        var result = engine.Traverse(table, 1, "next", 0, GraphTraversalStrategy.Bfs);

        Assert.Equal(1, result.Count);
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
