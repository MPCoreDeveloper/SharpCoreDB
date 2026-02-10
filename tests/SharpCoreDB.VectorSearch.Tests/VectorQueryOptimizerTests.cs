// <copyright file="VectorQueryOptimizerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch.Tests;

using SharpCoreDB.VectorSearch;

/// <summary>
/// Tests for <see cref="VectorQueryOptimizer"/> — detects vec_distance patterns
/// and routes to vector indexes.
/// </summary>
public sealed class VectorQueryOptimizerTests : IDisposable
{
    private readonly VectorIndexManager _manager = new(new VectorSearchOptions());
    private readonly VectorQueryOptimizer _optimizer;

    public VectorQueryOptimizerTests()
    {
        _optimizer = new VectorQueryOptimizer(_manager);
    }

    [Fact]
    public void CanOptimize_NoIndex_ReturnsFalse()
    {
        Assert.False(_optimizer.CanOptimize("docs", "embedding", "VEC_DISTANCE_COSINE", 10));
    }

    [Fact]
    public void CanOptimize_WithIndex_ReturnsTrue()
    {
        // Arrange
        var table = CreateTable(4, 5);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        // Act & Assert
        Assert.True(_optimizer.CanOptimize("docs", "embedding", "VEC_DISTANCE_COSINE", 10));
    }

    [Fact]
    public void CanOptimize_InvalidFunction_ReturnsFalse()
    {
        var table = CreateTable(4, 5);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        Assert.False(_optimizer.CanOptimize("docs", "embedding", "UNKNOWN_FUNC", 10));
    }

    [Fact]
    public void CanOptimize_ZeroLimit_ReturnsFalse()
    {
        var table = CreateTable(4, 5);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        Assert.False(_optimizer.CanOptimize("docs", "embedding", "VEC_DISTANCE_COSINE", 0));
    }

    [Theory]
    [InlineData("VEC_DISTANCE_COSINE")]
    [InlineData("VEC_DISTANCE_L2")]
    [InlineData("VEC_DISTANCE_DOT")]
    public void CanOptimize_AllDistanceFunctions_ReturnTrue(string funcName)
    {
        var table = CreateTable(4, 5);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        Assert.True(_optimizer.CanOptimize("docs", "embedding", funcName, 5));
    }

    [Fact]
    public void ExecuteOptimized_ReturnsTopKResults()
    {
        // Arrange — 10 vectors in 4D, search for [1,0,0,0]
        var table = CreateTableWithKnownVectors();
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        float[] query = [1f, 0f, 0f, 0f];

        // Act
        var results = _optimizer.ExecuteOptimized(
            table, "docs", "embedding", "VEC_DISTANCE_COSINE",
            "[1.0, 0.0, 0.0, 0.0]", 3, false);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.True(results.All(r => r.ContainsKey("distance")));
        // Results should be sorted by distance ascending
        for (int i = 1; i < results.Count; i++)
        {
            Assert.True((float)results[i]["distance"] >= (float)results[i - 1]["distance"]);
        }
    }

    [Fact]
    public void ExecuteOptimized_NullQueryVector_ThrowsArgumentException()
    {
        var table = CreateTable(4, 5);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        Assert.Throws<ArgumentException>(() =>
            _optimizer.ExecuteOptimized(table, "docs", "embedding",
                "VEC_DISTANCE_COSINE", null, 5, false));
    }

    [Fact]
    public void ExecuteOptimized_NoIndex_ThrowsInvalidOperationException()
    {
        var table = CreateTable(4, 5);

        Assert.Throws<InvalidOperationException>(() =>
            _optimizer.ExecuteOptimized(table, "docs", "embedding",
                "VEC_DISTANCE_COSINE", "[1,0,0,0]", 5, false));
    }

    [Fact]
    public void GetExplainPlan_NoIndex_ReturnsFullScan()
    {
        var plan = _optimizer.GetExplainPlan("docs", "embedding");

        Assert.Contains("no index", plan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetExplainPlan_FlatIndex_ReturnsExactScan()
    {
        var table = CreateTable(4, 10);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Flat);

        var plan = _optimizer.GetExplainPlan("docs", "embedding");

        Assert.Contains("Flat", plan);
        Assert.Contains("count=10", plan);
    }

    [Fact]
    public void GetExplainPlan_HnswIndex_ReturnsHnswScan()
    {
        var table = CreateTable(4, 10);
        _manager.BuildIndex(table, "docs", "embedding", VectorIndexType.Hnsw);

        var plan = _optimizer.GetExplainPlan("docs", "embedding");

        Assert.Contains("HNSW", plan);
    }

    [Fact]
    public void BuildIndex_ViaOptimizer_CreatesIndex()
    {
        var table = CreateTable(4, 10);

        _optimizer.BuildIndex(table, "docs", "embedding", "HNSW");

        Assert.True(_manager.HasIndex("docs", "embedding"));
    }

    [Fact]
    public void DropIndex_ViaOptimizer_RemovesIndex()
    {
        var table = CreateTable(4, 10);
        _optimizer.BuildIndex(table, "docs", "embedding", "FLAT");

        _optimizer.DropIndex("docs", "embedding");

        Assert.False(_manager.HasIndex("docs", "embedding"));
    }

    public void Dispose() => _manager.Dispose();

    private static FakeVectorTable CreateTable(int dimensions, int rowCount)
        => new(dimensions, rowCount);

    private static FakeVectorTable CreateTableWithKnownVectors()
    {
        float[][] vectors =
        [
            [1f, 0f, 0f, 0f],   // id=0: exact match for [1,0,0,0]
            [0f, 1f, 0f, 0f],   // id=1: orthogonal
            [0.9f, 0.1f, 0f, 0f], // id=2: close
            [0f, 0f, 1f, 0f],   // id=3: orthogonal
            [-1f, 0f, 0f, 0f],  // id=4: opposite
        ];
        return new FakeVectorTable(vectors);
    }
}
