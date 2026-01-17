using BenchmarkDotNet.Attributes;
using SharpCoreDB;
using SharpCoreDB.Benchmarks.Infrastructure;
using SharpCoreDB.DataStructures;
using System;
using System.Collections.Generic;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2C: ref readonly Optimization Benchmarks
/// 
/// Measures performance improvements from returning references instead of copies.
/// Uses cached dictionary instances to minimize allocations during row materialization.
/// 
/// Expected improvements:
/// - 2-3x faster row materialization
/// - 90% less memory allocation
/// - Zero copy overhead for intermediate operations
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2CRefReadonlyBenchmark
{
    private BenchmarkDatabaseHelper db = null!;
    private const int DATASET_SIZE = 100000;
    private const int ITERATIONS = 1000;  // Simulate hot path

    [GlobalSetup]
    public void Setup()
    {
        // Create test database
        db = new BenchmarkDatabaseHelper(
            "phase2c_refreadonly_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            "testpassword",
            enableEncryption: false);

        // Create test table
        db.CreateUsersTable();

        // Populate dataset
        PopulateTestData(DATASET_SIZE);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        db?.Dispose();
    }

    /// <summary>
    /// Traditional: Each row returns a new Dictionary copy
    /// Simulates current behavior with allocations
    /// </summary>
    [Benchmark(Description = "Row materialization - Traditional (copies)")]
    public int RowMaterialization_Traditional()
    {
        int totalCount = 0;
        
        // Simulate fetching rows one at a time with copies
        for (int i = 0; i < ITERATIONS; i++)
        {
            var row = new Dictionary<string, object>
            {
                { "id", i },
                { "name", $"User{i}" },
                { "email", $"user{i}@test.com" },
                { "age", 20 + (i % 50) },
                { "is_active", i % 2 }
            };
            totalCount += row.Count;
        }
        
        return totalCount / ITERATIONS;
    }

    /// <summary>
    /// Optimized: Returns cached instance, caller copies if needed
    /// Expected: 2-3x faster due to reduced allocations
    /// </summary>
    [Benchmark(Description = "Row materialization - Cached (minimal allocations)")]
    public int RowMaterialization_Cached()
    {
        var columns = new[] { "id", "name", "email", "age", "is_active" };
        var types = new[] { typeof(int), typeof(string), typeof(string), typeof(int), typeof(bool) };
        
        using var materializer = new RowMaterializer(columns, types);
        int totalCount = 0;
        
        // Simulate fetching rows with cached dictionary
        for (int i = 0; i < ITERATIONS; i++)
        {
            // Get cached instance (no allocation for the dictionary itself)
            var row = materializer.MaterializeRow(ReadOnlySpan<byte>.Empty, i);
            totalCount += row.Count;
        }
        
        return totalCount / ITERATIONS;
    }

    /// <summary>
    /// Thread-safe version with lock-based synchronization
    /// Lock is minimal (only during materialization)
    /// Expected: 2-3x faster due to cached instances + reduced critical section
    /// </summary>
    [Benchmark(Description = "Row materialization - Thread-safe cached")]
    public int RowMaterialization_ThreadSafeCached()
    {
        var columns = new[] { "id", "name", "email", "age", "is_active" };
        var types = new[] { typeof(int), typeof(string), typeof(string), typeof(int), typeof(bool) };
        
        using var materializer = new ThreadSafeRowMaterializer(columns, types);
        int totalCount = 0;
        
        for (int i = 0; i < ITERATIONS; i++)
        {
            var row = materializer.MaterializeRowThreadSafe(ReadOnlySpan<byte>.Empty, i);
            totalCount += row.Count;
        }
        
        return totalCount / ITERATIONS;
    }

    private void PopulateTestData(int rowCount)
    {
        var random = new Random(42);

        for (int i = 0; i < rowCount; i++)
        {
            var id = i;
            var name = $"User{i}";
            var email = $"user{i}@test.com";
            var age = 18 + random.Next(65);
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
}

/// <summary>
/// Detailed ref readonly behavior benchmark
/// Isolates the performance characteristics of ref readonly pattern
/// </summary>
[MemoryDiagnoser]
public class Phase2CRefReadonlyDetailedTest
{
    private RowMaterializer materializer = null!;
    private string[] columnNames = null!;
    private Type[] columnTypes = null!;

    [GlobalSetup]
    public void Setup()
    {
        columnNames = new[] { "id", "name", "email", "age", "is_active" };
        columnTypes = new[] { typeof(int), typeof(string), typeof(string), typeof(int), typeof(bool) };
        
        materializer = new RowMaterializer(columnNames, columnTypes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        materializer?.Dispose();
    }

    /// <summary>
    /// Single row materialization - cached pattern
    /// No allocation for the dictionary itself
    /// </summary>
    [Benchmark(Description = "Single row - cached")]
    public int SingleRow_Cached()
    {
        var row = materializer.MaterializeRow(ReadOnlySpan<byte>.Empty, 0);
        return row.Count;
    }

    /// <summary>
    /// Single row with copy - shows traditional overhead
    /// </summary>
    [Benchmark(Description = "Single row - with copy")]
    public int SingleRow_WithCopy()
    {
        var row = materializer.MaterializeRow(ReadOnlySpan<byte>.Empty, 0);
        var copy = new Dictionary<string, object>(row);
        return copy.Count;
    }

    /// <summary>
    /// Batch processing: 100 rows with copies
    /// Shows accumulated benefit of cached instances
    /// Expected: 90% less allocation vs traditional
    /// </summary>
    [Benchmark(Description = "Batch 100 rows - cached with copies")]
    public int Batch100Rows_Cached()
    {
        int totalCount = 0;
        
        for (int i = 0; i < 100; i++)
        {
            // Get cached instance (no allocation)
            var row = materializer.MaterializeRow(ReadOnlySpan<byte>.Empty, i);
            
            // Copy only when needed
            var copy = new Dictionary<string, object>(row);
            totalCount += copy.Count;
        }
        
        return totalCount;
    }

    /// <summary>
    /// Memory impact test: shows reduced allocations
    /// </summary>
    [Benchmark(Description = "Memory impact - 1000 rows")]
    public int Memory_1000Rows()
    {
        var results = new List<Dictionary<string, object>>(1000);
        
        for (int i = 0; i < 1000; i++)
        {
            var row = materializer.MaterializeRow(ReadOnlySpan<byte>.Empty, i);
            results.Add(new Dictionary<string, object>(row));
        }
        
        return results.Count;
    }
}

/// <summary>
/// Concurrent access test: Shows performance with thread-safe wrapper
/// </summary>
[MemoryDiagnoser]
public class Phase2CRefReadonlyConcurrentTest
{
    private ThreadSafeRowMaterializer materializer = null!;

    [GlobalSetup]
    public void Setup()
    {
        var columns = new[] { "id", "name", "email", "age", "is_active" };
        var types = new[] { typeof(int), typeof(string), typeof(string), typeof(int), typeof(bool) };
        
        materializer = new ThreadSafeRowMaterializer(columns, types);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        materializer?.Dispose();
    }

    /// <summary>
    /// Sequential access (single threaded)
    /// Lock overhead is minimal
    /// </summary>
    [Benchmark(Description = "Sequential access - thread-safe cached")]
    public int Sequential_ThreadSafe()
    {
        int totalCount = 0;
        
        for (int i = 0; i < 100; i++)
        {
            var row = materializer.MaterializeRowThreadSafe(ReadOnlySpan<byte>.Empty, i);
            totalCount += row.Count;
        }
        
        return totalCount;
    }

    /// <summary>
    /// Batch access with thread-safe materialization
    /// Shows benefits of cached instances for batches
    /// </summary>
    [Benchmark(Description = "Batch access - thread-safe cached")]
    public int Batch_ThreadSafe()
    {
        var offsets = new int[100];
        for (int i = 0; i < 100; i++)
            offsets[i] = i;
        
        var results = materializer.MaterializeRowsThreadSafe(ReadOnlySpan<byte>.Empty, offsets);
        return results.Count;
    }
}
