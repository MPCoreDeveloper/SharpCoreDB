namespace SharpCoreDB.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SharpCoreDB;
using SharpCoreDB.DataStructures;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// ✅ Phase 5: Performance Benchmarks for Collation-Aware Query Operations
/// 
/// Measures performance of:
/// - WHERE clause filtering with different collations
/// - DISTINCT deduplication with collation
/// - GROUP BY with collation
/// - ORDER BY with collation
/// 
/// Goals:
/// - Binary collation: zero overhead vs non-collation path
/// - NoCase: &lt;5% overhead vs binary
/// - Large datasets (100K rows): consistent performance
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class Phase5_CollationQueryPerformanceBenchmark
{
    private Table _table = null!;
    private List<Dictionary<string, object>> _rows1K = null!;
    private List<Dictionary<string, object>> _rows10K = null!;
    private List<Dictionary<string, object>> _rows100K = null!;

    [GlobalSetup]
    public void Setup()
    {
        _table = CreateTable(CollationType.NoCase);
        _rows1K = GenerateTestRows(1000);
        _rows10K = GenerateTestRows(10_000);
        _rows100K = GenerateTestRows(100_000);
    }

    /// <summary>
    /// Benchmark: WHERE clause filtering with 1K rows, Binary collation.
    /// ✅ Baseline for performance comparison.
    /// </summary>
    [Benchmark]
    public int WhereClauseFiltering_1K_Binary()
    {
        var table = CreateTable(CollationType.Binary);
        var count = 0;
        foreach (var row in _rows1K)
        {
            if (table.EvaluateConditionWithCollation(row, "email", "=", "alice@example.com"))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: WHERE clause filtering with 1K rows, NoCase collation.
    /// ✅ Measures overhead of collation-aware comparison.
    /// </summary>
    [Benchmark]
    public int WhereClauseFiltering_1K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var count = 0;
        foreach (var row in _rows1K)
        {
            if (table.EvaluateConditionWithCollation(row, "email", "=", "alice@example.com"))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: WHERE clause filtering with 10K rows, NoCase collation.
    /// ✅ Verifies scalability of collation comparison.
    /// </summary>
    [Benchmark]
    public int WhereClauseFiltering_10K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var count = 0;
        foreach (var row in _rows10K)
        {
            if (table.EvaluateConditionWithCollation(row, "email", "=", "alice@example.com"))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: WHERE clause filtering with 100K rows, NoCase collation.
    /// ✅ Tests performance on large datasets.
    /// </summary>
    [Benchmark]
    public int WhereClauseFiltering_100K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var count = 0;
        foreach (var row in _rows100K)
        {
            if (table.EvaluateConditionWithCollation(row, "email", "=", "alice@example.com"))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: DISTINCT deduplication with 1K rows, NoCase collation.
    /// ✅ Measures HashSet allocation and comparison overhead.
    /// </summary>
    [Benchmark]
    public int DistinctOperation_1K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var result = table.ApplyDistinctWithCollation(_rows1K, "email");
        return result.Count;
    }

    /// <summary>
    /// Benchmark: DISTINCT deduplication with 10K rows, NoCase collation.
    /// ✅ Tests HashSet performance at scale.
    /// </summary>
    [Benchmark]
    public int DistinctOperation_10K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var result = table.ApplyDistinctWithCollation(_rows10K, "email");
        return result.Count;
    }

    /// <summary>
    /// Benchmark: DISTINCT deduplication with 100K rows, NoCase collation.
    /// ✅ Extreme case: memory usage and GC impact.
    /// </summary>
    [Benchmark]
    public int DistinctOperation_100K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var result = table.ApplyDistinctWithCollation(_rows100K, "email");
        return result.Count;
    }

    /// <summary>
    /// Benchmark: GROUP BY operation with 1K rows, NoCase collation.
    /// ✅ Measures dictionary allocation and grouping.
    /// </summary>
    [Benchmark]
    public int GroupByOperation_1K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var groups = table.GroupByWithCollation(_rows1K, "status");
        return groups.Count;
    }

    /// <summary>
    /// Benchmark: GROUP BY operation with 10K rows, NoCase collation.
    /// ✅ Tests dictionary performance at scale.
    /// </summary>
    [Benchmark]
    public int GroupByOperation_10K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var groups = table.GroupByWithCollation(_rows10K, "status");
        return groups.Count;
    }

    /// <summary>
    /// Benchmark: GROUP BY operation with 100K rows, NoCase collation.
    /// ✅ Extreme case: memory allocation for large groups.
    /// </summary>
    [Benchmark]
    public int GroupByOperation_100K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var groups = table.GroupByWithCollation(_rows100K, "status");
        return groups.Count;
    }

    /// <summary>
    /// Benchmark: ORDER BY operation with 1K rows, Binary collation.
    /// ✅ Baseline for sorting performance.
    /// </summary>
    [Benchmark]
    public int OrderByOperation_1K_Binary()
    {
        var table = CreateTable(CollationType.Binary);
        var sorted = table.OrderByWithCollation(_rows1K, "email", ascending: true);
        return sorted.Count;
    }

    /// <summary>
    /// Benchmark: ORDER BY operation with 1K rows, NoCase collation.
    /// ✅ Measures sorting overhead with collation.
    /// </summary>
    [Benchmark]
    public int OrderByOperation_1K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var sorted = table.OrderByWithCollation(_rows1K, "email", ascending: true);
        return sorted.Count;
    }

    /// <summary>
    /// Benchmark: ORDER BY operation with 10K rows, NoCase collation.
    /// ✅ Tests O(n log n) sorting at scale.
    /// </summary>
    [Benchmark]
    public int OrderByOperation_10K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var sorted = table.OrderByWithCollation(_rows10K, "email", ascending: true);
        return sorted.Count;
    }

    /// <summary>
    /// Benchmark: ORDER BY operation with 100K rows, NoCase collation.
    /// ✅ Extreme case: sorting large dataset with collation.
    /// </summary>
    [Benchmark]
    public int OrderByOperation_100K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var sorted = table.OrderByWithCollation(_rows100K, "email", ascending: true);
        return sorted.Count;
    }

    /// <summary>
    /// Benchmark: LIKE pattern matching with 1K rows, NoCase collation.
    /// ✅ Measures regex-free wildcard matching.
    /// </summary>
    [Benchmark]
    public int LikePatternMatching_1K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var count = 0;
        foreach (var row in _rows1K)
        {
            if (table.EvaluateConditionWithCollation(row, "email", "LIKE", "%example%"))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: LIKE pattern matching with 10K rows, NoCase collation.
    /// ✅ Tests wildcard matching at scale.
    /// </summary>
    [Benchmark]
    public int LikePatternMatching_10K_NoCase()
    {
        var table = CreateTable(CollationType.NoCase);
        var count = 0;
        foreach (var row in _rows10K)
        {
            if (table.EvaluateConditionWithCollation(row, "email", "LIKE", "%example%"))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: Complex query with multiple conditions and collation.
    /// ✅ Realistic scenario: WHERE email = 'X' AND status = 'Y' with collations.
    /// </summary>
    [Benchmark]
    public int ComplexQuery_1K_MultipleConditions()
    {
        var table = CreateTable(CollationType.NoCase);
        var count = 0;
        foreach (var row in _rows1K)
        {
            if (table.EvaluateConditionWithCollation(row, "email", "=", "alice@example.com") &&
                table.EvaluateConditionWithCollation(row, "status", "=", "ACTIVE"))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: CollationComparator.Equals() direct call.
    /// ✅ Micro-benchmark for equality comparison performance.
    /// </summary>
    [Benchmark]
    public bool CollationComparator_Equals_NoCase_100K()
    {
        bool result = false;
        for (int i = 0; i < 100_000; i++)
        {
            result = CollationComparator.Equals("alice@example.com", "ALICE@EXAMPLE.COM", CollationType.NoCase);
        }
        return result;
    }

    /// <summary>
    /// Benchmark: CollationComparator.Equals() with Binary collation.
    /// ✅ Baseline: should be very fast (ordinal comparison).
    /// </summary>
    [Benchmark]
    public bool CollationComparator_Equals_Binary_100K()
    {
        bool result = false;
        for (int i = 0; i < 100_000; i++)
        {
            result = CollationComparator.Equals("alice@example.com", "alice@example.com", CollationType.Binary);
        }
        return result;
    }

    // ==================== HELPERS ====================

    private static Table CreateTable(CollationType emailCollation)
    {
        var table = new Table();
        table.Name = "users";
        table.Columns = ["id", "email", "name", "status"];
        table.ColumnTypes = [DataType.Integer, DataType.String, DataType.String, DataType.String];
        table.ColumnCollations = [CollationType.Binary, emailCollation, CollationType.Binary, CollationType.NoCase];
        table.PrimaryKeyIndex = 0;
        return table;
    }

    private static List<Dictionary<string, object>> GenerateTestRows(int count)
    {
        var rows = new List<Dictionary<string, object>>();
        var emails = new[] 
        { 
            "alice@example.com", "ALICE@EXAMPLE.COM", "alice@other.com",
            "bob@example.com", "BOB@EXAMPLE.COM",
            "charlie@example.com", "CHARLIE@EXAMPLE.COM"
        };
        var statuses = new[] { "active", "ACTIVE", "inactive", "INACTIVE", "pending", "PENDING" };

        for (int i = 0; i < count; i++)
        {
            rows.Add(new Dictionary<string, object>
            {
                { "id", i },
                { "email", emails[i % emails.Length] },
                { "name", $"User{i}" },
                { "status", statuses[i % statuses.Length] }
            });
        }

        return rows;
    }
}
