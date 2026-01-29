// <copyright file="RangeQueryOptimizationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;
using System;
using System.Linq;
using Xunit;

/// <summary>
/// Tests for Phase 4: Range Query Optimization
/// Validates that range queries use B-tree indexes for O(log n + k) performance.
/// </summary>
public class RangeQueryOptimizationTests
{
    [Fact]
    public void RangeQueryOptimizer_DetectsRangeQuery_BETWEEN()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());

        // Act
        var isBetween = optimizer.IsRangeQuery("age BETWEEN 18 AND 65");

        // Assert
        Assert.True(isBetween);
    }

    [Fact]
    public void RangeQueryOptimizer_DetectsRangeQuery_GreaterThan()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());

        // Act
        var isRange = optimizer.IsRangeQuery("price > 100");

        // Assert
        Assert.True(isRange);
    }

    [Fact]
    public void RangeQueryOptimizer_DetectsRangeQuery_LessThan()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());

        // Act
        var isRange = optimizer.IsRangeQuery("quantity < 50");

        // Assert
        Assert.True(isRange);
    }

    [Fact]
    public void RangeQueryOptimizer_ExtractsBETWEENBounds_Correctly()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());
        var whereExpr = "order_date BETWEEN '2025-01-01' AND '2025-12-31'";

        // Act
        var success = optimizer.TryExtractBetweenBounds(
            whereExpr,
            out var column,
            out var start,
            out var end);

        // Assert
        Assert.True(success);
        Assert.Equal("order_date", column);
        Assert.Equal("2025-01-01", start);
        Assert.Equal("2025-12-31", end);
    }

    [Fact]
    public void RangeQueryOptimizer_ExtractsBETWEENBounds_Integers()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());
        var whereExpr = "age BETWEEN 18 AND 65";

        // Act
        var success = optimizer.TryExtractBetweenBounds(
            whereExpr,
            out var column,
            out var start,
            out var end);

        // Assert
        Assert.True(success);
        Assert.Equal("age", column);
        Assert.Equal("18", start);
        Assert.Equal("65", end);
    }

    [Fact]
    public void RangeQueryOptimizer_ExtractsComparisonBounds_GreaterThan()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());
        var whereExpr = "price > 100.00";

        // Act
        var success = optimizer.TryExtractComparisonBounds(
            whereExpr,
            out var column,
            out var op,
            out var bound);

        // Assert
        Assert.True(success);
        Assert.Equal("price", column);
        Assert.Equal(">", op);
        Assert.Equal("100.00", bound);
    }

    [Fact]
    public void RangeQueryOptimizer_ExtractsComparisonBounds_GreaterThanOrEqual()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());
        var whereExpr = "score >= 80";

        // Act
        var success = optimizer.TryExtractComparisonBounds(
            whereExpr,
            out var column,
            out var op,
            out var bound);

        // Assert
        Assert.True(success);
        Assert.Equal("score", column);
        Assert.Equal(">=", op);
        Assert.Equal("80", bound);
    }

    [Fact]
    public void RangeQueryOptimizer_ExtractsComparisonBounds_LessThanOrEqual()
    {
        // Arrange
        var optimizer = new RangeQueryOptimizer(new IndexManager());
        var whereExpr = "inventory <= 100";

        // Act
        var success = optimizer.TryExtractComparisonBounds(
            whereExpr,
            out var column,
            out var op,
            out var bound);

        // Assert
        Assert.True(success);
        Assert.Equal("inventory", column);
        Assert.Equal("<=", op);
        Assert.Equal("100", bound);
    }

    /// <summary>
    /// ✅ Phase 4: Integration test for B-tree range query optimization.
    /// Validates that B-tree index FindRange returns correct results.
    /// </summary>
    [Fact]
    public void BTreeIndex_RangeQuery_IntegerRange()
    {
        // Arrange
        var index = new BTreeIndex<int>("product_id");
        
        // Add sample products with IDs
        for (int i = 1; i <= 100; i++)
        {
            index.Add(i, (long)(i * 10)); // Position = id * 10
        }

        // Act - Find products with IDs between 20 and 80
        var results = index.FindRange(20, 80).ToList();

        // Assert
        Assert.Equal(61, results.Count); // 20 to 80 inclusive
        Assert.Equal(200, results.First()); // ID 20 → position 200
        Assert.Equal(800, results.Last()); // ID 80 → position 800
    }

    /// <summary>
    /// ✅ Phase 4: B-tree range query with strings (common for LIKE patterns).
    /// </summary>
    [Fact]
    public void BTreeIndex_RangeQuery_StringRange()
    {
        // Arrange
        var index = new BTreeIndex<string>("product_name");
        
        var products = new[]
        {
            "Apple", "Apricot", "Banana", "Blueberry",
            "Cherry", "Cranberry", "Date", "Dragon Fruit"
        };

        for (int i = 0; i < products.Length; i++)
        {
            index.Add(products[i], i);
        }

        // Act - Find products starting with 'B' through 'C'
        var results = index.FindRange("B", "D").ToList();

        // Assert - Should include Banana, Blueberry, Cherry, Cranberry
        Assert.Equal(4, results.Count);
        Assert.Contains(2, results); // Banana
        Assert.Contains(3, results); // Blueberry
        Assert.Contains(4, results); // Cherry
        Assert.Contains(5, results); // Cranberry
    }

    /// <summary>
    /// ✅ Phase 4: B-tree range query with DateTime (temporal ranges).
    /// </summary>
    [Fact]
    public void BTreeIndex_RangeQuery_DateRange()
    {
        // Arrange
        var index = new BTreeIndex<DateTime>("created_date");
        
        var dates = new[]
        {
            new DateTime(2025, 1, 15),
            new DateTime(2025, 3, 20),
            new DateTime(2025, 6, 10),
            new DateTime(2025, 9, 5),
            new DateTime(2025, 12, 25)
        };

        for (int i = 0; i < dates.Length; i++)
        {
            index.Add(dates[i], i);
        }

        // Act - Find records from March to September
        var results = index.FindRange(
            new DateTime(2025, 3, 1),
            new DateTime(2025, 9, 30)
        ).ToList();

        // Assert - Should include March 20, June 10, September 5
        Assert.Equal(3, results.Count);
        Assert.Contains(1, results); // March 20
        Assert.Contains(2, results); // June 10
        Assert.Contains(3, results); // September 5
    }

    /// <summary>
    /// ✅ Phase 4: B-tree handles empty range correctly (no matches).
    /// </summary>
    [Fact]
    public void BTreeIndex_RangeQuery_EmptyRange()
    {
        // Arrange
        var index = new BTreeIndex<int>("age");
        
        index.Add(20, 1);
        index.Add(30, 2);
        index.Add(40, 3);

        // Act - Search for range with no matching values
        var results = index.FindRange(21, 29).ToList();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// ✅ Phase 4: B-tree handles single-element range correctly.
    /// </summary>
    [Fact]
    public void BTreeIndex_RangeQuery_SingleElement()
    {
        // Arrange
        var index = new BTreeIndex<int>("priority");
        
        index.Add(1, 10);
        index.Add(2, 20);
        index.Add(3, 30);

        // Act - Search for exact value as range (start == end)
        var results = index.FindRange(2, 2).ToList();

        // Assert
        Assert.Single(results);
        Assert.Contains(20, results);
    }

    /// <summary>
    /// ✅ Phase 4: B-tree handles duplicate keys correctly.
    /// </summary>
    [Fact]
    public void BTreeIndex_RangeQuery_WithDuplicates()
    {
        // Arrange
        var index = new BTreeIndex<int>("rating");
        
        // Add multiple records with same rating
        index.Add(4, 1); // First 4-star review
        index.Add(4, 2); // Second 4-star review
        index.Add(5, 3); // First 5-star review
        index.Add(5, 4); // Second 5-star review

        // Act - Find all 4-5 star reviews
        var results = index.FindRange(4, 5).ToList();

        // Assert
        Assert.Equal(4, results.Count);
        Assert.Contains(1, results);
        Assert.Contains(2, results);
        Assert.Contains(3, results);
        Assert.Contains(4, results);
    }
}
