// <copyright file="VectorIndexManagerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch.Tests;

using SharpCoreDB.VectorSearch;

/// <summary>
/// Tests for <see cref="VectorIndexManager"/> — manages live IVectorIndex instances.
/// </summary>
public sealed class VectorIndexManagerTests : IDisposable
{
    private readonly VectorIndexManager _manager = new(new VectorSearchOptions());

    [Fact]
    public void HasIndex_NoIndexBuilt_ReturnsFalse()
    {
        Assert.False(_manager.HasIndex("docs", "embedding"));
    }

    [Fact]
    public void IndexCount_Empty_ReturnsZero()
    {
        Assert.Equal(0, _manager.IndexCount);
    }

    [Fact]
    public void GetIndex_NoIndexBuilt_ReturnsNull()
    {
        Assert.Null(_manager.GetIndex("docs", "embedding"));
    }

    [Fact]
    public void BuildIndex_FlatType_CreatesSearchableIndex()
    {
        // Arrange
        var table = new FakeVectorTable(dimensions: 4, rowCount: 10);

        // Act
        var index = _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        // Assert
        Assert.NotNull(index);
        Assert.Equal(VectorIndexType.Flat, index.IndexType);
        Assert.Equal(10, index.Count);
        Assert.True(_manager.HasIndex("docs", "embedding"));
        Assert.Equal(1, _manager.IndexCount);
    }

    [Fact]
    public void BuildIndex_HnswType_CreatesSearchableIndex()
    {
        // Arrange
        var table = new FakeVectorTable(dimensions: 4, rowCount: 20);

        // Act
        var index = _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Hnsw);

        // Assert
        Assert.NotNull(index);
        Assert.Equal(VectorIndexType.Hnsw, index.IndexType);
        Assert.Equal(20, index.Count);
    }

    [Fact]
    public void BuildIndex_EmptyTable_CreatesEmptyIndex()
    {
        // Arrange
        var table = new FakeVectorTable(dimensions: 4, rowCount: 0);

        // Act
        var index = _manager.BuildIndex(table, "empty", "embedding", VectorIndexType.Flat);

        // Assert
        Assert.NotNull(index);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void BuildIndex_ReplacesExistingIndex()
    {
        // Arrange
        var table1 = new FakeVectorTable(dimensions: 4, rowCount: 5);
        var table2 = new FakeVectorTable(dimensions: 4, rowCount: 15);
        _manager.BuildIndex(table1, "docs", "embedding", VectorIndexType.Flat);

        // Act
        var newIndex = _manager.BuildIndex(table2, "docs", "embedding", VectorIndexType.Flat);

        // Assert
        Assert.Equal(15, newIndex.Count);
        Assert.Equal(1, _manager.IndexCount);
    }

    [Fact]
    public void DropIndex_ExistingIndex_ReturnsTrue()
    {
        // Arrange
        var table = new FakeVectorTable(dimensions: 4, rowCount: 5);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        // Act
        var dropped = _manager.DropIndex("docs", "embedding");

        // Assert
        Assert.True(dropped);
        Assert.False(_manager.HasIndex("docs", "embedding"));
        Assert.Equal(0, _manager.IndexCount);
    }

    [Fact]
    public void DropIndex_NonExistent_ReturnsFalse()
    {
        Assert.False(_manager.DropIndex("docs", "embedding"));
    }

    [Fact]
    public void BuildIndex_SearchReturnsCorrectResults()
    {
        // Arrange — 3 vectors in 4D: known distances
        var table = new FakeVectorTable(
        [
            [1f, 0f, 0f, 0f],
            [0f, 1f, 0f, 0f],
            [1f, 1f, 0f, 0f],
        ]);

        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);
        var index = _manager.GetIndex("docs", "embedding")!;

        // Act — search for [1,0,0,0], closest should be id=0
        float[] query = [1f, 0f, 0f, 0f];
        var results = index.Search(query, 2);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].Id); // exact match
        Assert.True(results[0].Distance < 0.01f);
    }

    [Fact]
    public void TotalMemoryBytes_WithIndexes_ReturnsPositiveValue()
    {
        // Arrange
        var table = new FakeVectorTable(dimensions: 4, rowCount: 100);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        // Assert
        Assert.True(_manager.TotalMemoryBytes > 0);
    }

    public void Dispose() => _manager.Dispose();
}
