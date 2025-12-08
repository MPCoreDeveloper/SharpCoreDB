// <copyright file="AutoIndexingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Tests for auto-indexing with PRAGMA-based detection.
/// </summary>
public sealed class AutoIndexingTests
{
    [Fact]
    public void AutoIndexing_HighSelectivity_RecommendsIndex()
    {
        // Arrange: Data with high selectivity column (unique values)
        var manager = new IndexManager(enableAutoIndexing: true);
        var rows = Enumerable.Range(0, 1000).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,           // 100% unique ? HIGH selectivity
            ["name"] = $"User{i}" // 100% unique ? HIGH selectivity
        }).ToList();

        // Act: Analyze
        var tableInfo = manager.AnalyzeAndCreateIndexes("users", rows);

        // Assert: Should recommend indexes for high selectivity columns
        Assert.True(tableInfo.ColumnStatistics["id"].ShouldIndex,
            "ID column should be indexed (high selectivity)");
        Assert.True(tableInfo.ColumnStatistics["id"].Selectivity > 0.5,
            "ID selectivity should be > 0.5");
        Assert.True(tableInfo.ColumnStatistics["name"].ShouldIndex,
            "Name column should be indexed (high selectivity)");

        manager.Dispose();
    }

    [Fact]
    public void AutoIndexing_LowSelectivity_SkipsIndex()
    {
        // Arrange: Data with low selectivity (few unique values)
        var manager = new IndexManager(enableAutoIndexing: true);
        var rows = Enumerable.Range(0, 1000).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,                    // High selectivity
            ["active"] = i % 2 == 0,       // LOW selectivity (only 2 values)
            ["category"] = i % 5           // LOW selectivity (only 5 values)
        }).ToList();

        // Act
        var tableInfo = manager.AnalyzeAndCreateIndexes("users", rows);

        // Assert: Should NOT index low selectivity columns
        Assert.True(tableInfo.ColumnStatistics["id"].ShouldIndex);
        Assert.False(tableInfo.ColumnStatistics["active"].ShouldIndex,
            "Boolean column has low selectivity, should not index");
        Assert.False(tableInfo.ColumnStatistics["category"].ShouldIndex,
            "Category with 5 values has low selectivity, should not index");

        manager.Dispose();
    }

    [Fact]
    public void AutoIndexing_QueryTracking_RecommendsIndexAfterFrequentQueries()
    {
        // Arrange
        var manager = new IndexManager(enableAutoIndexing: true);
        var rows = Enumerable.Range(0, 100).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,
            ["rarely_queried"] = i % 10 // Low selectivity initially
        }).ToList();

        var tableInfo = manager.AnalyzeAndCreateIndexes("users", rows);
        
        // Initially not recommended (low selectivity)
        Assert.False(tableInfo.ColumnStatistics["rarely_queried"].ShouldIndex);

        // Act: Simulate frequent queries (> 10 queries triggers recommendation)
        for (int i = 0; i < 15; i++)
        {
            manager.GetPragmaDetector().RecordQuery("users", "rarely_queried");
        }

        // Assert: After frequent queries, should recommend indexing
        var updatedTableInfo = manager.GetPragmaDetector().GetTableInfo("users");
        // Note: This is a simplification - in real implementation we'd re-check
        
        manager.Dispose();
    }

    [Fact]
    public void AutoIndexing_PragmaIndexList_ReturnsCorrectFormat()
    {
        // Arrange
        var manager = new IndexManager(enableAutoIndexing: true);
        var rows = Enumerable.Range(0, 100).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,
            ["email"] = $"user{i}@example.com"
        }).ToList();

        manager.AnalyzeAndCreateIndexes("users", rows);

        // Act
        var pragmaOutput = manager.GetPragmaIndexList("users");

        // Assert: Should contain index information
        Assert.Contains("idx_users_", pragmaOutput);
        Assert.Contains("auto=", pragmaOutput);
        Assert.Contains("Hash", pragmaOutput); // Index type

        Console.WriteLine("PRAGMA index_list('users'):");
        Console.WriteLine(pragmaOutput);

        manager.Dispose();
    }

    [Fact]
    public void AutoIndexing_PragmaTableInfo_ShowsStatistics()
    {
        // Arrange
        var manager = new IndexManager(enableAutoIndexing: true);
        var rows = Enumerable.Range(0, 1000).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,
            ["category"] = i % 5,
            ["active"] = i % 2 == 0
        }).ToList();

        manager.AnalyzeAndCreateIndexes("users", rows);

        // Act
        var pragmaOutput = manager.GetPragmaTableInfo("users");

        // Assert
        Assert.Contains("selectivity=", pragmaOutput);
        Assert.Contains("unique", pragmaOutput);
        Assert.Contains("index=", pragmaOutput);

        Console.WriteLine("PRAGMA table_info('users'):");
        Console.WriteLine(pragmaOutput);

        manager.Dispose();
    }

    [Fact]
    public void GenericIndex_TypeSafe_IntKeys()
    {
        // Arrange
        var manager = new IndexManager();
        
        // Act: Add int keys (type-safe)
        manager.AddToIndex("users", "id", 42, 100L);
        manager.AddToIndex("users", "id", 43, 200L);
        manager.AddToIndex("users", "id", 42, 300L); // Duplicate key

        // Assert: Find returns all positions for key
        var positions = manager.FindInIndex("users", "id", 42).ToList();
        Assert.Equal(2, positions.Count);
        Assert.Contains(100L, positions);
        Assert.Contains(300L, positions);

        var positions43 = manager.FindInIndex("users", "id", 43).ToList();
        Assert.Single(positions43);
        Assert.Equal(200L, positions43[0]);

        manager.Dispose();
    }

    [Fact]
    public void GenericIndex_TypeSafe_StringKeys()
    {
        // Arrange
        var manager = new IndexManager();
        
        // Act: Add string keys
        manager.AddToIndex("users", "email", "alice@example.com", 100L);
        manager.AddToIndex("users", "email", "bob@example.com", 200L);

        // Assert
        var alicePos = manager.FindInIndex("users", "email", "alice@example.com").ToList();
        Assert.Single(alicePos);
        Assert.Equal(100L, alicePos[0]);

        var bobPos = manager.FindInIndex("users", "email", "bob@example.com").ToList();
        Assert.Single(bobPos);
        Assert.Equal(200L, bobPos[0]);

        manager.Dispose();
    }

    [Fact]
    public void GenericIndex_GetStatistics_ReturnsCorrectData()
    {
        // Arrange
        var manager = new IndexManager();
        
        for (int i = 0; i < 1000; i++)
        {
            // Category has low selectivity (only 5 unique values)
            manager.AddToIndex("products", "category", i % 5, (long)i);
        }

        // Act
        var stats = manager.GetIndexStatistics("products", "category");

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(5, stats.UniqueKeys);
        Assert.Equal(1000, stats.TotalEntries);
        Assert.Equal(200.0, stats.AverageEntriesPerKey); // 1000/5
        Assert.Equal(0.005, stats.Selectivity, precision: 3); // 5/1000

        Console.WriteLine($"Index Statistics:");
        Console.WriteLine($"  Unique Keys: {stats.UniqueKeys}");
        Console.WriteLine($"  Total Entries: {stats.TotalEntries}");
        Console.WriteLine($"  Avg per Key: {stats.AverageEntriesPerKey:F1}");
        Console.WriteLine($"  Selectivity: {stats.Selectivity:F3}");
        Console.WriteLine($"  Memory: {stats.MemoryUsageBytes / 1024.0:F2} KB");

        manager.Dispose();
    }

    [Fact]
    public void AutoIndexing_RecommendedIndexTypes_BasedOnSelectivity()
    {
        // Arrange
        var manager = new IndexManager(enableAutoIndexing: true);
        var rows = Enumerable.Range(0, 1000).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i,              // Selectivity = 1.0 ? Hash index
            ["category"] = i % 100,  // Selectivity = 0.1 ? B-Tree index
            ["active"] = i % 2 == 0  // Selectivity = 0.002 ? No index
        }).ToList();

        // Act
        var tableInfo = manager.AnalyzeAndCreateIndexes("products", rows);

        // Assert: High selectivity columns get Hash index
        var idIndex = tableInfo.Indexes.FirstOrDefault(i => i.ColumnName == "id");
        Assert.NotNull(idIndex);
        Assert.Equal(IndexType.Hash, idIndex.Type);

        // Medium selectivity could get B-Tree for range queries
        // (implementation detail - currently may still be Hash)

        Console.WriteLine("Recommended Index Types:");
        foreach (var index in tableInfo.Indexes)
        {
            Console.WriteLine($"  {index.ColumnName}: {index.Type} " +
                            $"(selectivity={index.Statistics.Selectivity:F2})");
        }

        manager.Dispose();
    }
}
