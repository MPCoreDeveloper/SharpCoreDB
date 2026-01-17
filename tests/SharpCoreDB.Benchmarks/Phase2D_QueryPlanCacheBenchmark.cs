using BenchmarkDotNet.Attributes;
using SharpCoreDB.Execution;
using System;
using System.Collections.Generic;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2D Friday: Query Plan Cache Benchmarks
/// 
/// Demonstrates cache hit efficiency and latency improvements.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2D_QueryPlanCacheBenchmark
{
    private QueryPlanCache cache = null!;
    private string[] uniqueQueries = null!;
    private const int QueryCount = 1000;
    private const int UniqueQueries = 100;

    [GlobalSetup]
    public void Setup()
    {
        cache = new QueryPlanCache(maxCacheSize: UniqueQueries);
        
        // Create unique parameterized queries
        uniqueQueries = new string[UniqueQueries];
        for (int i = 0; i < UniqueQueries; i++)
        {
            uniqueQueries[i] = $"SELECT * FROM users WHERE id = {i}";
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        cache.Clear();
    }

    /// <summary>
    /// Baseline: No caching (reparse every query)
    /// </summary>
    [Benchmark(Description = "Query Execution - No Caching")]
    public void QueryExecution_NoCaching()
    {
        for (int i = 0; i < QueryCount; i++)
        {
            string query = uniqueQueries[i % UniqueQueries];
            // Without cache: always re-parse
            var plan = ParseQueryDirectly(query);
            // Execute plan...
        }
    }

    /// <summary>
    /// Optimized: With query plan cache
    /// Expected: 1.5-2x faster due to cache hits
    /// </summary>
    [Benchmark(Description = "Query Execution - With Plan Caching")]
    public void QueryExecution_WithCaching()
    {
        for (int i = 0; i < QueryCount; i++)
        {
            string query = uniqueQueries[i % UniqueQueries];
            // With cache: hits after first use
            var plan = cache.GetOrCreate(query);
            // Execute plan...
        }
    }

    /// <summary>
    /// Parameterized queries: same plan for different parameters
    /// </summary>
    [Benchmark(Description = "Parameterized Queries - With Caching")]
    public void ParameterizedQueries_Cached()
    {
        const string baseQuery = "SELECT * FROM users WHERE id = ?";
        var plan = cache.GetOrCreate(baseQuery);
        
        for (int i = 0; i < QueryCount; i++)
        {
            // Reuse same plan for different parameter values
            int userId = i % 1000;
            ExecutePlanWithParameters(plan, userId);
        }
    }

    /// <summary>
    /// Single query repeated many times
    /// Shows maximum cache benefit
    /// </summary>
    [Benchmark(Description = "Repeated Query - Maximum Cache Benefit")]
    public void RepeatedQuery_MaximumBenefit()
    {
        const string repeatedQuery = "SELECT COUNT(*) FROM users";
        
        for (int i = 0; i < QueryCount; i++)
        {
            var plan = cache.GetOrCreate(repeatedQuery);
            ExecutePlan(plan);
        }
    }

    /// <summary>
    /// Helper: Direct query parsing (simulates work)
    /// </summary>
    private static QueryPlan ParseQueryDirectly(string queryText)
    {
        return new QueryPlan
        {
            QueryText = queryText,
            ParsedAt = DateTimeOffset.UtcNow,
            OptimizationLevel = QueryOptimizationLevel.Full
        };
    }

    /// <summary>
    /// Helper: Execute plan
    /// </summary>
    private static void ExecutePlan(QueryPlan plan)
    {
        // Simulate plan execution
    }

    /// <summary>
    /// Helper: Execute with parameters
    /// </summary>
    private static void ExecutePlanWithParameters(QueryPlan plan, int paramValue)
    {
        // Simulate parameterized execution
    }
}

/// <summary>
/// Phase 2D Friday: Cache Statistics Benchmark
/// 
/// Validates cache hit rates and performance metrics.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_CacheStatisticsBenchmark
{
    private QueryPlanCache cache = null!;
    private const int QueryCount = 10000;
    private const int UniqueQueries = 100;

    [GlobalSetup]
    public void Setup()
    {
        cache = new QueryPlanCache(maxCacheSize: UniqueQueries);
    }

    /// <summary>
    /// Warm-up cache and measure hit rates
    /// </summary>
    [Benchmark(Description = "Cache Warm-up - Statistics")]
    public QueryPlanCacheStatistics CacheWarmup_Statistics()
    {
        // First pass: cold cache
        for (int i = 0; i < UniqueQueries; i++)
        {
            string query = $"SELECT * FROM table_{i}";
            cache.GetOrCreate(query);
        }

        var coldStats = cache.GetStatistics();

        // Second pass: warm cache (should see high hit rate)
        for (int i = 0; i < QueryCount - UniqueQueries; i++)
        {
            string query = $"SELECT * FROM table_{i % UniqueQueries}";
            cache.GetOrCreate(query);
        }

        var warmStats = cache.GetStatistics();
        return warmStats;
    }

    /// <summary>
    /// Measure cache hit rate at different workload densities
    /// </summary>
    [Benchmark(Description = "Cache Hit Rate - Varying Density")]
    public double CacheHitRate_VaryingDensity()
    {
        cache.Clear();

        // Test with 50% unique queries
        for (int i = 0; i < QueryCount; i++)
        {
            string query = i % 2 == 0 
                ? $"SELECT * FROM users WHERE id = {i / 2}"
                : $"SELECT * FROM orders WHERE user_id = {i / 2}";
            
            cache.GetOrCreate(query);
        }

        return cache.GetStatistics().HitRate;
    }
}

/// <summary>
/// Phase 2D Friday: Cache vs No-Cache Comparison
/// 
/// Direct comparison of latency with and without caching.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_CacheLatencyBenchmark
{
    private QueryPlanCache cache = null!;
    private List<string> repeatedQueries = null!;
    private const int QueryCount = 100000;

    [GlobalSetup]
    public void Setup()
    {
        cache = new QueryPlanCache();
        repeatedQueries = new List<string>();

        // Create list of repeated queries
        for (int i = 0; i < QueryCount; i++)
        {
            repeatedQueries.Add($"SELECT * FROM users WHERE user_id = {i % 1000}");
        }
    }

    /// <summary>
    /// Baseline: Sequential parsing
    /// </summary>
    [Benchmark(Description = "Baseline - Parse Each Query")]
    public int BaselineParseEachQuery()
    {
        int count = 0;
        foreach (var query in repeatedQueries)
        {
            var plan = ParseQueryDirectly(query);
            count += plan.QueryText.Length;
        }
        return count;
    }

    /// <summary>
    /// Optimized: Cache-based lookup
    /// Expected: 1.5-2x faster
    /// </summary>
    [Benchmark(Description = "Optimized - Cache Lookup")]
    public int OptimizedCacheLookup()
    {
        int count = 0;
        foreach (var query in repeatedQueries)
        {
            var plan = cache.GetOrCreate(query);
            count += plan.QueryText.Length;
        }
        return count;
    }

    private static QueryPlan ParseQueryDirectly(string queryText)
    {
        return new QueryPlan
        {
            QueryText = queryText,
            ParsedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Phase 2D Friday: Concurrent Cache Access
/// 
/// Tests thread-safety under concurrent load.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_ConcurrentCacheBenchmark
{
    private QueryPlanCache cache = null!;
    private const int ThreadCount = 8;
    private const int OperationsPerThread = 10000;
    private const int UniqueQueries = 100;

    [GlobalSetup]
    public void Setup()
    {
        cache = new QueryPlanCache(maxCacheSize: UniqueQueries * 2);
    }

    /// <summary>
    /// Concurrent cache access from multiple threads
    /// </summary>
    [Benchmark(Description = "Cache - Multi-threaded Access")]
    public void ConcurrentCacheAccess()
    {
        var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();

        for (int t = 0; t < ThreadCount; t++)
        {
            int threadId = t;
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    string query = $"SELECT * FROM table_{(threadId * OperationsPerThread + i) % UniqueQueries}";
                    var plan = cache.GetOrCreate(query);
                }
            }));
        }

        System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
    }
}
