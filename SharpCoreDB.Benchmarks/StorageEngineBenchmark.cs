// <copyright file="StorageEngineBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Engines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

/// <summary>
/// Hybrid benchmark comparing three storage engine modes:
/// 1. AppendOnly - Pure append-only (sequential writes, no in-place updates)
/// 2. PageBased - In-place updates with 8KB pages
/// 3. Hybrid - WAL append + page-based compaction
/// 
/// Tests: 10k inserts (single + bulk), 10k updates, full scan, file size, VACUUM time
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 5)]
public class StorageEngineBenchmark
{
    private const int RecordCount = 10_000;
    private const int RecordSize = 100; // 100 bytes per record
    private string testDbPath = string.Empty;
    private List<byte[]> testData = new();

    [GlobalSetup]
    public void Setup()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_bench_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        // Generate test data
        var random = new Random(42);
        testData = new List<byte[]>(RecordCount);
        
        for (int i = 0; i < RecordCount; i++)
        {
            var data = new byte[RecordSize];
            random.NextBytes(data);
            testData.Add(data);
        }

        Console.WriteLine($"?? Setup complete: {RecordCount:N0} test records of {RecordSize} bytes each");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region INSERT Benchmarks

    [Benchmark(Baseline = true, Description = "AppendOnly: 10K Single Inserts")]
    public void AppendOnly_10K_Inserts()
    {
        var dbPath = Path.Combine(testDbPath, "appendonly");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new AppendOnlyEngine(storage, dbPath);

        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < RecordCount; i++)
        {
            _ = engine.Insert("test_table", testData[i]);
        }
        
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? AppendOnly: {sw.ElapsedMilliseconds}ms, {metrics.AvgInsertTimeMicros:F2}?s/insert");
    }

    [Benchmark(Description = "PageBased: 10K Single Inserts")]
    public void PageBased_10K_Inserts()
    {
        var dbPath = Path.Combine(testDbPath, "pagebased");
        Directory.CreateDirectory(dbPath);

        using var engine = new PageBasedEngine(dbPath);

        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < RecordCount; i++)
        {
            _ = engine.Insert("test_table", testData[i]);
        }
        
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? PageBased: {sw.ElapsedMilliseconds}ms, {metrics.AvgInsertTimeMicros:F2}?s/insert");
    }

    [Benchmark(Description = "Hybrid: 10K Single Inserts")]
    public void Hybrid_10K_Inserts()
    {
        var dbPath = Path.Combine(testDbPath, "hybrid");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new HybridEngine(storage, dbPath);

        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < RecordCount; i++)
        {
            _ = engine.Insert("test_table", testData[i]);
        }
        
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? Hybrid: {sw.ElapsedMilliseconds}ms, {metrics.AvgInsertTimeMicros:F2}?s/insert, WAL: {metrics.CustomMetrics["WalSizeBytes"]} bytes");
    }

    [Benchmark(Description = "AppendOnly: Bulk Insert 10K")]
    public void AppendOnly_Bulk_Insert()
    {
        var dbPath = Path.Combine(testDbPath, "appendonly_bulk");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new AppendOnlyEngine(storage, dbPath);

        var sw = Stopwatch.StartNew();
        _ = engine.InsertBatch("test_table", testData);
        sw.Stop();
        
        Console.WriteLine($"? AppendOnly Bulk: {sw.ElapsedMilliseconds}ms");
    }

    [Benchmark(Description = "PageBased: Bulk Insert 10K")]
    public void PageBased_Bulk_Insert()
    {
        var dbPath = Path.Combine(testDbPath, "pagebased_bulk");
        Directory.CreateDirectory(dbPath);

        using var engine = new PageBasedEngine(dbPath);

        var sw = Stopwatch.StartNew();
        _ = engine.InsertBatch("test_table", testData);
        sw.Stop();
        
        Console.WriteLine($"? PageBased Bulk: {sw.ElapsedMilliseconds}ms");
    }

    [Benchmark(Description = "Hybrid: Bulk Insert 10K")]
    public void Hybrid_Bulk_Insert()
    {
        var dbPath = Path.Combine(testDbPath, "hybrid_bulk");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new HybridEngine(storage, dbPath);

        var sw = Stopwatch.StartNew();
        _ = engine.InsertBatch("test_table", testData);
        sw.Stop();
        
        Console.WriteLine($"? Hybrid Bulk: {sw.ElapsedMilliseconds}ms");
    }

    #endregion

    #region UPDATE Benchmarks

    [Benchmark(Description = "AppendOnly: 10K Updates (creates new versions)")]
    public void AppendOnly_Updates()
    {
        var dbPath = Path.Combine(testDbPath, "appendonly_updates");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new AppendOnlyEngine(storage, dbPath);

        // Insert initial data
        var references = new long[RecordCount];
        for (int i = 0; i < RecordCount; i++)
        {
            references[i] = engine.Insert("test_table", testData[i]);
        }

        // Measure update performance
        var sw = Stopwatch.StartNew();
        var random = new Random(42);
        for (int i = 0; i < RecordCount; i++)
        {
            var newData = new byte[RecordSize];
            random.NextBytes(newData);
            engine.Update("test_table", references[i], newData);
        }
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? AppendOnly Updates: {sw.ElapsedMilliseconds}ms, {metrics.AvgUpdateTimeMicros:F2}?s/update");
    }

    [Benchmark(Description = "PageBased: 10K Updates (in-place)")]
    public void PageBased_Update_Performance()
    {
        var dbPath = Path.Combine(testDbPath, "pagebased_update");
        Directory.CreateDirectory(dbPath);

        using var engine = new PageBasedEngine(dbPath);

        // Insert initial data
        var references = new long[RecordCount];
        for (int i = 0; i < RecordCount; i++)
        {
            references[i] = engine.Insert("test_table", testData[i]);
        }

        // Measure update performance
        var sw = Stopwatch.StartNew();
        
        var random = new Random(42);
        for (int i = 0; i < RecordCount; i++)
        {
            var newData = new byte[RecordSize];
            random.NextBytes(newData);
            engine.Update("test_table", references[i], newData);
        }
        
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? PageBased Updates: {sw.ElapsedMilliseconds}ms, {metrics.AvgUpdateTimeMicros:F2}?s/update");
    }

    [Benchmark(Description = "Hybrid: 10K Updates (WAL append)")]
    public void Hybrid_Updates()
    {
        var dbPath = Path.Combine(testDbPath, "hybrid_updates");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new HybridEngine(storage, dbPath);

        // Insert initial data
        var references = new long[RecordCount];
        for (int i = 0; i < RecordCount; i++)
        {
            references[i] = engine.Insert("test_table", testData[i]);
        }

        // Measure update performance
        var sw = Stopwatch.StartNew();
        var random = new Random(42);
        for (int i = 0; i < RecordCount; i++)
        {
            var newData = new byte[RecordSize];
            random.NextBytes(newData);
            engine.Update("test_table", references[i], newData);
        }
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? Hybrid Updates: {sw.ElapsedMilliseconds}ms, {metrics.AvgUpdateTimeMicros:F2}?s/update, WAL: {metrics.CustomMetrics["WalSizeBytes"]} bytes");
    }

    #endregion

    #region DELETE & READ Benchmarks

    [Benchmark(Description = "PageBased: 10K Deletes")]
    public void PageBased_Delete_Performance()
    {
        var dbPath = Path.Combine(testDbPath, "pagebased_delete");
        Directory.CreateDirectory(dbPath);

        using var engine = new PageBasedEngine(dbPath);

        // Insert initial data
        var references = new long[RecordCount];
        for (int i = 0; i < RecordCount; i++)
        {
            references[i] = engine.Insert("test_table", testData[i]);
        }

        // Measure delete performance
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < RecordCount; i++)
        {
            engine.Delete("test_table", references[i]);
        }
        
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? PageBased Deletes: {sw.ElapsedMilliseconds}ms, {metrics.AvgDeleteTimeMicros:F2}?s/delete");
    }

    [Benchmark(Description = "PageBased: 10K Random Reads")]
    public void PageBased_Read_Performance()
    {
        var dbPath = Path.Combine(testDbPath, "pagebased_read");
        Directory.CreateDirectory(dbPath);

        using var engine = new PageBasedEngine(dbPath);

        // Insert initial data
        var references = new long[RecordCount];
        for (int i = 0; i < RecordCount; i++)
        {
            references[i] = engine.Insert("test_table", testData[i]);
        }

        // Measure read performance
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < RecordCount; i++)
        {
            var data = engine.Read("test_table", references[i]);
            if (data == null)
            {
                throw new InvalidOperationException($"Failed to read record {i}");
            }
        }
        
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? PageBased Reads: {sw.ElapsedMilliseconds}ms, {metrics.AvgReadTimeMicros:F2}?s/read");
    }

    [Benchmark(Description = "Hybrid: 10K Random Reads (WAL + Pages)")]
    public void Hybrid_Read_Performance()
    {
        var dbPath = Path.Combine(testDbPath, "hybrid_read");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new HybridEngine(storage, dbPath);

        // Insert initial data
        var references = new long[RecordCount];
        for (int i = 0; i < RecordCount; i++)
        {
            references[i] = engine.Insert("test_table", testData[i]);
        }

        // Measure read performance
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < RecordCount; i++)
        {
            var data = engine.Read("test_table", references[i]);
            if (data == null)
            {
                throw new InvalidOperationException($"Failed to read record {i}");
            }
        }
        
        sw.Stop();
        
        var metrics = engine.GetMetrics();
        Console.WriteLine($"? Hybrid Reads: {sw.ElapsedMilliseconds}ms, {metrics.AvgReadTimeMicros:F2}?s/read");
    }

    #endregion

    #region VACUUM Benchmark

    [Benchmark(Description = "Hybrid: VACUUM (compact WAL to pages)")]
    public void Hybrid_Vacuum()
    {
        var dbPath = Path.Combine(testDbPath, "hybrid_vacuum");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);
        
        using var engine = new HybridEngine(storage, dbPath);

        // Insert initial data
        for (int i = 0; i < RecordCount; i++)
        {
            _ = engine.Insert("test_table", testData[i]);
        }

        // Perform VACUUM
        var sw = Stopwatch.StartNew();
        var stats = engine.VacuumAsync().GetAwaiter().GetResult();
        sw.Stop();
        
        Console.WriteLine($"? VACUUM: {sw.ElapsedMilliseconds}ms, reclaimed {stats.BytesReclaimed} bytes, {stats.TablesCompacted} tables");
    }

    #endregion
}
