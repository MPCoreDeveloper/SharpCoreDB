// <copyright file="TraversalPlanCacheTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph.Caching;

using SharpCoreDB.Graph.Caching;
using SharpCoreDB.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for TraversalPlanCache.
/// ✅ GraphRAG Phase 5.2: Validates caching, eviction, and TTL behavior.
/// </summary>
public class TraversalPlanCacheTests
{
    [Fact]
    public void TraversalPlanCache_InitialState_IsEmpty()
    {
        // Arrange & Act
        var cache = new TraversalPlanCache();

        // Assert
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Hits);
        Assert.Equal(0, cache.Misses);
        Assert.Equal(0.0, cache.HitRatio);
    }

    [Fact]
    public void TraversalPlanCache_SetAndGet_StoresPlan()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now);

        // Act
        cache.Set(plan);
        var found = cache.TryGet(key, out var retrieved);

        // Assert
        Assert.True(found);
        Assert.NotNull(retrieved);
        Assert.Equal(GraphTraversalStrategy.Bfs, retrieved.Strategy);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(0, cache.Misses);
    }

    [Fact]
    public void TraversalPlanCache_GetNonExistent_ReturnsFalse()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);

        // Act
        var found = cache.TryGet(key, out var plan);

        // Assert
        Assert.False(found);
        Assert.Null(plan);
        Assert.Equal(0, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void TraversalPlanCache_MultipleAccesses_UpdatesAccessCount()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now);
        cache.Set(plan);

        // Act
        cache.TryGet(key, out var retrieved1);
        cache.TryGet(key, out var retrieved2);
        cache.TryGet(key, out var retrieved3);

        // Assert
        Assert.NotNull(retrieved3);
        Assert.Equal(3, retrieved3.AccessCount);
        Assert.Equal(3, cache.Hits);
    }

    [Fact]
    public void TraversalPlanCache_StalePlan_IsEvicted()
    {
        // Arrange
        var cache = new TraversalPlanCache(maxSize: 1000, ttlSeconds: 0.1); // 100ms TTL
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now);
        cache.Set(plan);

        // Act
        Thread.Sleep(150); // Wait for TTL to expire
        var found = cache.TryGet(key, out var retrieved);

        // Assert
        Assert.False(found);
        Assert.Null(retrieved);
        Assert.Equal(0, cache.Count); // Plan should be removed
    }

    [Fact]
    public void TraversalPlanCache_MaxSizeExceeded_EvictsLRU()
    {
        // Arrange
        var cache = new TraversalPlanCache(maxSize: 3);

        var key1 = new TraversalPlanCacheKey("table1", "col1", 5, GraphTraversalStrategy.Bfs);
        var key2 = new TraversalPlanCacheKey("table2", "col2", 5, GraphTraversalStrategy.Dfs);
        var key3 = new TraversalPlanCacheKey("table3", "col3", 5, GraphTraversalStrategy.Bidirectional);
        var key4 = new TraversalPlanCacheKey("table4", "col4", 5, GraphTraversalStrategy.Dijkstra);

        var plan1 = new CachedTraversalPlan(key1, GraphTraversalStrategy.Bfs, 100, DateTime.Now);
        var plan2 = new CachedTraversalPlan(key2, GraphTraversalStrategy.Dfs, 100, DateTime.Now);
        var plan3 = new CachedTraversalPlan(key3, GraphTraversalStrategy.Bidirectional, 100, DateTime.Now);
        var plan4 = new CachedTraversalPlan(key4, GraphTraversalStrategy.Dijkstra, 100, DateTime.Now);

        // Act
        cache.Set(plan1);
        Thread.Sleep(10);
        cache.Set(plan2);
        Thread.Sleep(10);
        cache.Set(plan3);
        Thread.Sleep(10);

        // Access plan2 and plan3 to update their LastAccessedAt
        cache.TryGet(key2, out _);
        cache.TryGet(key3, out _);

        // Now add plan4, which should evict plan1 (LRU)
        cache.Set(plan4);

        // Assert
        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGet(key1, out _)); // plan1 evicted
        Assert.True(cache.TryGet(key2, out _));  // plan2 still cached
        Assert.True(cache.TryGet(key3, out _));  // plan3 still cached
        Assert.True(cache.TryGet(key4, out _));  // plan4 added
        Assert.Equal(1, cache.Evictions);
    }

    [Fact]
    public void TraversalPlanCache_Clear_RemovesAllEntries()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var key1 = new TraversalPlanCacheKey("table1", "col1", 5, GraphTraversalStrategy.Bfs);
        var key2 = new TraversalPlanCacheKey("table2", "col2", 5, GraphTraversalStrategy.Dfs);

        cache.Set(new CachedTraversalPlan(key1, GraphTraversalStrategy.Bfs, 100, DateTime.Now));
        cache.Set(new CachedTraversalPlan(key2, GraphTraversalStrategy.Dfs, 100, DateTime.Now));

        // Act
        cache.Clear();

        // Assert
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Hits);
        Assert.Equal(0, cache.Misses);
    }

    [Fact]
    public void TraversalPlanCache_Remove_RemovesSpecificEntry()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now);
        cache.Set(plan);

        // Act
        var removed = cache.Remove(key);

        // Assert
        Assert.True(removed);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(key, out _));
    }

    [Fact]
    public void TraversalPlanCache_PurgeStaleEntries_RemovesExpiredPlans()
    {
        // Arrange
        var cache = new TraversalPlanCache(ttlSeconds: 0.1); // 100ms TTL
        var key1 = new TraversalPlanCacheKey("table1", "col1", 5, GraphTraversalStrategy.Bfs);
        var key2 = new TraversalPlanCacheKey("table2", "col2", 5, GraphTraversalStrategy.Dfs);

        cache.Set(new CachedTraversalPlan(key1, GraphTraversalStrategy.Bfs, 100, DateTime.Now));
        Thread.Sleep(150); // key1 becomes stale
        cache.Set(new CachedTraversalPlan(key2, GraphTraversalStrategy.Dfs, 100, DateTime.Now)); // key2 fresh

        // Act
        var purged = cache.PurgeStaleEntries();

        // Assert
        Assert.Equal(1, purged);
        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet(key1, out _)); // Stale, should be gone
        Assert.True(cache.TryGet(key2, out _));  // Fresh, should remain
    }

    [Fact]
    public void TraversalPlanCache_GetStatistics_ReturnsCorrectValues()
    {
        // Arrange
        var cache = new TraversalPlanCache(maxSize: 1000, ttlSeconds: 3600);
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now);

        cache.Set(plan);
        cache.TryGet(key, out _); // Hit
        cache.TryGet(new TraversalPlanCacheKey("missing", "col", 1, GraphTraversalStrategy.Bfs), out _); // Miss

        // Act
        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(1, stats.Count);
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(0.5, stats.HitRatio, precision: 2);
        Assert.Equal(1000, stats.MaxSize);
        Assert.Equal(3600, stats.TtlSeconds);
    }

    [Fact]
    public void TraversalPlanCache_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var cache = new TraversalPlanCache();
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now);

        cache.Set(plan);

        // Act - Concurrent reads
        Parallel.For(0, 100, _ =>
        {
            cache.TryGet(key, out var _);
        });

        // Assert
        Assert.Equal(1, cache.Count);
        Assert.Equal(100, cache.Hits);
    }

    [Fact]
    public void TraversalPlanCacheKey_EqualityComparison_WorksCorrectly()
    {
        // Arrange
        var key1 = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var key2 = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var key3 = new TraversalPlanCacheKey("users", "managerId", 10, GraphTraversalStrategy.Bfs);

        // Assert
        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);
    }

    [Fact]
    public void TraversalPlanCacheKey_ToString_FormatsCorrectly()
    {
        // Arrange
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);

        // Act
        var str = key.ToString();

        // Assert
        Assert.Equal("users|managerId|5|Bfs", str);
    }

    [Fact]
    public void TraversalPlanCacheKey_WithAStarHeuristic_IncludesHeuristic()
    {
        // Arrange
        var key = new TraversalPlanCacheKey(
            "users",
            "managerId",
            5,
            GraphTraversalStrategy.AStar,
            AStarHeuristic.Depth);

        // Act
        var str = key.ToString();

        // Assert
        Assert.Contains("AStar", str);
        Assert.Contains("H:Depth", str);
    }

    [Fact]
    public void CachedTraversalPlan_RecordAccess_UpdatesMetadata()
    {
        // Arrange
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now);
        var initialAccess = plan.LastAccessedAt;

        // Act
        Thread.Sleep(50);
        plan.RecordAccess();

        // Assert
        Assert.Equal(1, plan.AccessCount);
        Assert.True(plan.LastAccessedAt > initialAccess);
    }

    [Fact]
    public void CachedTraversalPlan_IsStale_DetectsExpiredPlans()
    {
        // Arrange
        var key = new TraversalPlanCacheKey("users", "managerId", 5, GraphTraversalStrategy.Bfs);
        var plan = new CachedTraversalPlan(key, GraphTraversalStrategy.Bfs, 100, DateTime.Now.AddSeconds(-10));

        // Act & Assert
        Assert.False(plan.IsStale(20)); // TTL 20s, plan is 10s old
        Assert.True(plan.IsStale(5));   // TTL 5s, plan is 10s old (stale)
    }
}
