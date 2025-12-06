using SharpCoreDB.DataStructures;
using System.Diagnostics;

namespace SharpCoreDB.Tests;

/// <summary>
/// Unit and integration tests for index functionality.
/// Covers hash indexes, performance benchmarks, and edge cases.
/// </summary>
public class IndexTests
{
    [Fact]
    public void HashIndex_Lookup_Performance_Benchmark()
    {
        // Arrange
        var index = new HashIndex("benchmark", "key");
        var rowCount = 100000;
        var uniqueKeys = 1000;

        // Act - Build index with large dataset
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < rowCount; i++)
        {
            var row = new Dictionary<string, object>
            {
                { "key", $"key_{i % uniqueKeys}" },
                { "value", i },
                { "data", $"data_{i}" }
            };
            index.Add(row);
        }
        sw.Stop();
        var buildTime = sw.ElapsedMilliseconds;

        // Act - Perform lookups
        sw.Restart();
        var lookupCount = 1000;
        for (int i = 0; i < lookupCount; i++)
        {
            var results = index.Lookup($"key_{i % uniqueKeys}");
            Assert.NotEmpty(results);
        }
        sw.Stop();
        var lookupTime = sw.ElapsedMilliseconds;

        // Assert - Performance should be reasonable
        Assert.True(buildTime < 5000, $"Index build took {buildTime}ms, should be < 5000ms");
        Assert.True(lookupTime < 1000, $"Index lookups took {lookupTime}ms, should be < 1000ms");

        var stats = index.GetStatistics();
        Assert.Equal(uniqueKeys, stats.UniqueKeys);
        Assert.Equal(rowCount, stats.TotalRows);
    }

    [Fact]
    public void HashIndex_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var index = new HashIndex("concurrent", "id");
        var tasks = new List<Task>();

        // Act - Add rows from multiple threads
        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    var row = new Dictionary<string, object>
                    {
                        { "id", i },
                        { "thread", t },
                        { "data", $"data_{t}_{i}" }
                    };
                    index.Add(row);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - All rows should be indexed
        var totalRows = 0;
        for (int i = 0; i < 100; i++)
        {
            var results = index.Lookup(i);
            Assert.Equal(10, results.Count); // 10 threads added same id
            totalRows += results.Count;
        }
        Assert.Equal(1000, totalRows);
    }

    [Fact]
    public void HashIndex_MemoryUsage_Efficient()
    {
        // Arrange
        var index = new HashIndex("memory", "key");
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Add many rows
        for (int i = 0; i < 50000; i++)
        {
            var row = new Dictionary<string, object>
            {
                { "key", $"key_{i % 1000}" },
                { "value", i }
            };
            index.Add(row);
        }

        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = finalMemory - initialMemory;

        // Assert - Memory usage should be reasonable (< 50MB for 50k rows)
        Assert.True(memoryUsed < 50 * 1024 * 1024, $"Memory used: {memoryUsed / 1024 / 1024}MB, should be < 50MB");
    }

    [Fact]
    public void HashIndex_IndexLookup_Vs_TableScan_Performance()
    {
        // Arrange - Create test data
        var index = new HashIndex("perf_test", "category");
        var rows = new List<Dictionary<string, object>>();
        for (int i = 0; i < 10000; i++)
        {
            rows.Add(new Dictionary<string, object>
            {
                { "id", i },
                { "category", $"cat_{i % 10}" },
                { "data", $"data_{i}" }
            });
        }

        index.Rebuild(rows);

        // Act - Index lookup
        var sw = Stopwatch.StartNew();
        var indexResults = index.Lookup("cat_5");
        sw.Stop();
        var indexTime = sw.ElapsedTicks;

        // Act - Simulate table scan
        sw.Restart();
        var scanResults = rows.Where(r => r["category"].ToString() == "cat_5").ToList();
        sw.Stop();
        var scanTime = sw.ElapsedTicks;

        // Assert - Index should be significantly faster
        Assert.Equal(scanResults.Count, indexResults.Count);
        Assert.True(indexTime < scanTime / 10, $"Index lookup should be at least 10x faster. Index: {indexTime} ticks, Scan: {scanTime} ticks");
    }

    [Fact]
    public void HashIndex_Rebuild_LargeDataset_Efficient()
    {
        // Arrange
        var index = new HashIndex("rebuild_test", "key");
        var rows = new List<Dictionary<string, object>>();
        for (int i = 0; i < 100000; i++)
        {
            rows.Add(new Dictionary<string, object>
            {
                { "key", $"key_{i % 5000}" },
                { "value", i }
            });
        }

        // Act
        var sw = Stopwatch.StartNew();
        index.Rebuild(rows);
        sw.Stop();

        // Assert - Rebuild should be fast
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Rebuild took {sw.ElapsedMilliseconds}ms, should be < 2000ms");

        var stats = index.GetStatistics();
        Assert.Equal(5000, stats.UniqueKeys);
        Assert.Equal(100000, stats.TotalRows);
    }

    [Fact]
    public void HashIndex_UpdateOperations_MaintainConsistency()
    {
        // Arrange
        var index = new HashIndex("update_test", "status");
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "status", "active" }, { "name", "Item1" } },
            new() { { "id", 2 }, { "status", "active" }, { "name", "Item2" } },
            new() { { "id", 3 }, { "status", "inactive" }, { "name", "Item3" } }
        };

        foreach (var row in rows)
        {
            index.Add(row);
        }

        // Act - Update status
        var updatedRow = new Dictionary<string, object> { { "id", 1 }, { "status", "inactive" }, { "name", "Item1" } };
        index.Remove(rows[0]);
        index.Add(updatedRow);

        // Assert - Index should reflect changes
        Assert.Single(index.Lookup("active"));
        Assert.Equal(2, index.Lookup("inactive").Count);
    }

    [Fact]
    public void HashIndex_EdgeCases_NullAndEmptyKeys()
    {
        // Arrange
        var index = new HashIndex("edge_cases", "key");

        // Act & Assert - Null keys should be ignored
        var nullRow = new Dictionary<string, object> { { "key", (object)null! }, { "value", 1 } };
        index.Add(nullRow);
        Assert.Equal(0, index.Count);

        // Empty string keys should work
        var emptyRow = new Dictionary<string, object> { { "key", "" }, { "value", 2 } };
        index.Add(emptyRow);
        Assert.Single(index.Lookup(""));
        Assert.Equal(1, index.Count);

        // Missing key column should be ignored
        var missingKeyRow = new Dictionary<string, object> { { "value", 3 } };
        index.Add(missingKeyRow);
        Assert.Equal(1, index.Count); // Still only the empty string key
    }

    [Fact]
    public void HashIndex_Statistics_Accurate()
    {
        // Arrange
        var index = new HashIndex("stats_test", "group");

        // Act - Add rows with varying distribution
        var distributions = new[] { 1, 1, 1, 2, 2, 5, 5, 5, 5, 5 }; // Expect 3 unique keys: 1,2,5
        foreach (var key in distributions)
        {
            var row = new Dictionary<string, object> { { "group", key }, { "data", $"item_{key}" } };
            index.Add(row);
        }

        var stats = index.GetStatistics();

        // Assert
        Assert.Equal(3, stats.UniqueKeys);
        Assert.Equal(10, stats.TotalRows);
        Assert.Equal(10.0 / 3.0, stats.AvgRowsPerKey);
    }

    [Fact]
    public void HashIndex_ClearAndRebuild_Consistent()
    {
        // Arrange
        var index = new HashIndex("clear_test", "id");
        var initialRows = new List<Dictionary<string, object>>();
        for (int i = 0; i < 100; i++)
        {
            initialRows.Add(new Dictionary<string, object> { { "id", i }, { "value", $"val_{i}" } });
            index.Add(initialRows[i]);
        }

        // Act - Clear and rebuild with different data
        index.Clear();
        var newRows = new List<Dictionary<string, object>>();
        for (int i = 0; i < 50; i++)
        {
            newRows.Add(new Dictionary<string, object> { { "id", i + 100 }, { "value", $"new_{i}" } });
        }
        index.Rebuild(newRows);

        // Assert - Should only contain new data
        Assert.Equal(50, index.Count);
        for (int i = 0; i < 50; i++)
        {
            Assert.Single(index.Lookup(i + 100));
        }
        for (int i = 0; i < 100; i++)
        {
            Assert.Empty(index.Lookup(i));
        }
    }
}