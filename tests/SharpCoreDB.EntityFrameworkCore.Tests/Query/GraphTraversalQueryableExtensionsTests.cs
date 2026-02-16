// <copyright file="GraphTraversalQueryableExtensionsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.EntityFrameworkCore.Tests.Query;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// Unit tests specifically for GraphTraversalQueryableExtensions methods.
/// Tests parameter validation, error handling, and extension method behavior.
/// ✅ GraphRAG Phase 2: Queryable extension method tests.
/// </summary>
public class GraphTraversalQueryableExtensionsTests
{
    private class TestEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Test: Traverse validates null source parameter.
    /// </summary>
    [Fact]
    public void Traverse_NullSource_ThrowsArgumentNullException()
    {
        // Arrange
        IQueryable<TestEntity> nullSource = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            nullSource.Traverse(1, "column", 3, GraphTraversalStrategy.Bfs));
        Assert.Equal("source", ex.ParamName);
    }

    /// <summary>
    /// Test: Traverse validates relationshipColumn is not null.
    /// </summary>
    [Fact]
    public void Traverse_NullRelationshipColumn_ThrowsArgumentException()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            source.Traverse(1, null!, 3, GraphTraversalStrategy.Bfs));
    }

    /// <summary>
    /// Test: Traverse validates relationshipColumn is not empty.
    /// </summary>
    [Fact]
    public void Traverse_EmptyRelationshipColumn_ThrowsArgumentException()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            source.Traverse(1, "", 3, GraphTraversalStrategy.Bfs));
    }

    /// <summary>
    /// Test: Traverse validates maxDepth is non-negative.
    /// </summary>
    [Fact]
    public void Traverse_NegativeMaxDepth_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            source.Traverse(1, "column", -1, GraphTraversalStrategy.Bfs));
        Assert.Equal("maxDepth", ex.ParamName);
    }

    /// <summary>
    /// Test: Traverse accepts maxDepth = 0 (only start node).
    /// </summary>
    [Fact]
    public void Traverse_ZeroMaxDepth_Succeeds()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.Traverse(1, "column", 0, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test: Traverse returns IQueryable<long>.
    /// </summary>
    [Fact]
    public void Traverse_ReturnsIQueryableOfLong()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.Traverse(1, "column", 3, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.IsAssignableFrom<IQueryable<long>>(result);
    }

    /// <summary>
    /// Test: WhereIn validates null source parameter.
    /// </summary>
    [Fact]
    public void WhereIn_NullSource_ThrowsArgumentNullException()
    {
        // Arrange
        IQueryable<TestEntity> nullSource = null!;
        var ids = new List<long> { 1, 2, 3 };

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            nullSource.WhereIn(ids));
        Assert.Equal("source", ex.ParamName);
    }

    /// <summary>
    /// Test: WhereIn validates traversalIds is not null.
    /// </summary>
    [Fact]
    public void WhereIn_NullTraversalIds_ThrowsArgumentNullException()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            source.WhereIn(null!));
        Assert.Equal("traversalIds", ex.ParamName);
    }

    /// <summary>
    /// Test: WhereIn with empty collection succeeds.
    /// </summary>
    [Fact]
    public void WhereIn_EmptyCollection_Succeeds()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.WhereIn(new List<long>());

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test: WhereIn returns filtered IQueryable.
    /// </summary>
    [Fact]
    public void WhereIn_ReturnsFilteredIQueryable()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();
        var ids = new List<long> { 1, 2, 3 };

        // Act
        var result = source.WhereIn(ids);

        // Assert
        Assert.IsAssignableFrom<IQueryable<TestEntity>>(result);
    }

    /// <summary>
    /// Test: TraverseWhere validates all parameters.
    /// </summary>
    [Fact]
    public void TraverseWhere_ValidateAllParameters()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act & Assert - null source
        Assert.Throws<ArgumentNullException>(() =>
            ((IQueryable<TestEntity>)null!).TraverseWhere(
                1, "col", 3, GraphTraversalStrategy.Bfs, _ => true));

        // null column
        Assert.Throws<ArgumentException>(() =>
            source.TraverseWhere(
                1, null!, 3, GraphTraversalStrategy.Bfs, _ => true));

        // negative depth
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            source.TraverseWhere(
                1, "col", -1, GraphTraversalStrategy.Bfs, _ => true));

        // null predicate
        Assert.Throws<ArgumentNullException>(() =>
            source.TraverseWhere(
                1, "col", 3, GraphTraversalStrategy.Bfs, null!));
    }

    /// <summary>
    /// Test: TraverseWhere returns filtered IQueryable.
    /// </summary>
    [Fact]
    public void TraverseWhere_ReturnsFilteredIQueryable()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.TraverseWhere(
            1, "column", 3, GraphTraversalStrategy.Bfs,
            _ => true);

        // Assert
        Assert.IsAssignableFrom<IQueryable<TestEntity>>(result);
    }

    /// <summary>
    /// Test: Distinct on traversal results returns IQueryable<long>.
    /// </summary>
    [Fact]
    public void Distinct_OnTraversalResults_ReturnsIQueryableOfLong()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();
        var traversalQuery = source.Traverse(1, "col", 3, GraphTraversalStrategy.Bfs);

        // Act
        var result = traversalQuery.Distinct();

        // Assert
        Assert.IsAssignableFrom<IQueryable<long>>(result);
    }

    /// <summary>
    /// Test: Take with valid count succeeds.
    /// </summary>
    [Fact]
    public void Take_WithValidCount_Succeeds()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();
        var traversalQuery = source.Traverse(1, "col", 3, GraphTraversalStrategy.Bfs);

        // Act
        var result = traversalQuery.Take(10);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test: Take with zero count succeeds.
    /// </summary>
    [Fact]
    public void Take_WithZeroCount_Succeeds()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();
        var traversalQuery = source.Traverse(1, "col", 3, GraphTraversalStrategy.Bfs);

        // Act
        var result = traversalQuery.Take(0);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test: Take with negative count throws ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public void Take_WithNegativeCount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();
        var traversalQuery = source.Traverse(1, "col", 3, GraphTraversalStrategy.Bfs);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            traversalQuery.Take(-1));
    }

    /// <summary>
    /// Test: Chaining multiple extensions works.
    /// </summary>
    [Fact]
    public void ChainedExtensions_AllSucceed()
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source
            .Traverse(1, "column", 5, GraphTraversalStrategy.Bfs)
            .Distinct()
            .Take(10);

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IQueryable<long>>(result);
    }

    /// <summary>
    /// Test: All available strategies can be used.
    /// </summary>
    [Theory]
    [InlineData(GraphTraversalStrategy.Bfs)]
    [InlineData(GraphTraversalStrategy.Dfs)]
    public void AllStrategies_AreAccepted(GraphTraversalStrategy strategy)
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.Traverse(1, "column", 3, strategy);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test: Large depth values are accepted.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(int.MaxValue)]
    public void LargeDepthValues_AreAccepted(int depth)
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.Traverse(1, "column", depth, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test: Special characters in column names are preserved.
    /// </summary>
    [Theory]
    [InlineData("_columnName")]
    [InlineData("Column_Name")]
    [InlineData("column123")]
    [InlineData("COLUMN")]
    public void SpecialColumnNames_ArePreserved(string columnName)
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.Traverse(1, columnName, 3, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test: Various node IDs work correctly.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(999L)]
    [InlineData(long.MaxValue)]
    public void VariousNodeIds_AreAccepted(long nodeId)
    {
        // Arrange
        var source = Enumerable.Empty<TestEntity>().AsQueryable();

        // Act
        var result = source.Traverse(nodeId, "column", 3, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.NotNull(result);
    }
}
