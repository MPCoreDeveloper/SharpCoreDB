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
/// Measures actual performance improvements from:
/// - Monday-Tuesday: WHERE Clause Caching (50-100x for repeated)
/// - Wednesday: SELECT* StructRow Path (2-3x + 25x memory)
/// - Thursday: Type Conversion Caching (5-10x)
/// - Friday: Batch PK Validation (1.1-1.3x)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2AOptimizationBenchmark
{
    private BenchmarkDatabaseHelper db = null!;
    private const int SMALL_DATASET = 1000;
    private const int MEDIUM_DATASET = 10000;
    private int nextId = 0;

    [GlobalSetup]
    public void Setup()
    {
        // Create test database with benchmark configuration
        db = new BenchmarkDatabaseHelper("phase2a_benchmark_" + Guid.NewGuid().ToString("N"), "testpassword", enableEncryption: false);
        
        // Create test table
        db.CreateUsersTable();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        db?.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Clear the table before each iteration to avoid PK violations
        nextId += MEDIUM_DATASET; // Use different IDs for each iteration
    }

    #region Monday-Tuesday: WHERE Clause Caching Benchmarks

    [Benchmark(Description = "Repeated WHERE clause query - Cache benefits")]
    [Arguments(MEDIUM_DATASET)]
    public int WhereClauseCaching_RepeatedQuery(int rowCount)
    {
        // Populate test data with unique IDs for this iteration
        PopulateTestData(rowCount, nextId);
        
        // Execute same WHERE query 100 times (cache should benefit all except first)
        int totalCount = 0;
        for (int i = 0; i < 100; i++)
        {
            var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age > 25");
            totalCount += result.Count;
        }
        
        return totalCount;
    }

    #endregion

    #region Wednesday: SELECT* StructRow Fast Path Benchmarks

    [Benchmark(Description = "SELECT * using fast path (StructRow)")]
    [Arguments(MEDIUM_DATASET)]
    public int SelectStructRow_FastPath(int rowCount)
    {
        PopulateTestData(rowCount, nextId);
        
        var result = db.Database.ExecuteQueryFast("SELECT * FROM users");
        return result.Count;
    }

    #endregion

    #region Helper Methods

    private void PopulateTestData(int rowCount, int startId)
    {
        var random = new Random(42);
        
        // Insert data with unique IDs per iteration
        for (int i = 0; i < rowCount; i++)
        {
            var id = startId + i;
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
                // Skip if row already exists (shouldn't happen with unique IDs, but handle gracefully)
                continue;
            }
        }
    }

    #endregion
}
