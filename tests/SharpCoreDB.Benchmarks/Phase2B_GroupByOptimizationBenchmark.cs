using BenchmarkDotNet.Attributes;
using SharpCoreDB;
using SharpCoreDB.Benchmarks.Infrastructure;
using SharpCoreDB.Execution;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2B: GROUP BY Optimization Benchmarks
/// Measures performance improvements from manual aggregation + SIMD.
/// 
/// Expected improvements:
/// - GROUP BY with aggregation: 1.5-2x faster
/// - Memory allocation: 70% reduction
/// - SIMD: 2-3x for numeric operations
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2BGroupByOptimizationBenchmark
{
    private BenchmarkDatabaseHelper db = null!;
    private AggregationOptimizer optimizer = null!;
    private const int DATASET_SIZE = 100000;
    private const int GROUP_COUNT = 50;

    [GlobalSetup]
    public void Setup()
    {
        // Create test database
        db = new BenchmarkDatabaseHelper(
            "phase2b_groupby_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            "testpassword",
            enableEncryption: false);

        // Create test table
        db.CreateUsersTable();

        // Populate with larger dataset for GROUP BY tests
        PopulateTestData(DATASET_SIZE);

        // Initialize aggregation optimizer
        optimizer = new AggregationOptimizer();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        optimizer?.Dispose();
        db?.Dispose();
    }

    #region GROUP BY Aggregation Tests

    /// <summary>
    /// Baseline: GROUP BY COUNT using standard query
    /// Groups users by age, counts per group
    /// </summary>
    [Benchmark(Description = "GROUP BY COUNT (standard query)")]
    public int GroupByCount_Baseline()
    {
        // This would use internal GROUP BY implementation
        // For this benchmark, we simulate by fetching and using optimizer
        var rows = db.Database.ExecuteQuery("SELECT * FROM users");
        
        var result = optimizer.GroupAndAggregate(rows,
            groupByColumns: ["age"],
            aggregates: new[] {
                new AggregateDefinition(AggregateType.Count)
            });
        
        return result.Count;
    }

    /// <summary>
    /// Optimized: GROUP BY with SUM aggregation
    /// Groups users by age, sums numeric column per group
    /// Expected: 1.5-2x improvement
    /// </summary>
    [Benchmark(Description = "GROUP BY COUNT + SUM (optimized)")]
    public int GroupByCountSum_Optimized()
    {
        var rows = db.Database.ExecuteQuery("SELECT * FROM users");
        
        var result = optimizer.GroupAndAggregate(rows,
            groupByColumns: ["age"],
            aggregates: new[] {
                new AggregateDefinition(AggregateType.Count),
                new AggregateDefinition(AggregateType.Sum, "id")
            });
        
        return result.Count;
    }

    /// <summary>
    /// Multiple GROUP BY columns with multiple aggregates
    /// Groups by age AND is_active, computes COUNT, SUM, AVG
    /// Tests complex aggregation performance
    /// </summary>
    [Benchmark(Description = "GROUP BY multiple columns (COUNT+SUM+AVG)")]
    public int GroupByMultipleColumns()
    {
        var rows = db.Database.ExecuteQuery("SELECT * FROM users");
        
        var result = optimizer.GroupAndAggregate(rows,
            groupByColumns: ["age", "is_active"],
            aggregates: new[] {
                new AggregateDefinition(AggregateType.Count),
                new AggregateDefinition(AggregateType.Sum, "id"),
                new AggregateDefinition(AggregateType.Average, "id")
            });
        
        return result.Count;
    }

    #endregion

    #region SIMD Numeric Aggregation Tests

    /// <summary>
    /// Scalar SUM aggregation (baseline)
    /// Processes array sequentially
    /// </summary>
    [Benchmark(Description = "SUM scalar loop (baseline)")]
    public double SumScalarLoop()
    {
        var values = GenerateNumericArray(10000);
        
        double sum = 0;
        foreach (var val in values)
        {
            sum += val;
        }
        
        return sum;
    }

    /// <summary>
    /// SIMD-optimized SUM aggregation
    /// Uses Vector<double> to process 4 values at once
    /// Expected: 2-3x faster
    /// </summary>
    [Benchmark(Description = "SUM SIMD optimized (Vector<T>)")]
    public double SumSIMD()
    {
        var values = GenerateNumericArray(10000);
        return AggregationOptimizer.SumWithSIMD(values);
    }

    #endregion

    #region Memory Allocation Tests

    /// <summary>
    /// Measures memory allocation during GROUP BY with aggregation
    /// Single-pass algorithm should allocate minimal memory
    /// </summary>
    [Benchmark(Description = "GROUP BY memory allocation")]
    public int GroupByMemoryTest()
    {
        var rows = db.Database.ExecuteQuery("SELECT * FROM users");
        
        // Measure allocations during aggregation
        var result = optimizer.GroupAndAggregate(rows,
            groupByColumns: ["age"],
            aggregates: new[] {
                new AggregateDefinition(AggregateType.Count),
                new AggregateDefinition(AggregateType.Sum, "id")
            });
        
        return result.Count;
    }

    #endregion

    #region Cache Effectiveness Tests

    /// <summary>
    /// Tests cache effectiveness when grouping by same column repeatedly
    /// Second GROUP BY should have faster key lookups (cached)
    /// </summary>
    [Benchmark(Description = "Repeated GROUP BY (cache effectiveness)")]
    public int RepeatedGroupBy()
    {
        var rows = db.Database.ExecuteQuery("SELECT * FROM users");
        
        int totalGroups = 0;
        
        // First GROUP BY - caches string keys
        var result1 = optimizer.GroupAndAggregate(rows,
            groupByColumns: ["age"],
            aggregates: new[] { new AggregateDefinition(AggregateType.Count) });
        totalGroups += result1.Count;
        
        // Second GROUP BY - keys are cached
        var result2 = optimizer.GroupAndAggregate(rows,
            groupByColumns: ["age"],
            aggregates: new[] { new AggregateDefinition(AggregateType.Count) });
        totalGroups += result2.Count;
        
        return totalGroups;
    }

    #endregion

    #region Helper Methods

    private void PopulateTestData(int rowCount)
    {
        var random = new Random(42);

        for (int i = 0; i < rowCount; i++)
        {
            var id = i;
            var name = $"User{i}";
            var email = $"user{i}@test.com";
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
                continue;
            }
        }
    }

    private double[] GenerateNumericArray(int size)
    {
        var array = new double[size];
        var random = new Random(42);
        
        for (int i = 0; i < size; i++)
        {
            array[i] = random.NextDouble() * 1000;
        }
        
        return array;
    }

    #endregion
}

/// <summary>
/// Detailed GROUP BY aggregation behavior benchmark
/// Isolates aggregation performance metrics
/// </summary>
[MemoryDiagnoser]
public class AggregationOptimizerDetailedTest
{
    private AggregationOptimizer optimizer = null!;
    private List<Dictionary<string, object>>? testRows;

    [GlobalSetup]
    public void Setup()
    {
        optimizer = new AggregationOptimizer();
        testRows = GenerateTestRows(10000, 50);  // 10k rows, 50 groups
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        optimizer?.Dispose();
    }

    /// <summary>
    /// Benchmark: Single-column GROUP BY with COUNT
    /// Simplest aggregation case
    /// </summary>
    [Benchmark(Description = "GROUP BY single column (COUNT)")]
    public int GroupBySingleColumnCount()
    {
        var result = optimizer.GroupAndAggregate(testRows!,
            groupByColumns: ["Category"],
            aggregates: new[] {
                new AggregateDefinition(AggregateType.Count)
            });
        
        return result.Count;
    }

    /// <summary>
    /// Benchmark: GROUP BY with COUNT + SUM + AVG
    /// Complex aggregation case
    /// </summary>
    [Benchmark(Description = "GROUP BY single column (COUNT+SUM+AVG)")]
    public int GroupBySingleColumnMultipleAgg()
    {
        var result = optimizer.GroupAndAggregate(testRows!,
            groupByColumns: ["Category"],
            aggregates: new[] {
                new AggregateDefinition(AggregateType.Count),
                new AggregateDefinition(AggregateType.Sum, "Amount"),
                new AggregateDefinition(AggregateType.Average, "Amount")
            });
        
        return result.Count;
    }

    /// <summary>
    /// Benchmark: GROUP BY multiple columns
    /// More complex grouping
    /// </summary>
    [Benchmark(Description = "GROUP BY multiple columns")]
    public int GroupByMultipleColumns()
    {
        var result = optimizer.GroupAndAggregate(testRows!,
            groupByColumns: ["Category", "Status"],
            aggregates: new[] {
                new AggregateDefinition(AggregateType.Count),
                new AggregateDefinition(AggregateType.Sum, "Amount")
            });
        
        return result.Count;
    }

    private List<Dictionary<string, object>> GenerateTestRows(int count, int groupCount)
    {
        var rows = new List<Dictionary<string, object>>(count);
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            rows.Add(new Dictionary<string, object>
            {
                ["Id"] = i,
                ["Category"] = $"Category{i % groupCount}",
                ["Status"] = i % 2 == 0 ? "Active" : "Inactive",
                ["Amount"] = random.NextDouble() * 1000
            });
        }

        return rows;
    }
}
