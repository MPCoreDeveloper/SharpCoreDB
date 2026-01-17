using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SharpCoreDB;
using SharpCoreDB.Benchmarks.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2A Optimization Benchmarks
/// 
/// IMPORTANT: These benchmarks measure specific optimizations:
/// - WHERE caching: Compilation is cached, but results still materialized
/// - SELECT* path: Zero-copy StructRow vs Dictionary materialization
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2AOptimizationBenchmark
{
    private BenchmarkDatabaseHelper db = null!;
    private const int DATASET_SIZE = 10000;

    [GlobalSetup]
    public void Setup()
    {
        // Create test database with benchmark configuration
        db = new BenchmarkDatabaseHelper(
            "phase2a_benchmark_" + Guid.NewGuid().ToString("N").Substring(0, 8), 
            "testpassword", 
            enableEncryption: false);
        
        // Create test table
        db.CreateUsersTable();
        
        // Populate data ONCE in GlobalSetup
        PopulateTestDataOnce();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        db?.Dispose();
    }

    #region Monday-Tuesday: WHERE Clause Caching Benchmarks

    /// <summary>
    /// Baseline: Single WHERE query (no cache benefit yet).
    /// This is the first execution where compilation happens.
    /// </summary>
    [Benchmark(Description = "WHERE single query (baseline, no cache)")]
    public int WhereCaching_SingleQuery()
    {
        // Execute WHERE once - this compiles and caches the predicate
        var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age > 25");
        return result.Count;
    }

    /// <summary>
    /// WHERE repeated 10x - tests cache effectiveness.
    /// After first execution, predicate should be cached.
    /// Expected: Similar performance to first query (filtering still needed).
    /// Cache benefit: Reduced compilation overhead.
    /// </summary>
    [Benchmark(Description = "WHERE repeated 10x (cache benefits)")]
    public int WhereCaching_Repeated10()
    {
        int totalCount = 0;
        for (int i = 0; i < 10; i++)
        {
            var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age > 25");
            totalCount += result.Count;
        }
        return totalCount;
    }

    /// <summary>
    /// Different WHERE clause - tests cache separation.
    /// Different predicate = new cache entry.
    /// Verifies cache works correctly for different queries.
    /// </summary>
    [Benchmark(Description = "WHERE different clause (tests cache isolation)")]
    public int WhereCaching_DifferentClause()
    {
        int totalCount = 0;
        
        // Query 1: age > 25 (may use cached predicate)
        var result1 = db.Database.ExecuteQuery("SELECT * FROM users WHERE age > 25");
        totalCount += result1.Count;
        
        // Query 2: age < 40 (different predicate = new cache entry)
        var result2 = db.Database.ExecuteQuery("SELECT * FROM users WHERE age < 40");
        totalCount += result2.Count;
        
        return totalCount;
    }

    #endregion

    #region Wednesday: SELECT* StructRow Fast Path Benchmarks

    /// <summary>
    /// SELECT * using traditional Dictionary path (baseline).
    /// Each row materializes to Dictionary<string, object>.
    /// Expected: ~200 bytes per row overhead
    /// </summary>
    [Benchmark(Description = "SELECT * Dictionary path (baseline)")]
    public int SelectDictionary_Path()
    {
        var result = db.Database.ExecuteQuery("SELECT * FROM users");
        return result.Count;
    }

    /// <summary>
    /// SELECT * using fast path with StructRow (optimized).
    /// Uses zero-copy StructRow instead of Dictionary materialization.
    /// Expected: 2-3x faster, 25x less memory
    /// </summary>
    [Benchmark(Description = "SELECT * StructRow fast path (optimized)")]
    public int SelectStructRow_FastPath()
    {
        var result = db.Database.ExecuteQueryFast("SELECT * FROM users");
        return result.Count;
    }

    #endregion

    #region Helper Methods

    private void PopulateTestDataOnce()
    {
        var random = new Random(42);
        
        for (int i = 0; i < DATASET_SIZE; i++)
        {
            var id = i;
            var name = $"User{id}";
            var email = $"user{id}@test.com";
            var age = 20 + random.Next(50);
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
                continue;
            }
        }
    }

    #endregion
}
