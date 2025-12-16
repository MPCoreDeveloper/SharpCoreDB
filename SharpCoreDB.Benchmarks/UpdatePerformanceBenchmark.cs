// <copyright file="UpdatePerformanceBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Detailed benchmarks for UPDATE operations comparing storage modes.
/// Demonstrates the advantage of page-based (in-place) vs columnar (append-based) updates.
/// This is the key differentiator for OLTP workloads.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[MarkdownExporter]
public class UpdatePerformanceBenchmark
{
    private Database? columnarDb;
    private Database? pageBasedDb;
    private string columnarDbPath = "";
    private string pageBasedDbPath = "";
    private const string TestPassword = "test_password_123";

    [Params(100, 1000, 5000)]
    public int TableSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Create temp directories
        columnarDbPath = Path.Combine(Path.GetTempPath(), $"update_columnar_{Guid.NewGuid():N}");
        pageBasedDbPath = Path.Combine(Path.GetTempPath(), $"update_pagebased_{Guid.NewGuid():N}");

        Directory.CreateDirectory(columnarDbPath);
        Directory.CreateDirectory(pageBasedDbPath);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Create service provider
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();

        // Create fresh databases with pre-populated data
        columnarDb?.Dispose();
        pageBasedDb?.Dispose();

        // Columnar database with test data
        columnarDb = new Database(serviceProvider, columnarDbPath, TestPassword, config: DatabaseConfig.Benchmark);
        columnarDb.ExecuteSQL("CREATE TABLE updates (id INTEGER, status TEXT, counter INTEGER, last_update INTEGER)");
        
        for (int i = 0; i < TableSize; i++)
        {
            columnarDb.ExecuteSQL($"INSERT INTO updates VALUES ({i}, 'active', 0, {DateTimeOffset.UtcNow.ToUnixTimeSeconds()})");
        }

        // Page-based database with test data
        pageBasedDb = new Database(serviceProvider, pageBasedDbPath, TestPassword, config: DatabaseConfig.Benchmark);
        pageBasedDb.ExecuteSQL("CREATE TABLE updates (id INTEGER, status TEXT, counter INTEGER, last_update INTEGER) STORAGE = PAGE_BASED");
        
        for (int i = 0; i < TableSize; i++)
        {
            pageBasedDb.ExecuteSQL($"INSERT INTO updates VALUES ({i}, 'active', 0, {DateTimeOffset.UtcNow.ToUnixTimeSeconds()})");
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        columnarDb?.Dispose();
        pageBasedDb?.Dispose();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
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

    // ==================== SINGLE RECORD UPDATE ====================

    /// <summary>
    /// Single UPDATE in columnar storage (append-based).
    /// Expected: Slow - requires append new version + mark old as deleted.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Single UPDATE - Columnar (append)")]
    public void SingleUpdate_Columnar()
    {
        int targetId = TableSize / 2;
        columnarDb!.ExecuteSQL($"UPDATE updates SET counter = counter + 1, last_update = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} WHERE id = {targetId}");
    }

    /// <summary>
    /// Single UPDATE in page-based storage (in-place).
    /// Expected: Fast - direct page modification, no append overhead.
    /// </summary>
    [Benchmark(Description = "Single UPDATE - Page-Based (in-place)")]
    public void SingleUpdate_PageBased()
    {
        int targetId = TableSize / 2;
        pageBasedDb!.ExecuteSQL($"UPDATE updates SET counter = counter + 1, last_update = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} WHERE id = {targetId}");
    }

    // ==================== BATCH UPDATE (10% of table) ====================

    [Benchmark(Description = "Batch UPDATE (10%) - Columnar")]
    public void BatchUpdate_Columnar()
    {
        int updateCount = TableSize / 10;
        for (int i = 0; i < updateCount; i++)
        {
            columnarDb!.ExecuteSQL($"UPDATE updates SET status = 'updated', counter = {i} WHERE id = {i}");
        }
    }

    [Benchmark(Description = "Batch UPDATE (10%) - Page-Based")]
    public void BatchUpdate_PageBased()
    {
        int updateCount = TableSize / 10;
        for (int i = 0; i < updateCount; i++)
        {
            pageBasedDb!.ExecuteSQL($"UPDATE updates SET status = 'updated', counter = {i} WHERE id = {i}");
        }
    }

    // ==================== REPEATED UPDATES (same record) ====================

    /// <summary>
    /// Repeatedly update the same record (worst case for columnar).
    /// Expected: Very slow - creates many versions of same record.
    /// </summary>
    [Benchmark(Description = "Repeated UPDATE (100x) - Columnar")]
    public void RepeatedUpdate_Columnar()
    {
        int targetId = TableSize / 2;
        for (int i = 0; i < 100; i++)
        {
            columnarDb!.ExecuteSQL($"UPDATE updates SET counter = {i} WHERE id = {targetId}");
        }
    }

    /// <summary>
    /// Repeatedly update the same record (best case for page-based).
    /// Expected: Fast - same page, in-place modification.
    /// </summary>
    [Benchmark(Description = "Repeated UPDATE (100x) - Page-Based")]
    public void RepeatedUpdate_PageBased()
    {
        int targetId = TableSize / 2;
        for (int i = 0; i < 100; i++)
        {
            pageBasedDb!.ExecuteSQL($"UPDATE updates SET counter = {i} WHERE id = {targetId}");
        }
    }

    // ==================== UPDATE WITH FULL TABLE SCAN ====================

    [Benchmark(Description = "UPDATE all rows - Columnar")]
    public void UpdateAll_Columnar()
    {
        columnarDb!.ExecuteSQL($"UPDATE updates SET last_update = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
    }

    [Benchmark(Description = "UPDATE all rows - Page-Based")]
    public void UpdateAll_PageBased()
    {
        pageBasedDb!.ExecuteSQL($"UPDATE updates SET last_update = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
    }

    // ==================== UPDATE WITH STRING MODIFICATION ====================

    [Benchmark(Description = "UPDATE string field - Columnar")]
    public void UpdateString_Columnar()
    {
        for (int i = 0; i < Math.Min(50, TableSize); i++)
        {
            columnarDb!.ExecuteSQL($"UPDATE updates SET status = 'modified_status_{i}' WHERE id = {i}");
        }
    }

    [Benchmark(Description = "UPDATE string field - Page-Based")]
    public void UpdateString_PageBased()
    {
        for (int i = 0; i < Math.Min(50, TableSize); i++)
        {
            pageBasedDb!.ExecuteSQL($"UPDATE updates SET status = 'modified_status_{i}' WHERE id = {i}");
        }
    }

    // ==================== UPDATE THROUGHPUT TEST ====================

    [Benchmark(Description = "UPDATE Throughput (1 sec) - Columnar")]
    public int UpdateThroughput_Columnar()
    {
        int count = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.ElapsedMilliseconds < 1000 && count < TableSize)
        {
            columnarDb!.ExecuteSQL($"UPDATE updates SET counter = {count} WHERE id = {count % TableSize}");
            count++;
        }
        
        return count;
    }

    [Benchmark(Description = "UPDATE Throughput (1 sec) - Page-Based")]
    public int UpdateThroughput_PageBased()
    {
        int count = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.ElapsedMilliseconds < 1000 && count < TableSize)
        {
            pageBasedDb!.ExecuteSQL($"UPDATE updates SET counter = {count} WHERE id = {count % TableSize}");
            count++;
        }
        
        return count;
    }

    // ==================== UPDATE WITH INDEX ====================

    [Benchmark(Description = "UPDATE with Index - Columnar")]
    public void UpdateWithIndex_Columnar()
    {
        columnarDb!.ExecuteSQL("CREATE INDEX idx_status ON updates(status)");
        
        for (int i = 0; i < Math.Min(100, TableSize); i++)
        {
            columnarDb!.ExecuteSQL($"UPDATE updates SET counter = {i} WHERE id = {i}");
        }
    }

    [Benchmark(Description = "UPDATE with Index - Page-Based")]
    public void UpdateWithIndex_PageBased()
    {
        pageBasedDb!.ExecuteSQL("CREATE INDEX idx_status ON updates(status)");
        
        for (int i = 0; i < Math.Min(100, TableSize); i++)
        {
            pageBasedDb!.ExecuteSQL($"UPDATE updates SET counter = {i} WHERE id = {i}");
        }
    }

    // ==================== WRITE AMPLIFICATION TEST ====================

    /// <summary>
    /// Measures write amplification for updates.
    /// Columnar: Each update creates new record version (high amplification).
    /// Page-based: In-place update (low amplification).
    /// </summary>
    [Benchmark(Description = "Write Amplification (1000 updates) - Columnar")]
    public long WriteAmplification_Columnar()
    {
        var fileInfo = new FileInfo(Path.Combine(columnarDbPath, "updates.dat"));
        long sizeBefore = fileInfo.Exists ? fileInfo.Length : 0;

        for (int i = 0; i < 1000; i++)
        {
            columnarDb!.ExecuteSQL($"UPDATE updates SET counter = {i} WHERE id = {i % TableSize}");
        }

        fileInfo.Refresh();
        long sizeAfter = fileInfo.Length;
        
        return sizeAfter - sizeBefore; // Bytes written
    }

    [Benchmark(Description = "Write Amplification (1000 updates) - Page-Based")]
    public long WriteAmplification_PageBased()
    {
        var fileInfo = new FileInfo(Path.Combine(pageBasedDbPath, "updates.pages"));
        long sizeBefore = fileInfo.Exists ? fileInfo.Length : 0;

        for (int i = 0; i < 1000; i++)
        {
            pageBasedDb!.ExecuteSQL($"UPDATE updates SET counter = {i} WHERE id = {i % TableSize}");
        }

        fileInfo.Refresh();
        long sizeAfter = fileInfo.Length;
        
        return sizeAfter - sizeBefore; // Bytes written
    }
}
