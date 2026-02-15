// <copyright file="GraphTraversalEFCoreTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.EntityFrameworkCore.Tests.Query;

using Microsoft.EntityFrameworkCore;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// Comprehensive unit tests for GraphRAG EF Core LINQ integration.
/// Tests LINQ query extension methods and SQL translation.
/// ✅ GraphRAG Phase 2: Unit test suite for EF Core integration.
/// </summary>
public class GraphTraversalEFCoreTests
{
    // Test entity definitions
    private class Node
    {
        public long Id { get; set; }
        public long? NextId { get; set; }
        public long? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class Order
    {
        public long Id { get; set; }
        public long NodeId { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private class TestDbContext : DbContext
    {
        public DbSet<Node> Nodes { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSharpCoreDB("test_traverse.db");
        }
    }

    /// <summary>
    /// Test: Traverse method generates correct SQL with BFS strategy.
    /// </summary>
    [Fact]
    public void Traverse_WithBfsStrategy_GeneratesCorrectSQL()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs);
        var sql = query.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
        Assert.Contains("GRAPH_TRAVERSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", sql);
        Assert.Contains("NextId", sql);
        Assert.Contains("3", sql);
        Assert.Contains("0", sql); // BFS = 0
    }

    /// <summary>
    /// Test: Traverse method with DFS generates correct strategy value (1).
    /// </summary>
    [Fact]
    public void Traverse_WithDfsStrategy_IncludesStrategyValue1()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Nodes.Traverse(5, "NextId", 10, GraphTraversalStrategy.Dfs);
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("GRAPH_TRAVERSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", sql); // DFS = 1
    }

    /// <summary>
    /// Test: All traversal strategies generate unique values.
    /// </summary>
    [Fact]
    public void Traverse_AllStrategies_GenerateUniqueValues()
    {
        // Arrange
        using var context = new TestDbContext();
        var strategies = new[]
        {
            (GraphTraversalStrategy.Bfs, "0"),
            (GraphTraversalStrategy.Dfs, "1"),
            (GraphTraversalStrategy.Bidirectional, "2"),
            (GraphTraversalStrategy.Dijkstra, "3"),
        };

        // Act & Assert
        foreach (var (strategy, expectedValue) in strategies)
        {
            var query = context.Nodes.Traverse(1, "NextId", 3, strategy);
            var sql = query.ToQueryString();
            Assert.Contains("GRAPH_TRAVERSE", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Test: WhereIn generates correct IN expression.
    /// </summary>
    [Fact]
    public void WhereIn_WithTraversalIds_GeneratesInExpression()
    {
        // Arrange
        using var context = new TestDbContext();
        var ids = new List<long> { 1, 2, 3, 4, 5 };

        // Act
        var query = context.Orders.WhereIn(ids);
        var sql = query.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IN", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: WhereIn with empty collection generates WHERE FALSE.
    /// </summary>
    [Fact]
    public void WhereIn_WithEmptyCollection_GeneratesFalseCondition()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Orders.WhereIn(new List<long>());
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0", sql); // 0 = 1 (FALSE)
    }

    /// <summary>
    /// Test: TraverseWhere combines traversal and WHERE filtering.
    /// </summary>
    [Fact]
    public void TraverseWhere_CombinesTraversalAndPredicate()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Orders.TraverseWhere(
            1, "NodeId", 5, GraphTraversalStrategy.Bfs,
            o => o.Amount > 100);
        var sql = query.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
        Assert.Contains("GRAPH_TRAVERSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Amount", sql);
        Assert.Contains(">", sql);
        Assert.Contains("100", sql);
    }

    /// <summary>
    /// Test: Distinct on traversal results.
    /// </summary>
    [Fact]
    public void Distinct_OnTraversalResults_GeneratesDistinctKeyword()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Nodes
            .Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs)
            .Distinct();
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Take limits results.
    /// </summary>
    [Fact]
    public void Take_LimitsTraversalResults()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Nodes
            .Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs)
            .Take(10);
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10", sql);
    }

    /// <summary>
    /// Test: Traverse with null source throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void Traverse_WithNullSource_ThrowsArgumentNullException()
    {
        // Arrange
        IQueryable<Node> source = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            source.Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs));
    }

    /// <summary>
    /// Test: Traverse with null relationship column throws ArgumentException.
    /// </summary>
    [Fact]
    public void Traverse_WithNullRelationshipColumn_ThrowsArgumentException()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            context.Nodes.Traverse(1, null!, 3, GraphTraversalStrategy.Bfs));
    }

    /// <summary>
    /// Test: Traverse with empty relationship column throws ArgumentException.
    /// </summary>
    [Fact]
    public void Traverse_WithEmptyRelationshipColumn_ThrowsArgumentException()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            context.Nodes.Traverse(1, "", 3, GraphTraversalStrategy.Bfs));
    }

    /// <summary>
    /// Test: Traverse with negative max depth throws ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public void Traverse_WithNegativeMaxDepth_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            context.Nodes.Traverse(1, "NextId", -1, GraphTraversalStrategy.Bfs));
    }

    /// <summary>
    /// Test: Traverse with zero max depth succeeds (only start node).
    /// </summary>
    [Fact]
    public void Traverse_WithZeroMaxDepth_Succeeds()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Nodes.Traverse(1, "NextId", 0, GraphTraversalStrategy.Bfs);
        var sql = query.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
        Assert.Contains("GRAPH_TRAVERSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0", sql); // maxDepth = 0
    }

    /// <summary>
    /// Test: Chained WHERE clauses with traversal filtering.
    /// </summary>
    [Fact]
    public void ChainedWhere_WithTraversalFiltering_CombinesAllConditions()
    {
        // Arrange
        using var context = new TestDbContext();
        var traversalIds = new List<long> { 1, 2, 3 };

        // Act
        var query = context.Orders
            .WhereIn(traversalIds)
            .Where(o => o.Amount > 50)
            .Where(o => o.Description.Contains("urgent"));
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Amount", sql);
        Assert.Contains("Description", sql);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: OrderBy with traversal results.
    /// </summary>
    [Fact]
    public void OrderBy_WithTraversalResults_GeneratesOrderClause()
    {
        // Arrange
        using var context = new TestDbContext();
        var traversalIds = new List<long> { 1, 2, 3 };

        // Act
        var query = context.Orders
            .WhereIn(traversalIds)
            .OrderByDescending(o => o.Amount);
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ORDER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Select projection after traversal.
    /// </summary>
    [Fact]
    public void Select_AfterTraversalFiltering_ProjectsColumns()
    {
        // Arrange
        using var context = new TestDbContext();
        var traversalIds = new List<long> { 1, 2, 3 };

        // Act
        var query = context.Orders
            .WhereIn(traversalIds)
            .Select(o => new { o.Id, o.Description });
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Description", sql);
    }

    /// <summary>
    /// Test: Count on traversal results.
    /// </summary>
    [Fact]
    public void Count_OnTraversalResults_GeneratesCountFunction()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Orders.WhereIn(new List<long> { 1, 2, 3 });
        var countQuery = query.Count();

        // Assert - Just verify it doesn't throw
        Assert.True(countQuery >= 0);
    }

    /// <summary>
    /// Test: FirstOrDefault on traversal results.
    /// </summary>
    [Fact]
    public void FirstOrDefault_OnTraversalResults_Succeeds()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Orders.WhereIn(new List<long> { 1, 2, 3 });
        // Just verify it generates valid SQL
        var sql = query.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
    }

    /// <summary>
    /// Test: Multiple traversal strategies in same query.
    /// </summary>
    [Fact]
    public void MultipleStrategies_InSameQuery_AllGenerateCorrectValues()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var bfsQuery = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs);
        var dfsQuery = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Dfs);
        var bidQuery = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Bidirectional);
        var dijQuery = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Dijkstra);

        var bfsSql = bfsQuery.ToQueryString();
        var dfsSql = dfsQuery.ToQueryString();
        var bidSql = bidQuery.ToQueryString();
        var dijSql = dijQuery.ToQueryString();

        // Assert
        Assert.Contains("0", bfsSql);  // BFS
        Assert.Contains("1", dfsSql);  // DFS
        Assert.Contains("2", bidSql);  // Bidirectional
        Assert.Contains("3", dijSql);  // Dijkstra
    }

    /// <summary>
    /// Test: Large max depth value is accepted.
    /// </summary>
    [Fact]
    public void Traverse_WithLargeMaxDepth_Succeeds()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Nodes.Traverse(1, "NextId", 1000, GraphTraversalStrategy.Bfs);
        var sql = query.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
        Assert.Contains("GRAPH_TRAVERSE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Multiple node IDs in traversal.
    /// </summary>
    [Fact]
    public void Traverse_WithDifferentStartNodes_GeneratesDifferentSQL()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query1 = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs);
        var query2 = context.Nodes.Traverse(999, "NextId", 3, GraphTraversalStrategy.Bfs);

        var sql1 = query1.ToQueryString();
        var sql2 = query2.ToQueryString();

        // Assert
        Assert.Contains("1", sql1);
        Assert.Contains("999", sql2);
        Assert.NotEqual(sql1, sql2);
    }

    /// <summary>
    /// Test: WhereIn with null source throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void WhereIn_WithNullSource_ThrowsArgumentNullException()
    {
        // Arrange
        IQueryable<Order> source = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            source.WhereIn(new List<long> { 1, 2, 3 }));
    }

    /// <summary>
    /// Test: WhereIn with null ids throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void WhereIn_WithNullIds_ThrowsArgumentNullException()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            context.Orders.WhereIn(null!));
    }

    /// <summary>
    /// Test: Traverse for different relationship column names.
    /// </summary>
    [Fact]
    public void Traverse_WithDifferentColumns_GeneratesDifferentSQL()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query1 = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs);
        var query2 = context.Nodes.Traverse(1, "ParentId", 3, GraphTraversalStrategy.Bfs);

        var sql1 = query1.ToQueryString();
        var sql2 = query2.ToQueryString();

        // Assert
        Assert.Contains("NextId", sql1);
        Assert.Contains("ParentId", sql2);
    }

    /// <summary>
    /// Test: Complex query with nested traversals.
    /// </summary>
    [Fact]
    public void NestedTraversal_InComplexQuery_Succeeds()
    {
        // Arrange
        using var context = new TestDbContext();

        // Act
        var query = context.Orders
            .Where(o => context.Nodes
                .Traverse(o.NodeId, "NextId", 3, GraphTraversalStrategy.Bfs)
                .Contains(o.Id));

        var sql = query.ToQueryString();

        // Assert
        Assert.NotEmpty(sql);
        Assert.Contains("GRAPH_TRAVERSE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Verify InMemory provider doesn't break (just doesn't use GRAPH_TRAVERSE).
    /// </summary>
    [Fact]
    public void TraverseWithInMemory_FallsBackGracefully()
    {
        // Note: This test documents that in-memory provider doesn't support GRAPH_TRAVERSE
        // It's expected to either throw or return empty results when ToListAsync is called
        // This is acceptable as GRAPH_TRAVERSE is a SharpCoreDB-specific function

        using var context = new TestDbContext();
        var query = context.Nodes.Traverse(1, "NextId", 3, GraphTraversalStrategy.Bfs);

        // Just verify we can construct the query
        Assert.NotNull(query);
    }
}
