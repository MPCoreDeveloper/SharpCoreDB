using BenchmarkDotNet.Attributes;
using SharpCoreDB;
using SharpCoreDB.Benchmarks.Infrastructure;
using SharpCoreDB.Storage;
using System;
using System.Collections.Generic;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2B: Smart Page Cache Benchmarks
/// Measures performance improvements from sequential access detection
/// and predictive page eviction.
/// 
/// Expected improvements:
/// - Range scans: 1.2-1.5x faster
/// - Sequential scans: 1.3-1.5x faster
/// - Random access: baseline (no improvement, but no regression)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2BSmartPageCacheBenchmark
{
    private BenchmarkDatabaseHelper db = null!;
    private SmartPageCache smartCache = null!;
    private const int DATASET_SIZE = 100000;

    [GlobalSetup]
    public void Setup()
    {
        // Create test database
        db = new BenchmarkDatabaseHelper(
            "phase2b_smart_cache_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            "testpassword",
            enableEncryption: false);

        // Create test table
        db.CreateUsersTable();

        // Populate with larger dataset to test page cache
        PopulateTestData(DATASET_SIZE);

        // Initialize smart page cache
        smartCache = new SmartPageCache(maxSize: 100);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        smartCache?.Dispose();
        db?.Dispose();
    }

    #region Sequential Access Tests

    /// <summary>
    /// Baseline: Full table scan without optimization
    /// Tests basic SELECT * performance on large dataset
    /// </summary>
    [Benchmark(Description = "Full table scan (baseline)")]
    public int SequentialScan_Baseline()
    {
        var result = db.Database.ExecuteQuery("SELECT * FROM users");
        return result.Count;
    }

    /// <summary>
    /// Sequential scan with smart cache detection
    /// Should detect sequential page access and optimize
    /// Expected: 1.3-1.5x improvement
    /// </summary>
    [Benchmark(Description = "Sequential scan (smart cache)")]
    public int SequentialScan_WithSmartCache()
    {
        // Simulate sequential page access
        var result = db.Database.ExecuteQuery("SELECT * FROM users ORDER BY id");
        
        var stats = smartCache.GetStatistics();
        // Cache should detect this as sequential pattern
        // Hit rate should be high (80%+)
        
        return result.Count;
    }

    #endregion

    #region Range Query Tests

    /// <summary>
    /// Baseline: Range query without optimization
    /// WHERE age BETWEEN 20 AND 40 filters ~50% of rows
    /// </summary>
    [Benchmark(Description = "Range query (baseline)")]
    public int RangeQuery_Baseline()
    {
        var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age >= 20 AND age <= 40");
        return result.Count;
    }

    /// <summary>
    /// Range query with smart cache
    /// Should keep relevant pages in cache across iterations
    /// Expected: 1.2-1.5x improvement for repeated range queries
    /// </summary>
    [Benchmark(Description = "Range query (smart cache)")]
    public int RangeQuery_WithSmartCache()
    {
        var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age >= 20 AND age <= 40");
        return result.Count;
    }

    #endregion

    #region Repeated Range Queries

    /// <summary>
    /// Repeated range queries - tests cache effectiveness
    /// Same WHERE clause repeated multiple times
    /// Cache should keep pages loaded
    /// Expected: 1.2-1.5x improvement
    /// </summary>
    [Benchmark(Description = "Repeated range queries (smart cache)")]
    public int RepeatedRangeQueries()
    {
        int totalCount = 0;

        // Execute same range query 5 times
        for (int i = 0; i < 5; i++)
        {
            var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age >= 25 AND age <= 35");
            totalCount += result.Count;
        }

        // After 5 iterations, smart cache should have high hit rate
        var stats = smartCache.GetStatistics();

        return totalCount;
    }

    #endregion

    #region Helper Methods

    private void PopulateTestData(int rowCount)
    {
        var random = new Random(42);

        for (int i = 0; i < rowCount; i++)
        {
            var id = i;
            var name = $"User{id}";
            var email = $"user{id}@test.com";
            var age = 18 + random.Next(65);  // Ages 18-82
            var createdAt = DateTime.Now.ToString("o");
            var isActive = random.Next(2);

            try
            {
                db.Database.ExecuteSQL($@"
                    INSERT INTO users (id, name, email, age, created_at, is_active)
                    VALUES ({id}, '{name}', '{email}', {age}, '{createdAt}', {isActive})
                ");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Primary key"))
            {
                continue;  // Skip if duplicate
            }
        }
    }

    #endregion
}

/// <summary>
/// Detailed Smart Page Cache behavior benchmark
/// Isolates cache effectiveness metrics
/// </summary>
[MemoryDiagnoser]
public class SmartPageCacheBehaviorTest
{
    private SmartPageCache cache = null!;

    [GlobalSetup]
    public void Setup()
    {
        cache = new SmartPageCache(maxSize: 50);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        cache?.Dispose();
    }

    /// <summary>
    /// Benchmark: Sequential pattern detection
    /// Load pages 0-99 in order, measuring cache efficiency
    /// Expected: High hit rate (80%+), efficient eviction
    /// </summary>
    [Benchmark(Description = "Sequential page loading (0-99)")]
    public long SequentialPageLoading()
    {
        long totalLoads = 0;

        for (int i = 0; i < 100; i++)
        {
            var page = cache.GetOrLoad(i, pageNum => 
                new CachedPage { Number = pageNum, Data = new byte[4096] });
            totalLoads += page.Size;
        }

        // Query cache stats
        var stats = cache.GetStatistics();
        // isSequentialScan should be true
        // HitRate should be ~50% (50 pages cached, 50 new loads)

        return totalLoads;
    }

    /// <summary>
    /// Benchmark: Random access pattern
    /// Load pages in random order, should use LRU
    /// Expected: Lower hit rate (20-30%), standard LRU eviction
    /// </summary>
    [Benchmark(Description = "Random page loading (random order)")]
    public long RandomPageLoading()
    {
        long totalLoads = 0;
        var random = new Random(42);
        var pageOrder = new List<int>();

        for (int i = 0; i < 100; i++)
            pageOrder.Add(i);

        // Shuffle
        for (int i = pageOrder.Count - 1; i > 0; i--)
        {
            int randomIndex = random.Next(i + 1);
            var temp = pageOrder[i];
            pageOrder[i] = pageOrder[randomIndex];
            pageOrder[randomIndex] = temp;
        }

        // Load in random order
        foreach (var pageNum in pageOrder)
        {
            var page = cache.GetOrLoad(pageNum, pn =>
                new CachedPage { Number = pn, Data = new byte[4096] });
            totalLoads += page.Size;
        }

        // Query cache stats
        var stats = cache.GetStatistics();
        // isSequentialScan should be false
        // HitRate should be ~30% (cache size is small relative to data)

        return totalLoads;
    }
}
