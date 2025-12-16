// <copyright file="HybridStorageBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Storage.Hybrid;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks comparing columnar vs page-based storage modes.
/// Tests INSERT, UPDATE, DELETE, and SELECT operations across both modes.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[MarkdownExporter]
public class HybridStorageBenchmark
{
    private Database? columnarDb;
    private Database? pageBasedDb;
    private string columnarDbPath = "";
    private string pageBasedDbPath = "";
    private const string TestPassword = "test_password_123";

    /// <summary>
    /// Gets or sets the number of records for benchmarks.
    /// </summary>
    [Params(100, 1000, 10000)]
    public int RecordCount { get; set; }

    /// <summary>
    /// Global setup - creates databases and tables.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        // Create temp directories
        columnarDbPath = Path.Combine(Path.GetTempPath(), $"benchmark_columnar_{Guid.NewGuid():N}");
        pageBasedDbPath = Path.Combine(Path.GetTempPath(), $"benchmark_pagebased_{Guid.NewGuid():N}");

        Directory.CreateDirectory(columnarDbPath);
        Directory.CreateDirectory(pageBasedDbPath);

        // Create service provider
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();

        // Create columnar database
        columnarDb = new Database(serviceProvider, columnarDbPath, TestPassword, config: DatabaseConfig.Benchmark);
        columnarDb.ExecuteSQL("CREATE TABLE test_data (id INTEGER, name TEXT, value REAL, timestamp INTEGER)");
        // Note: Default is COLUMNAR mode

        // Create page-based database
        pageBasedDb = new Database(serviceProvider, pageBasedDbPath, TestPassword, config: DatabaseConfig.Benchmark);
        pageBasedDb.ExecuteSQL("CREATE TABLE test_data (id INTEGER, name TEXT, value REAL, timestamp INTEGER) STORAGE = PAGE_BASED");
    }

    /// <summary>
    /// Global cleanup - disposes databases and deletes temp files.
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        columnarDb?.Dispose();
        pageBasedDb?.Dispose();

        try
        {
            if (Directory.Exists(columnarDbPath))
                Directory.Delete(columnarDbPath, recursive: true);
            
            if (Directory.Exists(pageBasedDbPath))
                Directory.Delete(pageBasedDbPath, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    // ==================== INSERT BENCHMARKS ====================

    /// <summary>
    /// Baseline: Bulk INSERT into columnar storage.
    /// Expected: Fastest for bulk inserts (append-only).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Bulk INSERT - Columnar")]
    public void BulkInsert_Columnar()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            columnarDb!.ExecuteSQL($"INSERT INTO test_data VALUES ({i}, 'User_{i}', {i * 1.5}, {DateTimeOffset.UtcNow.ToUnixTimeSeconds()})");
        }
    }

    /// <summary>
    /// Bulk INSERT into page-based storage.
    /// Expected: Good performance, slight overhead from page allocation.
    /// </summary>
    [Benchmark(Description = "Bulk INSERT - Page-Based")]
    public void BulkInsert_PageBased()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            pageBasedDb!.ExecuteSQL($"INSERT INTO test_data VALUES ({i}, 'User_{i}', {i * 1.5}, {DateTimeOffset.UtcNow.ToUnixTimeSeconds()})");
        }
    }

    /// <summary>
    /// Single INSERT into columnar storage.
    /// Expected: OK performance (sequential append).
    /// </summary>
    [Benchmark(Description = "Single INSERT - Columnar")]
    public void SingleInsert_Columnar()
    {
        columnarDb!.ExecuteSQL($"INSERT INTO test_data VALUES ({RecordCount}, 'SingleUser', 99.9, {DateTimeOffset.UtcNow.ToUnixTimeSeconds()})");
    }

    /// <summary>
    /// Single INSERT into page-based storage.
    /// Expected: Fast (optimized for OLTP).
    /// </summary>
    [Benchmark(Description = "Single INSERT - Page-Based")]
    public void SingleInsert_PageBased()
    {
        pageBasedDb!.ExecuteSQL($"INSERT INTO test_data VALUES ({RecordCount}, 'SingleUser', 99.9, {DateTimeOffset.UtcNow.ToUnixTimeSeconds()})");
    }

    // ==================== UPDATE BENCHMARKS ====================

    /// <summary>
    /// UPDATE in columnar storage.
    /// Expected: Slow (requires append + tombstone).
    /// </summary>
    [Benchmark(Description = "UPDATE - Columnar (append-based)")]
    public void Update_Columnar()
    {
        columnarDb!.ExecuteSQL($"UPDATE test_data SET value = 999.9 WHERE id = {RecordCount / 2}");
    }

    /// <summary>
    /// UPDATE in page-based storage.
    /// Expected: Fast (in-place update).
    /// </summary>
    [Benchmark(Description = "UPDATE - Page-Based (in-place)")]
    public void Update_PageBased()
    {
        pageBasedDb!.ExecuteSQL($"UPDATE test_data SET value = 999.9 WHERE id = {RecordCount / 2}");
    }

    // ==================== DELETE BENCHMARKS ====================

    /// <summary>
    /// DELETE in columnar storage.
    /// Expected: Slow (tombstone marking).
    /// </summary>
    [Benchmark(Description = "DELETE - Columnar (tombstone)")]
    public void Delete_Columnar()
    {
        columnarDb!.ExecuteSQL($"DELETE FROM test_data WHERE id = {RecordCount - 1}");
    }

    /// <summary>
    /// DELETE in page-based storage.
    /// Expected: Fast (slot reclamation).
    /// </summary>
    [Benchmark(Description = "DELETE - Page-Based (slot reclaim)")]
    public void Delete_PageBased()
    {
        pageBasedDb!.ExecuteSQL($"DELETE FROM test_data WHERE id = {RecordCount - 1}");
    }

    // ==================== SELECT BENCHMARKS ====================

    /// <summary>
    /// Full table scan in columnar storage.
    /// Expected: Very fast (sequential read, columnar advantage).
    /// </summary>
    [Benchmark(Description = "SELECT * - Columnar (full scan)")]
    public int SelectAll_Columnar()
    {
        var results = columnarDb!.ExecuteQuery("SELECT * FROM test_data");
        return results.Count;
    }

    /// <summary>
    /// Full table scan in page-based storage.
    /// Expected: Good (sequential page reads).
    /// </summary>
    [Benchmark(Description = "SELECT * - Page-Based (full scan)")]
    public int SelectAll_PageBased()
    {
        var results = pageBasedDb!.ExecuteQuery("SELECT * FROM test_data");
        return results.Count;
    }

    /// <summary>
    /// Point query in columnar storage.
    /// Expected: Fast (hash index lookup).
    /// </summary>
    [Benchmark(Description = "SELECT WHERE - Columnar (index)")]
    public int SelectWhere_Columnar()
    {
        var results = columnarDb!.ExecuteQuery($"SELECT * FROM test_data WHERE id = {RecordCount / 2}");
        return results.Count;
    }

    /// <summary>
    /// Point query in page-based storage.
    /// Expected: Very fast (B-tree index + direct page access).
    /// </summary>
    [Benchmark(Description = "SELECT WHERE - Page-Based (B-tree)")]
    public int SelectWhere_PageBased()
    {
        var results = pageBasedDb!.ExecuteQuery($"SELECT * FROM test_data WHERE id = {RecordCount / 2}");
        return results.Count;
    }

    /// <summary>
    /// Column projection in columnar storage.
    /// Expected: Very fast (columnar advantage).
    /// </summary>
    [Benchmark(Description = "SELECT column - Columnar (projection)")]
    public int SelectColumn_Columnar()
    {
        var results = columnarDb!.ExecuteQuery("SELECT name FROM test_data");
        return results.Count;
    }

    /// <summary>
    /// Column projection in page-based storage.
    /// Expected: OK (needs to read full rows).
    /// </summary>
    [Benchmark(Description = "SELECT column - Page-Based")]
    public int SelectColumn_PageBased()
    {
        var results = pageBasedDb!.ExecuteQuery("SELECT name FROM test_data");
        return results.Count;
    }

    // ==================== AGGREGATION BENCHMARKS ====================

    /// <summary>
    /// COUNT(*) aggregation in columnar storage.
    /// Expected: Very fast (optimized for analytics).
    /// </summary>
    [Benchmark(Description = "COUNT(*) - Columnar (analytics)")]
    public int Count_Columnar()
    {
        var results = columnarDb!.ExecuteQuery("SELECT COUNT(*) FROM test_data");
        return Convert.ToInt32(results[0]["COUNT(*)"]);
    }

    /// <summary>
    /// COUNT(*) aggregation in page-based storage.
    /// Expected: OK (page-by-page count).
    /// </summary>
    [Benchmark(Description = "COUNT(*) - Page-Based")]
    public int Count_PageBased()
    {
        var results = pageBasedDb!.ExecuteQuery("SELECT COUNT(*) FROM test_data");
        return Convert.ToInt32(results[0]["COUNT(*)"]);
    }
}
