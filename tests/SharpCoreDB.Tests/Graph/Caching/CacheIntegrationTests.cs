// <copyright file="CacheIntegrationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph.Caching;

using SharpCoreDB.Graph;
using SharpCoreDB.Graph.Caching;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Integration tests for cache with GraphTraversalEngine.
/// ✅ GraphRAG Phase 5.3: Validates end-to-end caching behavior.
/// </summary>
public class CacheIntegrationTests
{
    [Fact]
    public void GraphTraversalEngine_WithCache_CachesQueryPlans()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var options = new GraphSearchOptions { PlanCache = cache };
        var engine = new GraphTraversalEngine(options);

        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
        };
        var table = new FakeGraphTable(rows, "next");

        // Act - First query (cache miss)
        var result1 = engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);

        // Assert - Plan should be cached
        Assert.Equal(1, cache.Count);
        Assert.Equal(0, cache.Hits); // First query is a miss
        Assert.Equal(1, cache.Misses);

        // Act - Second query (cache hit)
        var result2 = engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);

        // Assert - Cache hit
        Assert.Equal(1, cache.Count);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void GraphTraversalEngine_WithoutCache_DoesNotCache()
    {
        // Arrange
        var options = new GraphSearchOptions(); // No cache
        var engine = new GraphTraversalEngine(options);

        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
        };
        var table = new FakeGraphTable(rows, "next");

        // Act
        var result = engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);

        // Assert - Should work without cache
        Assert.NotNull(result);
        Assert.Contains(1L, result);
    }

    [Fact]
    public void GraphTraversalEngine_DifferentStrategies_CachedSeparately()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var options = new GraphSearchOptions { PlanCache = cache };
        var engine = new GraphTraversalEngine(options);

        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
        };
        var table = new FakeGraphTable(rows, "next");

        // Act - BFS
        engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);

        // Act - DFS (different strategy)
        engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Dfs);

        // Assert - Two separate cache entries
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void GraphTraversalEngine_DifferentDepths_CachedSeparately()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var options = new GraphSearchOptions { PlanCache = cache };
        var engine = new GraphTraversalEngine(options);

        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
        };
        var table = new FakeGraphTable(rows, "next");

        // Act - Depth 2
        engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);

        // Act - Depth 3
        engine.Traverse(table, 1, "next", 3, GraphTraversalStrategy.Bfs);

        // Assert - Two separate cache entries
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void GraphTraversalEngine_DifferentTables_CachedSeparately()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var options = new GraphSearchOptions { PlanCache = cache };
        var engine = new GraphTraversalEngine(options);

        var rows1 = new List<Dictionary<string, object>> { new() { ["id"] = 1L, ["next"] = 2L } };
        var table1 = new FakeGraphTable(rows1, "next", "table1");

        var rows2 = new List<Dictionary<string, object>> { new() { ["id"] = 1L, ["next"] = 2L } };
        var table2 = new FakeGraphTable(rows2, "next", "table2");

        // Act
        engine.Traverse(table1, 1, "next", 2, GraphTraversalStrategy.Bfs);
        engine.Traverse(table2, 1, "next", 2, GraphTraversalStrategy.Bfs);

        // Assert - Two separate cache entries
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void GraphTraversalEngine_CacheSizeLimit_EvictsOldEntries()
    {
        // Arrange
        var cache = new TraversalPlanCache(maxSize: 2); // Only 2 entries
        var options = new GraphSearchOptions { PlanCache = cache };
        var engine = new GraphTraversalEngine(options);

        var rows = new List<Dictionary<string, object>> { new() { ["id"] = 1L, ["next"] = 2L } };
        var table = new FakeGraphTable(rows, "next");

        // Act - Add 3 entries (should trigger eviction)
        engine.Traverse(table, 1, "next", 1, GraphTraversalStrategy.Bfs);
        System.Threading.Thread.Sleep(10); // Ensure different LastAccessedAt
        engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);
        System.Threading.Thread.Sleep(10);
        engine.Traverse(table, 1, "next", 3, GraphTraversalStrategy.Bfs);

        // Assert - Only 2 entries remain (LRU evicted)
        Assert.Equal(2, cache.Count);
        Assert.Equal(1, cache.Evictions);
    }

    [Fact]
    public void GraphTraversalEngine_RepeatedQueries_IncreasesHitCount()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var options = new GraphSearchOptions { PlanCache = cache };
        var engine = new GraphTraversalEngine(options);

        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
        };
        var table = new FakeGraphTable(rows, "next");

        // Act - Repeat same query 5 times
        for (int i = 0; i < 5; i++)
        {
            engine.Traverse(table, 1, "next", 2, GraphTraversalStrategy.Bfs);
        }

        // Assert
        Assert.Equal(1, cache.Count);
        Assert.Equal(4, cache.Hits); // 1 miss + 4 hits = 5 total
        Assert.Equal(1, cache.Misses);
        Assert.Equal(0.8, cache.HitRatio, precision: 2); // 4/5 = 0.8
    }
}
