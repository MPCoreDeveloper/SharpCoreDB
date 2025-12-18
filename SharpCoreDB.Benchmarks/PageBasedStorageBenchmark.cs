// <copyright file="PageBasedStorageBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Benchmarks.Infrastructure;
using SharpCoreDB.Interfaces;
using System;
using System.IO;

/// <summary>
/// Benchmarks PAGE_BASED storage performance with/without optimizations.
/// Tests the impact of:
/// - O(1) free list (vs O(n) linear scan)
/// - LRU page cache (vs direct disk access)
/// - Dirty page buffering (vs immediate writes)
/// 
/// Target: 100K records to validate production-scale performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PageBasedStorageBenchmark
{
    private const int RecordCount = 100_000;
    private string baselinePath = string.Empty;
    private string optimizedPath = string.Empty;
    private IServiceProvider services = null!;
    private Database? baselineDb; // ? Changed to Database for Dispose
    private Database? optimizedDb; // ? Changed to Database for Dispose

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

        // Baseline config: Minimal optimizations (simulates old PAGE_BASED behavior)
        var baselineConfig = new DatabaseConfig
        {
            NoEncryptMode = true,
            EnablePageCache = false,        // ? No LRU cache
            PageCacheCapacity = 0,
            UseGroupCommitWal = false,      // ? No group commit
            EnableAdaptiveWalBatching = false,
            StorageEngineType = StorageEngineType.PageBased,
            WorkloadHint = WorkloadHint.General
        };

        // Optimized config: All optimizations enabled
        var optimizedConfig = new DatabaseConfig
        {
            NoEncryptMode = true,
            EnablePageCache = true,         // ? LRU cache enabled
            PageCacheCapacity = 10000,      // 80MB cache
            UseGroupCommitWal = true,       // ? Group commit enabled
            EnableAdaptiveWalBatching = true,
            WalBatchMultiplier = 128,
            StorageEngineType = StorageEngineType.PageBased,
            WorkloadHint = WorkloadHint.WriteHeavy
        };

        var factory = services.GetRequiredService<DatabaseFactory>();
        
        baselineDb = (Database)factory.Create(baselinePath, "password", isReadOnly: false, baselineConfig);
        optimizedDb = (Database)factory.Create(optimizedPath, "password", isReadOnly: false, optimizedConfig);

        // Create table schema (same for both)
        var createTable = @"CREATE TABLE benchmark (
            id INTEGER PRIMARY KEY,
            name TEXT,
            email TEXT,
            age INTEGER,
            salary DECIMAL,
            created DATETIME
        )";

        baselineDb.ExecuteSQL(createTable);
        optimizedDb.ExecuteSQL(createTable);
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
    /// Baseline INSERT performance (no optimizations).
    /// Expected: ~800-1000ms for 100K records (slow due to O(n) free list + no cache).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Insert")]
    public void Baseline_Insert_100K()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            var sql = $@"INSERT INTO benchmark (id, name, email, age, salary, created) 
                         VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')";
            baselineDb!.ExecuteSQL(sql);
        }
    }

    /// <summary>
    /// Optimized INSERT performance (O(1) free list + LRU cache + dirty buffering).
    /// Expected: ~200-300ms for 100K records (3-5x faster than baseline).
    /// Target: 300-500 ops/ms throughput.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Insert")]
    public void Optimized_Insert_100K()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            var sql = $@"INSERT INTO benchmark (id, name, email, age, salary, created) 
                         VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')";
            optimizedDb!.ExecuteSQL(sql);
        }
    }

    /// <summary>
    /// Baseline UPDATE performance (50K random updates).
    /// Expected: ~600-800ms (slow due to no cache, immediate disk writes).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Update")]
    public void Baseline_Update_50K()
    {
        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            var sql = $"UPDATE benchmark SET salary = {50000 + id}, age = {25 + (id % 40)} WHERE id = {id}";
            baselineDb!.ExecuteSQL(sql);
        }
    }

    /// <summary>
    /// Optimized UPDATE performance (50K random updates).
    /// Expected: ~120-180ms (3-5x faster due to LRU cache + dirty buffering).
    /// Target: 200-400 ops/ms throughput.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Update")]
    public void Optimized_Update_50K()
    {
        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            var sql = $"UPDATE benchmark SET salary = {50000 + id}, age = {25 + (id % 40)} WHERE id = {id}";
            optimizedDb!.ExecuteSQL(sql);
        }
    }

    /// <summary>
    /// Baseline SELECT scan (full table scan of 100K records).
    /// Expected: ~150-200ms (no cache, direct disk reads).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Select")]
    public void Baseline_Select_FullScan()
    {
        baselineDb!.ExecuteSQL("SELECT * FROM benchmark WHERE age > 30"); // ? Returns void
    }

    /// <summary>
    /// Optimized SELECT scan (full table scan of 100K records with LRU cache).
    /// Expected: ~20-40ms first run, <5ms on cache hit (5-10x faster).
    /// Target: >90% cache hit rate for hot data.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Select")]
    public void Optimized_Select_FullScan()
    {
        optimizedDb!.ExecuteSQL("SELECT * FROM benchmark WHERE age > 30"); // ? Returns void
    }

    /// <summary>
    /// Baseline DELETE performance (20K random deletes).
    /// Expected: ~400-500ms (O(n) free list rebuild on each delete).
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Delete")]
    public void Baseline_Delete_20K()
    {
        for (int i = 0; i < RecordCount / 5; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            var sql = $"DELETE FROM benchmark WHERE id = {id}";
            baselineDb!.ExecuteSQL(sql);
        }
    }

    /// <summary>
    /// Optimized DELETE performance (20K random deletes with O(1) free list).
    /// Expected: ~80-120ms (3-5x faster due to O(1) free list push).
    /// Target: 150-250 ops/ms throughput.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Delete")]
    public void Optimized_Delete_20K()
    {
        for (int i = 0; i < RecordCount / 5; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            var sql = $"DELETE FROM benchmark WHERE id = {id}";
            optimizedDb!.ExecuteSQL(sql);
        }
    }

    /// <summary>
    /// Mixed workload: 40% SELECT, 40% UPDATE, 15% INSERT, 5% DELETE (realistic OLTP).
    /// Baseline expected: ~1200-1500ms for 50K ops.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Mixed")]
    public void Baseline_MixedWorkload_50K()
    {
        var ops = RecordCount / 2;
        for (int i = 0; i < ops; i++)
        {
            var op = i % 100;
            if (op < 40) // 40% SELECT
            {
                var id = Random.Shared.Next(0, RecordCount);
                baselineDb!.ExecuteSQL($"SELECT * FROM benchmark WHERE id = {id}"); // ? Returns void
            }
            else if (op < 80) // 40% UPDATE
            {
                var id = Random.Shared.Next(0, RecordCount);
                baselineDb!.ExecuteSQL($"UPDATE benchmark SET salary = {60000 + id} WHERE id = {id}");
            }
            else if (op < 95) // 15% INSERT
            {
                var id = RecordCount + i;
                baselineDb!.ExecuteSQL($"INSERT INTO benchmark (id, name, email, age, salary, created) VALUES ({id}, 'New{id}', 'new{id}@test.com', 30, 40000, '2025-01-01')");
            }
            else // 5% DELETE
            {
                var id = Random.Shared.Next(0, RecordCount);
                baselineDb!.ExecuteSQL($"DELETE FROM benchmark WHERE id = {id}");
            }
        }
    }

    /// <summary>
    /// Mixed workload with all optimizations enabled.
    /// Optimized expected: ~250-400ms for 50K ops (3-4x faster than baseline).
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
            if (op < 40) // 40% SELECT
            {
                var id = Random.Shared.Next(0, RecordCount);
                optimizedDb!.ExecuteSQL($"SELECT * FROM benchmark WHERE id = {id}"); // ? Returns void
            }
            else if (op < 80) // 40% UPDATE
            {
                var id = Random.Shared.Next(0, RecordCount);
                optimizedDb!.ExecuteSQL($"UPDATE benchmark SET salary = {60000 + id} WHERE id = {id}");
            }
            else if (op < 95) // 15% INSERT
            {
                var id = RecordCount + i;
                optimizedDb!.ExecuteSQL($"INSERT INTO benchmark (id, name, email, age, salary, created) VALUES ({id}, 'New{id}', 'new{id}@test.com', 30, 40000, '2025-01-01')");
            }
            else // 5% DELETE
            {
                var id = Random.Shared.Next(0, RecordCount);
                optimizedDb!.ExecuteSQL($"DELETE FROM benchmark WHERE id = {id}");
            }
        }
    }
}
