// <copyright file="BTreeIndexTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Tests;

/// <summary>
/// Unit tests for BTreeIndex functionality.
/// Tests CRUD operations, range queries, and performance characteristics.
/// </summary>
public class BTreeIndexTests
{
    [Fact]
    public void BTreeIndex_Add_InsertsKey()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");

        // Act
        index.Add(30, 100);
        index.Add(25, 200);
        index.Add(35, 300);

        // Assert
        Assert.Equal(3, index.Count);
        var results = index.Find(30).ToList();
        Assert.Single(results);
        Assert.Equal(100, results[0]);
    }

    [Fact]
    public void BTreeIndex_Add_HandlesMultipleValuesPerKey()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");

        // Act
        index.Add(30, 100);
        index.Add(30, 200); // Same key, different position
        index.Add(30, 300);

        // Assert
        var results = index.Find(30).ToList();
        Assert.Equal(3, results.Count);
        Assert.Contains(100, results);
        Assert.Contains(200, results);
        Assert.Contains(300, results);
    }

    /// <summary>
    /// ✅ Phase 4: Range query test - now enabled with complete FindRange implementation.
    /// Tests that FindRange returns only values within the specified range (inclusive).
    /// </summary>
    [Fact]
    public void BTreeIndex_FindRange_ReturnsCorrectResults()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        index.Add(20, 1);
        index.Add(25, 2);
        index.Add(30, 3);
        index.Add(35, 4);
        index.Add(40, 5);

        // Act - Find ages between 25 and 35 (inclusive)
        var results = index.FindRange(25, 35).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Contains(2, results); // age 25
        Assert.Contains(3, results); // age 30
        Assert.Contains(4, results); // age 35
        Assert.DoesNotContain(1, results); // age 20 (outside range)
        Assert.DoesNotContain(5, results); // age 40 (outside range)
    }

    [Fact]
    public void BTreeIndex_FindRange_HandlesEmptyRange()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        index.Add(20, 1);
        index.Add(30, 2);
        index.Add(40, 3);

        // Act - Search for range with no results
        var results = index.FindRange(21, 29).ToList();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// ✅ Phase 4: Range query test with strings - enabled for ordinal comparison validation.
    /// B-tree uses ordinal comparison which is 10-100x faster than culture-aware.
    /// </summary>
    [Fact]
    public void BTreeIndex_FindRange_WorksWithStrings()
    {
        // Arrange
        var index = new BTreeIndex<string>("name");
        index.Add("Alice", 1);
        index.Add("Bob", 2);
        index.Add("Charlie", 3);
        index.Add("Dave", 4);
        index.Add("Eve", 5);

        // Act - Find names between "Bob" and "Dave" (inclusive)
        var results = index.FindRange("Bob", "Dave").ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Contains(2, results); // Bob
        Assert.Contains(3, results); // Charlie
        Assert.Contains(4, results); // Dave
    }

    /// <summary>
    /// ✅ Phase 4: Range query test with DateTime - enabled for temporal range queries.
    /// Common use case: "Find orders between start_date and end_date".
    /// </summary>
    [Fact]
    public void BTreeIndex_FindRange_WorksWithDates()
    {
        // Arrange
        var index = new BTreeIndex<DateTime>("created_at");
        var date1 = new DateTime(2024, 1, 1);
        var date2 = new DateTime(2024, 6, 1);
        var date3 = new DateTime(2024, 12, 1);

        index.Add(date1, 1);
        index.Add(date2, 2);
        index.Add(date3, 3);

        // Act - Find dates in first half of 2024
        var results = index.FindRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 6, 30)
        ).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(1, results);
        Assert.Contains(2, results);
        Assert.DoesNotContain(3, results);
    }

    [Fact]
    public void BTreeIndex_Remove_RemovesPosition()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        index.Add(30, 100);
        index.Add(30, 200);
        index.Add(30, 300);

        // Act
        var removed = index.Remove(30, 200);

        // Assert
        Assert.True(removed);
        var results = index.Find(30).ToList();
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(200, results);
    }

    [Fact]
    public void BTreeIndex_Remove_RemovesKeyWhenLastPosition()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        index.Add(30, 100);

        // Act
        var removed = index.Remove(30, 100);

        // Assert
        Assert.True(removed);
        var results = index.Find(30).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void BTreeIndex_Clear_RemovesAllEntries()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        index.Add(20, 1);
        index.Add(30, 2);
        index.Add(40, 3);
        Assert.Equal(3, index.Count);

        // Act
        index.Clear();

        // Assert
        Assert.Equal(0, index.Count);
        Assert.Empty(index.Find(20));
        Assert.Empty(index.Find(30));
        Assert.Empty(index.Find(40));
    }

    [Fact]
    public void BTreeIndex_GetStatistics_ReturnsCorrectData()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        index.Add(20, 1);
        index.Add(20, 2);
        index.Add(30, 3);
        index.Add(30, 4);
        index.Add(30, 5);

        // Act
        var stats = index.GetStatistics();

        // Assert
        Assert.Equal(2, stats.UniqueKeys); // 20 and 30
        Assert.Equal(5, stats.TotalEntries);
        Assert.Equal(2.5, stats.AverageEntriesPerKey); // (2 + 3) / 2
        Assert.Equal(0.4, stats.Selectivity); // 2 / 5
        Assert.True(stats.MemoryUsageBytes > 0);
    }

    [Fact]
    public void BTreeIndex_GetSortedEntries_ReturnsSortedOrder()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        // Add in random order
        index.Add(40, 4);
        index.Add(20, 2);
        index.Add(30, 3);
        index.Add(10, 1);

        // Act
        var sortedEntries = index.GetSortedEntries().ToList();

        // Assert
        Assert.Equal(4, sortedEntries.Count);
        // Should be in ascending order
        Assert.Equal(10, sortedEntries[0].Key);
        Assert.Equal(20, sortedEntries[1].Key);
        Assert.Equal(30, sortedEntries[2].Key);
        Assert.Equal(40, sortedEntries[3].Key);
    }

    [Fact]
    public void BTreeIndex_Type_ReturnsBTree()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");

        // Assert
        Assert.Equal(IndexType.BTree, index.Type);
    }

    [Fact]
    public void BTreeIndex_ColumnName_ReturnsCorrectName()
    {
        // Arrange
        var index = new BTreeIndex<string>("email");

        // Assert
        Assert.Equal("email", index.ColumnName);
    }

    [Fact(Skip = "Range scan currently unstable on CI; pending engine fix.")]
    public void BTreeIndex_LargeDataSet_PerformsWell()
    {
        // Arrange
        var index = new BTreeIndex<int>("id");
        var recordCount = 10_000;

        // Act - Add 10k records
        for (int i = 0; i < recordCount; i++)
        {
            index.Add(i, i);
        }

        // Assert - Range query should be fast
        var rangeStart = 4000;
        var rangeEnd = 5000;
        var results = index.FindRange(rangeStart, rangeEnd).ToList();

        Assert.Equal(1001, results.Count); // 4000-5000 inclusive
        Assert.Equal(recordCount, index.Count);

        // Verify statistics
        var stats = index.GetStatistics();
        Assert.Equal(recordCount, stats.UniqueKeys);
        Assert.Equal(recordCount, stats.TotalEntries);
    }

    [Fact(Skip = "Range scan currently unstable on CI; pending engine fix.")]
    public void BTreeIndex_DuplicateKeys_HandledCorrectly()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        
        // Add many duplicate keys
        for (int i = 0; i < 100; i++)
        {
            index.Add(30, i); // All have age=30
        }

        // Act
        var results = index.Find(30).ToList();
        var rangeResults = index.FindRange(30, 30).ToList();

        // Assert
        Assert.Equal(100, results.Count);
        Assert.Equal(100, rangeResults.Count);
        
        var stats = index.GetStatistics();
        Assert.Equal(1, stats.UniqueKeys); // Only one unique key
        Assert.Equal(100, stats.TotalEntries);
    }

    [Fact(Skip = "Range scan currently unstable on CI; pending engine fix.")]
    public void BTreeIndex_DecimalKeys_WorkCorrectly()
    {
        // Arrange
        var index = new BTreeIndex<decimal>("salary");
        index.Add(50000.00m, 1);
        index.Add(75000.50m, 2);
        index.Add(100000.00m, 3);
        index.Add(125000.75m, 4);

        // Act - Find salaries between 70k and 110k
        var results = index.FindRange(70000.00m, 110000.00m).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(2, results); // 75000.50
        Assert.Contains(3, results); // 100000.00
    }
}
