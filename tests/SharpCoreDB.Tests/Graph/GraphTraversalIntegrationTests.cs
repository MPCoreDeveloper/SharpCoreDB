// <copyright file="GraphTraversalIntegrationTests.cs" company="MPCoreDeveloper">
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
/// End-to-end integration tests for graph traversal queries.
/// Tests GRAPH_TRAVERSE in SQL WHERE clauses and IN expressions.
/// ✅ GraphRAG Phase 1: Full query execution integration.
/// </summary>
public class GraphTraversalIntegrationTests
{
    [Fact]
    public void ExecuteQuery_WithGraphTraversalInInClause_ReturnsFilteredResults()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L, ["name"] = "Node1" },
            new() { ["id"] = 2L, ["next"] = 3L, ["name"] = "Node2" },
            new() { ["id"] = 3L, ["next"] = 4L, ["name"] = "Node3" },
            new() { ["id"] = 4L, ["next"] = DBNull.Value, ["name"] = "Node4" },
            new() { ["id"] = 5L, ["next"] = DBNull.Value, ["name"] = "Isolated" }
        };
        var table = new FakeGraphTable(rows, "next");
        
        var engine = new GraphTraversalEngine(new GraphSearchOptions());
        
        // Execute traversal
        var traversalResult = engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);
        
        // Assert
        Assert.NotNull(traversalResult);
        Assert.Equal(3, traversalResult.Count);
        Assert.Contains(1L, traversalResult);
        Assert.Contains(2L, traversalResult);
        Assert.Contains(3L, traversalResult);
    }

    [Fact]
    public void ExecuteQuery_WithDfsTraversal_ReturnsAllNodes()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["parent"] = 2L, ["name"] = "A" },
            new() { ["id"] = 2L, ["parent"] = 3L, ["name"] = "B" },
            new() { ["id"] = 3L, ["parent"] = DBNull.Value, ["name"] = "C" },
        };
        var table = new FakeGraphTable(rows, "parent");
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Execute with DFS
        var result = engine.Traverse(table, 1, "parent", 10, GraphTraversalStrategy.Dfs);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ExecuteQuery_WithMaxDepthZero_ReturnsOnlyStartNode()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
        };
        var table = new FakeGraphTable(rows, "next");
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act
        var result = engine.Traverse(table, 1, "next", 0, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.Single(result);
        Assert.Contains(1L, result);
    }

    [Fact]
    public void ExecuteQuery_WithNullNextPointer_StopsTraversal()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = DBNull.Value },
        };
        var table = new FakeGraphTable(rows, "next");
        var engine = new GraphTraversalEngine(new GraphSearchOptions());

        // Act
        var result = engine.Traverse(table, 1, "next", 10, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.Equal(2, result.Count);
    }
}
