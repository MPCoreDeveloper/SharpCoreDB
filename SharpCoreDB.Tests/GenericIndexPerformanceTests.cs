// <copyright file="GenericIndexPerformanceTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System.Diagnostics;
using Xunit;

/// <summary>
/// Performance tests for generic indexes with 10k records.
/// Target: < 0.05ms (50 microseconds) per lookup.
/// Comparison baseline: SQLite typically ~0.2ms for same query.
/// </summary>
public sealed class GenericIndexPerformanceTests
{
    private const int RecordCount = 10_000;
    private const double TargetMilliseconds = 0.05; // 50 microseconds
    
    [Fact]
    public void GenericHashIndex_10kRecords_LookupUnder50Microseconds()
    {
        // Arrange: Create index and populate with 10k records
        var index = new GenericHashIndex<int>("user_id");
        var random = new Random(42); // Deterministic

        // Insert 10k records (ids 0-9999)
        for (int i = 0; i < RecordCount; i++)
        {
            index.Add(i, i * 100); // Position = id * 100
        }

        // Warm up JIT
        for (int i = 0; i < 100; i++)
        {
            _ = index.Find(i % RecordCount).ToList();
        }

        // Act: Benchmark lookup performance
        var sw = Stopwatch.StartNew();
        var iterations = 1000;
        
        for (int i = 0; i < iterations; i++)
        {
            var key = random.Next(RecordCount);
            var positions = index.Find(key).ToList();
            Assert.Single(positions); // Each key has exactly one position
        }
        
        sw.Stop();
        
        var avgMilliseconds = sw.Elapsed.TotalMilliseconds / iterations;
        var avgMicroseconds = avgMilliseconds * 1000;

        // Assert: Must be faster than 50 microseconds (0.05ms)
        Assert.True(avgMilliseconds < TargetMilliseconds,
            $"Average lookup time {avgMilliseconds:F4}ms ({avgMicroseconds:F1}탎) " +
            $"exceeds target of {TargetMilliseconds}ms (50탎). " +
            $"Target is 4x faster than SQLite (~0.2ms).");

        // Log performance
        Console.WriteLine($"? Generic Hash Index Performance:");
        Console.WriteLine($"   Records: {RecordCount:N0}");
        Console.WriteLine($"   Iterations: {iterations:N0}");
        Console.WriteLine($"   Avg lookup: {avgMilliseconds:F4}ms ({avgMicroseconds:F1}탎)");
        Console.WriteLine($"   Target: < {TargetMilliseconds}ms (50탎)");
        Console.WriteLine($"   vs SQLite: ~4x faster (SQLite ~0.2ms)");
        Console.WriteLine($"   Status: {(avgMilliseconds < TargetMilliseconds ? "PASS ?" : "FAIL ?")}");
    }

    [Fact]
    public void GenericHashIndex_StringKeys_10kRecords_PerformanceTest()
    {
        // Arrange: String keys (more realistic for user names, emails, etc.)
        var index = new GenericHashIndex<string>("email");
        
        // Insert 10k email addresses
        for (int i = 0; i < RecordCount; i++)
        {
            var email = $"user{i}@example.com";
            index.Add(email, i);
        }

        var random = new Random(42);
        
        // Warm up
        for (int i = 0; i < 100; i++)
        {
            _ = index.Find($"user{i}@example.com").ToList();
        }

        // Act: Benchmark
        var sw = Stopwatch.StartNew();
        var iterations = 1000;
        
        for (int i = 0; i < iterations; i++)
        {
            var userId = random.Next(RecordCount);
            var email = $"user{userId}@example.com";
            var positions = index.Find(email).ToList();
            Assert.Single(positions);
        }
        
        sw.Stop();
        
        var avgMilliseconds = sw.Elapsed.TotalMilliseconds / iterations;
        var avgMicroseconds = avgMilliseconds * 1000;

        // Assert: String keys should still be under 50 microseconds
        Assert.True(avgMilliseconds < TargetMilliseconds,
            $"String key lookup {avgMilliseconds:F4}ms exceeds target");

        Console.WriteLine($"? String Key Performance:");
        Console.WriteLine($"   Avg lookup: {avgMilliseconds:F4}ms ({avgMicroseconds:F1}탎)");
    }

    [Fact]
    public void GenericHashIndex_DuplicateKeys_PerformanceTest()
    {
        // Arrange: Test with duplicate keys (e.g., category column)
        var index = new GenericHashIndex<string>("category");
        
        var categories = new[] { "Electronics", "Books", "Clothing", "Food", "Toys" };
        
        // Each category has 2000 items
        for (int i = 0; i < RecordCount; i++)
        {
            var category = categories[i % categories.Length];
            index.Add(category, i);
        }

        // Warm up
        for (int i = 0; i < 100; i++)
        {
            _ = index.Find(categories[i % categories.Length]).ToList();
        }

        // Act: Find all items in a category
        var sw = Stopwatch.StartNew();
        var iterations = 1000;
        
        for (int i = 0; i < iterations; i++)
        {
            var category = categories[i % categories.Length];
            var positions = index.Find(category).ToList();
            Assert.Equal(2000, positions.Count); // Each category has 2000 items
        }
        
        sw.Stop();
        
        var avgMilliseconds = sw.Elapsed.TotalMilliseconds / iterations;
        
        // Even with 2000 results, should be fast
        Assert.True(avgMilliseconds < 1.0, // Allow 1ms for returning 2000 results
            $"Duplicate key lookup {avgMilliseconds:F4}ms exceeds 1ms target");

        Console.WriteLine($"? Duplicate Keys Performance:");
        Console.WriteLine($"   Results per query: 2,000");
        Console.WriteLine($"   Avg time: {avgMilliseconds:F4}ms");
    }

    [Fact]
    public void IndexManager_AutoIndexing_AnalysisPerformance()
    {
        // Arrange: Test PRAGMA-based analysis performance
        var manager = new IndexManager(enableAutoIndexing: true);
        
        // Create sample data with good indexing candidates
        var rows = new List<Dictionary<string, object?>>();
        for (int i = 0; i < RecordCount; i++)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = i,                              // High selectivity - should index
                ["email"] = $"user{i}@example.com",      // High selectivity - should index
                ["category"] = i % 5,                     // Low selectivity - might skip
                ["active"] = i % 2 == 0,                  // Very low selectivity - should skip
                ["name"] = $"User {i}"
            });
        }

        // Act: Analyze table and create indexes
        var sw = Stopwatch.StartNew();
        var tableInfo = manager.AnalyzeAndCreateIndexes("users", rows);
        sw.Stop();

        // Assert: Analysis should be fast (< 50ms for 10k rows)
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"Analysis took {sw.ElapsedMilliseconds}ms, target < 50ms");

        // Verify correct indexes were recommended
        Assert.NotEmpty(tableInfo.Indexes);
        Assert.True(tableInfo.ColumnStatistics["id"].ShouldIndex, "ID should be indexed");
        Assert.True(tableInfo.ColumnStatistics["email"].ShouldIndex, "Email should be indexed");
        
        Console.WriteLine($"? Auto-Indexing Analysis:");
        Console.WriteLine($"   Rows analyzed: {RecordCount:N0}");
        Console.WriteLine($"   Analysis time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Indexes recommended: {tableInfo.Indexes.Count}");
        foreach (var index in tableInfo.Indexes)
        {
            Console.WriteLine($"   - {index.ColumnName}: {index.Type} " +
                            $"(selectivity={index.Statistics.Selectivity:F2})");
        }

        manager.Dispose();
    }

    [Fact]
    public void GenericHashIndex_MemoryEfficiency_Test()
    {
        // Arrange: Test memory usage is reasonable
        var index = new GenericHashIndex<int>("id");
        
        for (int i = 0; i < RecordCount; i++)
        {
            index.Add(i, i);
        }

        // Act: Get statistics
        var stats = index.GetStatistics();

        // Assert: Memory usage should be reasonable
        // Expected: ~10k keys * (4 bytes int + 8 bytes long + overhead) ? 200-300 KB
        var memoryKB = stats.MemoryUsageBytes / 1024.0;
        Assert.True(memoryKB < 500, // Allow up to 500KB for 10k records
            $"Memory usage {memoryKB:F2}KB exceeds 500KB limit");

        Console.WriteLine($"? Memory Efficiency:");
        Console.WriteLine($"   Records: {RecordCount:N0}");
        Console.WriteLine($"   Memory: {memoryKB:F2} KB");
        Console.WriteLine($"   Per record: {stats.MemoryUsageBytes / (double)RecordCount:F1} bytes");
        Console.WriteLine($"   Selectivity: {stats.Selectivity:F2}");
    }

    [Fact]
    public void GenericHashIndex_BulkInsert_Performance()
    {
        // Arrange & Act: Test bulk insert performance
        var index = new GenericHashIndex<int>("id");
        
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < RecordCount; i++)
        {
            index.Add(i, i);
        }
        sw.Stop();

        // Assert: Should insert 10k records in < 5ms
        Assert.True(sw.ElapsedMilliseconds < 5,
            $"Bulk insert took {sw.ElapsedMilliseconds}ms, target < 5ms");

        Console.WriteLine($"? Bulk Insert Performance:");
        Console.WriteLine($"   Records: {RecordCount:N0}");
        Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Rate: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec");
    }
}
