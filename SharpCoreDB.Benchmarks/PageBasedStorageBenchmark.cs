// <copyright file="PageBasedStorageBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.IO;
using System.Collections.Generic;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PageBasedStorageBenchmark
{
    private const int RecordCount = 10_000; // ? REALISTIC: 10K records for production benchmarks
    private string baselinePath = string.Empty;
    private string optimizedPath = string.Empty;
    private IServiceProvider services = null!;
    private Database? baselineDb;
    private Database? optimizedDb;

    /// <summary>
    /// Setup: Create two databases - baseline (no optimizations) and optimized (all features enabled).
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        baselinePath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_baseline_{Guid.NewGuid()}");
        optimizedPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_optimized_{Guid.NewGuid()}");
        Directory.CreateDirectory(baselinePath);
        Directory.CreateDirectory(optimizedPath);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSharpCoreDB();
        services = serviceCollection.BuildServiceProvider();

        var baselineConfig = new DatabaseConfig
        {
            NoEncryptMode = true,
            EnablePageCache = false,
            PageCacheCapacity = 0,
            UseGroupCommitWal = false,
            EnableAdaptiveWalBatching = false,
            StorageEngineType = StorageEngineType.PageBased,
            WorkloadHint = WorkloadHint.General,
            SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled, // ? CRITICAL: Disable for benchmarks
            StrictParameterValidation = false
        };

        var optimizedConfig = new DatabaseConfig
        {
            NoEncryptMode = true,
            EnablePageCache = true,
            PageCacheCapacity = 10000,
            UseGroupCommitWal = true,
            EnableAdaptiveWalBatching = true,
            WalBatchMultiplier = 128,
            WalMaxBatchDelayMs = 10,
            StorageEngineType = StorageEngineType.PageBased,
            WorkloadHint = WorkloadHint.WriteHeavy,
            SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled, // ? CRITICAL: Disable for benchmarks
            StrictParameterValidation = false
        };

        var factory = services.GetRequiredService<DatabaseFactory>();
        
        Console.WriteLine("Creating baseline database...");
        baselineDb = (Database)factory.Create(baselinePath, "password", isReadOnly: false, baselineConfig);
        Console.WriteLine("? Baseline database created.");
        
        Console.WriteLine("Creating optimized database...");
        optimizedDb = (Database)factory.Create(optimizedPath, "password", isReadOnly: false, optimizedConfig);
        Console.WriteLine("? Optimized database created.");

        var createTable = @"CREATE TABLE bench_data (
            id INTEGER PRIMARY KEY,
            name TEXT,
            email TEXT,
            age INTEGER,
            salary DECIMAL,
            created DATETIME
        ) STORAGE = PAGE_BASED";  // ? CRITICAL FIX: Force PageBased storage!

        baselineDb.ExecuteSQL(createTable);
        optimizedDb.ExecuteSQL(createTable);

        // ? RESTORED: Full 100K dataset for production benchmarks
        Console.WriteLine($"Pre-populating baseline database with {RecordCount} rows...");
        for (int i = 0; i < RecordCount; i++)
        {
            if (i % 10000 == 0) Console.WriteLine($"  Baseline: {i}/{RecordCount}");
            var sql = $@"INSERT INTO bench_data (id, name, email, age, salary, created) 
                         VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')";
            baselineDb.ExecuteSQL(sql);
        }
        Console.WriteLine("? Baseline pre-population complete.");

        Console.WriteLine($"Pre-populating optimized database with {RecordCount} rows...");
        for (int i = 0; i < RecordCount; i++)
        {
            if (i % 100 == 0) Console.WriteLine($"  Optimized: {i}/{RecordCount}");
            
            var sql = $@"INSERT INTO bench_data (id, name, email, age, salary, created) 
                         VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')";
            
            optimizedDb.ExecuteSQL(sql);
        }
        Console.WriteLine("? Optimized pre-population complete.");
    }

    /// <summary>
    /// Cleanup: Delete test databases.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        baselineDb?.Dispose();
        optimizedDb?.Dispose();

        try
        {
            if (Directory.Exists(baselinePath))
                Directory.Delete(baselinePath, recursive: true);
            if (Directory.Exists(optimizedPath))
                Directory.Delete(optimizedPath, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Baseline UPDATE performance (5000 random updates).
    /// Expected: ~600-800ms (slow due to no cache, immediate disk writes).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Update")]
    public void Baseline_Update_50K()
    {
        // ? FIX: Use ExecuteBatchSQL for batching
        var updates = new List<string>();
        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            updates.Add($"UPDATE bench_data SET salary = {50000 + id}, age = {25 + (id % 40)} WHERE id = {id}");
        }
        baselineDb!.ExecuteBatchSQL(updates);
    }

    /// <summary>
    /// Optimized UPDATE performance (5000 random updates).
    /// Expected: ~120-180ms (3-5x faster due to LRU cache + dirty buffering).
    /// Target: 250-400 ops/ms throughput.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Update")]
    public void Optimized_Update_50K()
    {
        // ? FIX: Use ExecuteBatchSQL for batching
        var updates = new List<string>();
        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            updates.Add($"UPDATE bench_data SET salary = {50000 + id}, age = {25 + (id % 40)} WHERE id = {id}");
        }
        optimizedDb!.ExecuteBatchSQL(updates);
    }

    /// <summary>
    /// Baseline SELECT scan (full table scan of 10K records).
    /// Expected: ~150-200ms (no cache, direct disk reads).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Select")]
    public void Baseline_Select_FullScan()
    {
        baselineDb!.ExecuteSQL("SELECT * FROM bench_data WHERE age > 30");
    }

    /// <summary>
    /// Optimized SELECT scan (full table scan of 10K records with LRU cache).
    /// Expected: ~20-40ms first run, <5ms on cache hit (5-10x faster).
    /// Target: >90% cache hit rate for hot data.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Select")]
    public void Optimized_Select_FullScan()
    {
        optimizedDb!.ExecuteSQL("SELECT * FROM bench_data WHERE age > 30");
    }

    /// <summary>
    /// Baseline DELETE performance (2000 random deletes).
    /// Expected: ~400-500ms (O(n) free list rebuild on each delete).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Delete")]
    public void Baseline_Delete_20K()
    {
        // ? FIX: Use ExecuteBatchSQL for batching
        var deletes = new List<string>();
        for (int i = 0; i < RecordCount / 5; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            deletes.Add($"DELETE FROM bench_data WHERE id = {id}");
        }
        baselineDb!.ExecuteBatchSQL(deletes);
    }

    /// <summary>
    /// Optimized DELETE performance (2000 random deletes with O(1) free list).
    /// Expected: ~80-120ms (3-5x faster due to O(1) free list push).
    /// Target: 150-250 ops/ms throughput.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Delete")]
    public void Optimized_Delete_20K()
    {
        // ? FIX: Use ExecuteBatchSQL for batching
        var deletes = new List<string>();
        for (int i = 0; i < RecordCount / 5; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            deletes.Add($"DELETE FROM bench_data WHERE id = {id}");
        }
        optimizedDb!.ExecuteBatchSQL(deletes);
    }

    /// <summary>
    /// Mixed workload: 40% SELECT, 40% UPDATE, 15% INSERT, 5% DELETE (realistic OLTP).
    /// Baseline expected: ~1200-1500ms for 5K ops.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Mixed")]
    public void Baseline_MixedWorkload_50K()
    {
        var ops = RecordCount / 2;
        for (int i = 0; i < ops; i++)
        {
            var op = i % 100;
            try
            {
                if (op < 40)
                {
                    var id = i % (RecordCount / 2);
                    baselineDb!.ExecuteSQL($"SELECT * FROM bench_data WHERE id = {id}");
                }
                else if (op < 80)
                {
                    var id = i % RecordCount;
                    baselineDb!.ExecuteSQL($"UPDATE bench_data SET salary = {60000 + id}, age = {25 + (id % 40)} WHERE id = {id}");
                }
                else if (op < 95)
                {
                    var id = RecordCount + i;
                    baselineDb!.ExecuteSQL($"INSERT INTO bench_data (id, name, email, age, salary, created) VALUES ({id}, 'New{id}', 'new{id}@test.com', 30, 40000, '2025-01-01')");
                }
                else
                {
                    var id = (RecordCount / 2) + (i % (RecordCount / 2));
                    baselineDb!.ExecuteSQL($"DELETE FROM bench_data WHERE id = {id}");
                }
            }
            catch (InvalidOperationException)
            {
                // Silently ignore: record may have been deleted
            }
        }
    }

    /// <summary>
    /// Mixed workload with all optimizations enabled.
    /// Optimized expected: ~250-400ms for 5K ops (3-4x faster than baseline).
    /// Target: 100-200 ops/ms throughput.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Mixed")]
    public void Optimized_MixedWorkload_50K()
    {
        var ops = RecordCount / 2;
        for (int i = 0; i < ops; i++)
        {
            var op = i % 100;
            try
            {
                if (op < 40)
                {
                    var id = i % (RecordCount / 2);
                    optimizedDb!.ExecuteSQL($"SELECT * FROM bench_data WHERE id = {id}");
                }
                else if (op < 80)
                {
                    var id = i % RecordCount;
                    optimizedDb!.ExecuteSQL($"UPDATE bench_data SET salary = {60000 + id}, age = {25 + (id % 40)} WHERE id = {id}");
                }
                else if (op < 95)
                {
                    var id = RecordCount + i;
                    optimizedDb!.ExecuteSQL($"INSERT INTO bench_data (id, name, email, age, salary, created) VALUES ({id}, 'New{id}', 'new{id}@test.com', 30, 40000, '2025-01-01')");
                }
                else
                {
                    var id = (RecordCount / 2) + (i % (RecordCount / 2));
                    optimizedDb!.ExecuteSQL($"DELETE FROM bench_data WHERE id = {id}");
                }
            }
            catch (InvalidOperationException)
            {
                // Silently ignore: record may have been deleted
            }
        }
    }
}
