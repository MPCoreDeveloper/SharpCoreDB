// <copyright file="InsertPerformanceBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Detailed benchmarks for INSERT operations comparing storage modes.
/// Tests various insert patterns: sequential, random, batch, and concurrent.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[MarkdownExporter]
public class InsertPerformanceBenchmark
{
    private Database? columnarDb;
    private Database? pageBasedDb;
    private string columnarDbPath = "";
    private string pageBasedDbPath = "";
    private const string TestPassword = "test_password_123";
    private List<(int id, string name, double value)> testData = new();

    [Params(1000, 10000)]
    public int InsertCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Create temp directories
        columnarDbPath = Path.Combine(Path.GetTempPath(), $"insert_columnar_{Guid.NewGuid():N}");
        pageBasedDbPath = Path.Combine(Path.GetTempPath(), $"insert_pagebased_{Guid.NewGuid():N}");

        Directory.CreateDirectory(columnarDbPath);
        Directory.CreateDirectory(pageBasedDbPath);

        // Pre-generate test data
        testData.Clear();
        for (int i = 0; i < InsertCount; i++)
        {
            testData.Add((i, $"User_{i}", i * 1.5));
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Create service provider
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();

        // Create fresh databases for each iteration
        columnarDb?.Dispose();
        pageBasedDb?.Dispose();

        columnarDb = new Database(serviceProvider, columnarDbPath, TestPassword, config: DatabaseConfig.Benchmark);
        columnarDb.ExecuteSQL("CREATE TABLE inserts (id INTEGER, name TEXT, value REAL)");

        pageBasedDb = new Database(serviceProvider, pageBasedDbPath, TestPassword, config: DatabaseConfig.Benchmark);
        pageBasedDb.ExecuteSQL("CREATE TABLE inserts (id INTEGER, name TEXT, value REAL) STORAGE = PAGE_BASED");
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

    // ==================== SEQUENTIAL INSERT ====================

    [Benchmark(Baseline = true, Description = "Sequential INSERT - Columnar")]
    public void SequentialInsert_Columnar()
    {
        for (int i = 0; i < InsertCount; i++)
        {
            var (id, name, value) = testData[i];
            columnarDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
        }
    }

    [Benchmark(Description = "Sequential INSERT - Page-Based")]
    public void SequentialInsert_PageBased()
    {
        for (int i = 0; i < InsertCount; i++)
        {
            var (id, name, value) = testData[i];
            pageBasedDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
        }
    }

    // ==================== BATCH INSERT (10 RECORDS) ====================

    [Benchmark(Description = "Batch INSERT (10) - Columnar")]
    public void BatchInsert10_Columnar()
    {
        for (int batch = 0; batch < InsertCount / 10; batch++)
        {
            for (int i = 0; i < 10; i++)
            {
                int idx = batch * 10 + i;
                var (id, name, value) = testData[idx];
                columnarDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
            }
        }
    }

    [Benchmark(Description = "Batch INSERT (10) - Page-Based")]
    public void BatchInsert10_PageBased()
    {
        for (int batch = 0; batch < InsertCount / 10; batch++)
        {
            for (int i = 0; i < 10; i++)
            {
                int idx = batch * 10 + i;
                var (id, name, value) = testData[idx];
                pageBasedDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
            }
        }
    }

    // ==================== RANDOM ORDER INSERT ====================

    [Benchmark(Description = "Random INSERT - Columnar")]
    public void RandomInsert_Columnar()
    {
        var shuffled = testData.OrderBy(x => Guid.NewGuid()).ToList();
        foreach (var (id, name, value) in shuffled)
        {
            columnarDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
        }
    }

    [Benchmark(Description = "Random INSERT - Page-Based")]
    public void RandomInsert_PageBased()
    {
        var shuffled = testData.OrderBy(x => Guid.NewGuid()).ToList();
        foreach (var (id, name, value) in shuffled)
        {
            pageBasedDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
        }
    }

    // ==================== INSERT WITH LARGE STRINGS ====================

    [Benchmark(Description = "Large String INSERT - Columnar")]
    public void LargeStringInsert_Columnar()
    {
        string largeText = new string('X', 1000); // 1KB text
        for (int i = 0; i < Math.Min(100, InsertCount); i++)
        {
            columnarDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({i}, '{largeText}_{i}', {i * 1.5})");
        }
    }

    [Benchmark(Description = "Large String INSERT - Page-Based")]
    public void LargeStringInsert_PageBased()
    {
        string largeText = new string('X', 1000); // 1KB text
        for (int i = 0; i < Math.Min(100, InsertCount); i++)
        {
            pageBasedDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({i}, '{largeText}_{i}', {i * 1.5})");
        }
    }

    // ==================== INSERT THROUGHPUT TEST ====================

    [Benchmark(Description = "INSERT Throughput (1 sec) - Columnar")]
    public int InsertThroughput_Columnar()
    {
        int count = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.ElapsedMilliseconds < 1000 && count < InsertCount)
        {
            var (id, name, value) = testData[count];
            columnarDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
            count++;
        }
        
        return count;
    }

    [Benchmark(Description = "INSERT Throughput (1 sec) - Page-Based")]
    public int InsertThroughput_PageBased()
    {
        int count = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        while (stopwatch.ElapsedMilliseconds < 1000 && count < InsertCount)
        {
            var (id, name, value) = testData[count];
            pageBasedDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
            count++;
        }
        
        return count;
    }

    // ==================== INSERT WITH INDEX ====================

    [Benchmark(Description = "INSERT with Index - Columnar")]
    public void InsertWithIndex_Columnar()
    {
        columnarDb!.ExecuteSQL("CREATE INDEX idx_id ON inserts(id)");
        
        for (int i = 0; i < Math.Min(1000, InsertCount); i++)
        {
            var (id, name, value) = testData[i];
            columnarDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
        }
    }

    [Benchmark(Description = "INSERT with Index - Page-Based")]
    public void InsertWithIndex_PageBased()
    {
        pageBasedDb!.ExecuteSQL("CREATE INDEX idx_id ON inserts(id)");
        
        for (int i = 0; i < Math.Min(1000, InsertCount); i++)
        {
            var (id, name, value) = testData[i];
            pageBasedDb!.ExecuteSQL($"INSERT INTO inserts VALUES ({id}, '{name}', {value})");
        }
    }
}
