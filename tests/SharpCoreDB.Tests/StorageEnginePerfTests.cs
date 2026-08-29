// <copyright file="StorageEnginePerfTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// WP13 allocation benchmarks for the PageBased storage-engine update/insert hot paths.
/// Documents the per-operation allocation reduction from WP10-WP13: no full row copy on
/// updates (key-only index snapshots), no ArrayPool.Rent + ToArray double allocation, and
/// exact-size row serialization.
/// </summary>
[Collection("PerformanceTests")]
[Trait("Category", "Performance")]
public sealed class StorageEnginePerfTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string testDbPath;

    public StorageEnginePerfTests(ITestOutputHelper output)
    {
        _output = output;
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_wp13_perf_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);
    }

    public void Dispose()
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

    private Table CreatePageBasedTable()
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };

        var table = new Table(new Services.Storage(crypto, key, config, null), isReadOnly: false, config)
        {
            Name = "wp13_perf_tbl",
            DataFile = Path.Combine(testDbPath, "wp13_perf_tbl.pages"),
            StorageMode = StorageMode.PageBased,
        };

        table.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
        table.AddColumn(new ColumnDefinition { Name = "age", DataType = "INTEGER" });
        table.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
        table.AddColumn(new ColumnDefinition { Name = "active", DataType = "BOOLEAN" });
        table.AddColumn(new ColumnDefinition { Name = "created", DataType = "DATETIME" });
        table.PrimaryKeyIndex = 0;
        table.InitializeStorageEngine();
        return table;
    }

    [Fact]
    public void Update_InPlace_Allocation_Benchmark()
    {
        if (TestEnvironment.IsCI)
        {
            _output.WriteLine("Skipping allocation benchmark in CI environment");
            return;
        }

        var table = CreatePageBasedTable();
        var created = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["age"] = 30, ["score"] = 1.5, ["active"] = true, ["created"] = created });

        const int iterations = 1000;

        // Warmup + settle
        table.Update("id = 1", new Dictionary<string, object> { ["age"] = 31 });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            table.Update("id = 1", new Dictionary<string, object> { ["age"] = 30 + (i % 50) });
        }

        double perUpdate = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        _output.WriteLine($"WP13 in-place UPDATE allocations: {perUpdate:F0} bytes/update ({iterations} iterations)");
        Console.WriteLine($"[benchmark] WP13 in-place UPDATE allocations: {perUpdate:F0} bytes/update");

        // WP10-WP13: single-row fixed-size update should stay bounded well below 2 KB/update
        // (no full row dictionary copy, no ArrayPool.Rent + ToArray double allocation).
        Assert.True(perUpdate < 2048, $"Per-update allocation too high: {perUpdate:F0} bytes/update");
    }

    [Fact]
    public void InsertBatch_Serialization_Allocation_Benchmark()
    {
        if (TestEnvironment.IsCI)
        {
            _output.WriteLine("Skipping allocation benchmark in CI environment");
            return;
        }

        var table = CreatePageBasedTable();
        var created = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Pre-build 10 batches with unique primary keys (outside the measurement).
        const int batchSize = 100;
        var batches = new List<List<Dictionary<string, object>>>(10);
        int nextId = 0;
        for (int b = 0; b < 10; b++)
        {
            var batch = new List<Dictionary<string, object>>(batchSize);
            for (int r = 0; r < batchSize; r++)
            {
                batch.Add(new Dictionary<string, object> { ["id"] = nextId++, ["age"] = 30 + r, ["score"] = 1.5, ["active"] = true, ["created"] = created });
            }
            batches.Add(batch);
        }

        // Warmup + settle (first batch)
        table.InsertBatch(batches[0]);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int b = 1; b < batches.Count; b++)
        {
            table.InsertBatch(batches[b]);
        }

        long totalRows = (batches.Count - 1) * batchSize;
        double perRow = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / totalRows;
        _output.WriteLine($"WP13 batch INSERT serialization: {perRow:F0} bytes/row ({totalRows} rows)");
        Console.WriteLine($"[benchmark] WP13 batch INSERT serialization: {perRow:F0} bytes/row");

        // WP13: exact-size serialization allocates the final row array once per row
        // (previously pool buffer + ToArray copy). The measurement covers the full batch
        // insert path (engine insert + B-tree/hash index maintenance + PK validation),
        // so keep a generous sanity bound that still catches allocation regressions.
        Assert.True(perRow < 16384, $"Per-row allocation too high: {perRow:F0} bytes/row");
    }
}
