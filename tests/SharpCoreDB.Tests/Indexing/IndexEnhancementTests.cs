// <copyright file="IndexEnhancementTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Indexing;

using System;
using System.Collections.Generic;
using System.Linq;
using SharpCoreDB.Indexing;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Tests for Phase 9: Index Enhancements.
/// ✅ SCDB Phase 9: Verifies index hints, partial indexes, expression indexes, adaptive indexing, and optimizer.
/// </summary>
public sealed class IndexEnhancementTests
{
    private readonly ITestOutputHelper _output;

    public IndexEnhancementTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // IndexHints Tests
    // ========================================

    [Fact]
    public void IndexHint_Parse_ParsesForceHint()
    {
        // Arrange
        var hintString = "/*+ INDEX(users idx_email) */";

        // Act
        var hint = IndexHint.Parse(hintString);

        // Assert
        Assert.NotNull(hint);
        Assert.Equal("users", hint.TableName);
        Assert.Equal("idx_email", hint.IndexName);
        Assert.Equal(IndexHintType.Force, hint.HintType);
        _output.WriteLine($"✓ Parsed force hint: {hint.TableName}.{hint.IndexName}");
    }

    [Fact]
    public void IndexHint_Parse_ParsesAvoidHint()
    {
        // Arrange
        var hintString = "/*+ NO_INDEX(products idx_price) */";

        // Act
        var hint = IndexHint.Parse(hintString);

        // Assert
        Assert.NotNull(hint);
        Assert.Equal("products", hint.TableName);
        Assert.Equal("idx_price", hint.IndexName);
        Assert.Equal(IndexHintType.Avoid, hint.HintType);
        _output.WriteLine($"✓ Parsed avoid hint");
    }

    [Fact]
    public void IndexHintCollection_ShouldUseIndex_RespectsForceHints()
    {
        // Arrange
        var collection = new IndexHintCollection();
        collection.Add(new IndexHint
        {
            TableName = "users",
            IndexName = "idx_email",
            HintType = IndexHintType.Force
        });

        // Act & Assert
        Assert.True(collection.ShouldUseIndex("users", "idx_email"));
        Assert.False(collection.ShouldUseIndex("users", "idx_name")); // Other indexes excluded
        _output.WriteLine("✓ Force hint excludes other indexes");
    }

    [Fact]
    public void IndexHintCollection_GetPreferredOrder_OrdersByPriority()
    {
        // Arrange
        var collection = new IndexHintCollection();
        collection.Add(new IndexHint
        {
            TableName = "users",
            IndexName = "idx_forced",
            HintType = IndexHintType.Force
        });
        collection.Add(new IndexHint
        {
            TableName = "users",
            IndexName = "idx_preferred",
            HintType = IndexHintType.Prefer
        });

        var availableIndexes = new[] { "idx_other", "idx_preferred", "idx_forced" };

        // Act
        var ordered = collection.GetPreferredOrder("users", availableIndexes).ToList();

        // Assert
        Assert.Equal("idx_forced", ordered[0]); // Forced first
        Assert.Equal("idx_preferred", ordered[1]); // Preferred second
        Assert.Equal("idx_other", ordered[2]); // Others last
        _output.WriteLine($"✓ Preferred order: {string.Join(", ", ordered)}");
    }

    // ========================================
    // PartialIndex Tests
    // ========================================

    [Fact]
    public void PartialIndex_Add_OnlyIndexesMatchingRows()
    {
        // Arrange
        using var index = new PartialIndex<int, string>(
            value => int.Parse(value.Split(':')[0]), // Key selector: parse ID
            value => value.EndsWith(":active")); // Predicate: only ":active" suffix

        // Act
        bool added1 = index.Add("1:active");
        bool added2 = index.Add("2:inactive");
        bool added3 = index.Add("3:active");

        // Assert
        Assert.True(added1);
        Assert.False(added2); // Not indexed because predicate failed
        Assert.True(added3);
        Assert.Equal(2, index.Count);
        _output.WriteLine($"✓ Partial index contains {index.Count} entries (filtered from 3)");
    }

    [Fact]
    public void PartialIndex_Lookup_ReturnsMatchingValues()
    {
        // Arrange
        using var index = new PartialIndex<string, string>(
            value => value.Split(':')[0], // Key: email address (before colon)
            value => value.Contains("@gmail.com")); // Only gmail addresses

        index.Add("alice@gmail.com:user1");
        index.Add("bob@gmail.com:user2");
        index.Add("charlie@yahoo.com:user3"); // Not indexed

        // Act
        var gmailUsers = index.Lookup("alice@gmail.com").ToList();

        // Assert
        Assert.Single(gmailUsers);
        Assert.Contains("alice@gmail.com:user1", gmailUsers);
        _output.WriteLine($"✓ Lookup returned {gmailUsers.Count} gmail users");
    }

    [Fact]
    public void PartialIndex_GetStats_ReportsCorrectly()
    {
        // Arrange
        using var index = new PartialIndex<int, int>(
            value => value % 10, // Key: last digit
            value => value > 50); // Only values > 50

        for (int i = 1; i <= 100; i++)
        {
            index.Add(i);
        }

        // Act
        var stats = index.GetStats();

        // Assert
        Assert.Equal(50, stats.TotalEntries); // 51-100 = 50 values
        Assert.Equal(10, stats.KeyCount); // 10 distinct last digits (0-9)
        _output.WriteLine($"✓ Partial index stats: {stats.TotalEntries} entries, {stats.KeyCount} keys");
    }

    // ========================================
    // ExpressionIndex Tests
    // ========================================

    [Fact]
    public void ExpressionIndex_Add_ComputesAndStores()
    {
        // Arrange: Index on LOWER(email)
        using var index = new ExpressionIndex<string, string, string>(
            email => email.ToLowerInvariant(), // Expression: lowercase
            email => email); // Value: original email

        // Act
        index.Add("Alice@Example.COM");
        index.Add("BOB@TEST.ORG");

        // Assert
        var results = index.Lookup("alice@example.com").ToList();
        Assert.Single(results);
        Assert.Equal("Alice@Example.COM", results[0]);
        _output.WriteLine($"✓ Expression index found case-insensitive match");
    }

    [Fact]
    public void ExpressionIndex_LookupByInput_ComputesOnTheFly()
    {
        // Arrange: Index on YEAR(date)
        using var index = new ExpressionIndex<DateTime, int, string>(
            date => date.Year, // Expression: extract year
            date => date.ToString("yyyy-MM-dd")); // Value: date string

        var dates = new[]
        {
            new DateTime(2025, 1, 15),
            new DateTime(2025, 6, 20),
            new DateTime(2026, 3, 10)
        };

        foreach (var date in dates)
        {
            index.Add(date);
        }

        // Act: Lookup by input (will compute year)
        var dates2025 = index.LookupByInput(new DateTime(2025, 12, 31)).ToList();

        // Assert
        Assert.Equal(2, dates2025.Count);
        _output.WriteLine($"✓ Found {dates2025.Count} dates in 2025");
    }

    [Fact]
    public void ExpressionIndex_GetStats_ShowsCachedComputations()
    {
        // Arrange
        using var index = new ExpressionIndex<string, int, string>(
            s => s.Length, // Expression: string length
            s => s);

        index.Add("a");
        index.Add("ab");
        index.Add("abc");
        index.Add("abcd");

        // Act
        var stats = index.GetStats();

        // Assert
        Assert.Equal(4, stats.TotalEntries);
        Assert.Equal(4, stats.CachedComputations);
        Assert.Equal(4, stats.KeyCount); // 4 distinct lengths
        _output.WriteLine($"✓ Expression index: {stats.CachedComputations} cached computations");
    }

    // ========================================
    // AdaptiveIndexManager Tests
    // ========================================

    [Fact]
    public void AdaptiveIndexManager_RecordQuery_TracksPatterns()
    {
        // Arrange
        using var manager = new AdaptiveIndexManager();

        // Act: Record the same query multiple times
        for (int i = 0; i < 15; i++)
        {
            manager.RecordQuery("users", ["email", "name"]);
        }

        // Assert
        Assert.Equal(1, manager.QueryPatternCount);
        _output.WriteLine($"✓ Tracked {manager.QueryPatternCount} query pattern");
    }

    [Fact]
    public void AdaptiveIndexManager_GetRecommendations_SuggestsIndexes()
    {
        // Arrange
        using var manager = new AdaptiveIndexManager(new AdaptiveIndexOptions
        {
            MinExecutionCountForRecommendation = 5
        });

        // Act: Execute query 10 times (above threshold)
        for (int i = 0; i < 10; i++)
        {
            manager.RecordQuery("products", ["category", "price"]);
        }

        var recommendations = manager.GetRecommendations().ToList();

        // Assert
        Assert.NotEmpty(recommendations);
        Assert.Contains(recommendations, r => r.TableName == "products");
        _output.WriteLine($"✓ Got {recommendations.Count} recommendations");
        foreach (var rec in recommendations)
        {
            _output.WriteLine($"  {rec.IndexName}: {rec.Reason} (priority={rec.Priority})");
        }
    }

    [Fact]
    public void AdaptiveIndexManager_RecordIndexUsage_TracksStatistics()
    {
        // Arrange
        using var manager = new AdaptiveIndexManager();

        // Act
        manager.RecordIndexUsage("idx_email", 100, TimeSpan.FromMilliseconds(10));
        manager.RecordIndexUsage("idx_email", 150, TimeSpan.FromMilliseconds(15));

        var stats = manager.GetIndexUsage("idx_email");

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(2, stats.UsageCount);
        Assert.Equal(250, stats.TotalRowsScanned);
        Assert.Equal(125, stats.AverageRowsPerQuery);
        _output.WriteLine($"✓ Index usage: {stats.UsageCount} uses, avg {stats.AverageRowsPerQuery} rows");
    }

    [Fact]
    public void AdaptiveIndexManager_GetUnusedIndexes_FindsStaleIndexes()
    {
        // Arrange
        using var manager = new AdaptiveIndexManager();

        // Record old usage
        manager.RecordIndexUsage("idx_old", 10, TimeSpan.FromMilliseconds(5));

        // Act: Check for unused (with a very short threshold)
        var unused = manager.GetUnusedIndexes(TimeSpan.FromSeconds(0.1)).ToList();

        System.Threading.Thread.Sleep(150); // Wait slightly longer than threshold

        unused = manager.GetUnusedIndexes(TimeSpan.FromSeconds(0.1)).ToList();

        // Assert
        Assert.Contains("idx_old", unused);
        _output.WriteLine($"✓ Found {unused.Count} unused indexes");
    }

    // ========================================
    // IndexOptimizer Tests
    // ========================================

    [Fact]
    public void IndexOptimizer_RecommendColumnOrder_OrdersBySelectivity()
    {
        // Arrange
        var columns = new List<string> { "name", "email", "id" };
        var statistics = new Dictionary<string, ColumnStatistics>
        {
            ["name"] = new() { TotalRows = 1000, DistinctValues = 200 }, // 20% selectivity
            ["email"] = new() { TotalRows = 1000, DistinctValues = 900 }, // 90% selectivity
            ["id"] = new() { TotalRows = 1000, DistinctValues = 1000 } // 100% selectivity
        };

        // Act
        var ordered = IndexOptimizer.RecommendColumnOrder(columns, statistics);

        // Assert: Should order by selectivity (id, email, name)
        Assert.Equal("id", ordered[0]);
        Assert.Equal("email", ordered[1]);
        Assert.Equal("name", ordered[2]);
        _output.WriteLine($"✓ Optimal column order: {string.Join(", ", ordered)}");
    }

    [Fact]
    public void IndexOptimizer_SelectBestIndex_ChoosesMatchingIndex()
    {
        // Arrange
        var queryColumns = new List<string> { "email", "name" };
        var availableIndexes = new List<IndexDefinition>
        {
            new() { Name = "idx_id", TableName = "users", Columns = ["id"] },
            new() { Name = "idx_email", TableName = "users", Columns = ["email"] },
            new() { Name = "idx_email_name", TableName = "users", Columns = ["email", "name"] }
        };
        var statistics = new Dictionary<string, ColumnStatistics>
        {
            ["email"] = new() { TotalRows = 1000, DistinctValues = 900 }
        };

        // Act
        var result = IndexOptimizer.SelectBestIndex(queryColumns, availableIndexes, statistics);

        // Assert: Should choose idx_email_name (exact match)
        Assert.NotNull(result.SelectedIndex);
        Assert.Equal("idx_email_name", result.SelectedIndex.Name);
        _output.WriteLine($"✓ Selected index: {result.SelectedIndex.Name} ({result.Reason})");
    }

    [Fact]
    public void IndexOptimizer_SelectBestIndex_RespectsForceHint()
    {
        // Arrange
        var queryColumns = new List<string> { "email" };
        var availableIndexes = new List<IndexDefinition>
        {
            new() { Name = "idx_email", TableName = "users", Columns = ["email"] },
            new() { Name = "idx_name", TableName = "users", Columns = ["name"] }
        };
        var statistics = new Dictionary<string, ColumnStatistics>();
        var hints = new IndexHintCollection();
        hints.Add(new IndexHint
        {
            TableName = "users",
            IndexName = "idx_name",
            HintType = IndexHintType.Force
        });

        // Act
        var result = IndexOptimizer.SelectBestIndex(queryColumns, availableIndexes, statistics, hints);

        // Assert: Should use forced index even if not optimal
        Assert.NotNull(result.SelectedIndex);
        Assert.Equal("idx_name", result.SelectedIndex.Name);
        _output.WriteLine($"✓ Forced index used: {result.SelectedIndex.Name}");
    }

    [Fact]
    public void IndexOptimizer_AnalyzeIndexDesign_DetectsIssues()
    {
        // Arrange
        var index = new IndexDefinition
        {
            Name = "idx_test",
            TableName = "users",
            Columns = ["name", "id", "email", "phone", "address", "city"] // Too many columns
        };
        var statistics = new Dictionary<string, ColumnStatistics>();

        // Act
        var analysis = IndexOptimizer.AnalyzeIndexDesign(index, statistics);

        // Assert
        Assert.NotEmpty(analysis.Recommendations);
        _output.WriteLine($"✓ Design analysis score: {analysis.OverallScore:F1}/100");
        foreach (var rec in analysis.Recommendations)
        {
            _output.WriteLine($"  - {rec}");
        }
    }

    [Fact]
    public void IndexOptimizer_EstimateColumnSelectivity_CalculatesCorrectly()
    {
        // Arrange
        var statistics = new Dictionary<string, ColumnStatistics>
        {
            ["id"] = new() { TotalRows = 1000, DistinctValues = 1000 }, // 100% unique
            ["category"] = new() { TotalRows = 1000, DistinctValues = 10 } // 1% unique
        };

        // Act
        var idSelectivity = IndexOptimizer.EstimateColumnSelectivity("id", statistics);
        var categorySelectivity = IndexOptimizer.EstimateColumnSelectivity("category", statistics);

        // Assert
        Assert.Equal(1.0, idSelectivity);
        Assert.Equal(0.01, categorySelectivity);
        _output.WriteLine($"✓ Selectivity: id={idSelectivity:P0}, category={categorySelectivity:P0}");
    }
}
