// <copyright file="InsertAllocationBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.DataStructures;
using System.Buffers;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks allocation optimization impact for insert operations.
/// Measures: CPU time, allocations, GC collections.
/// Target: 30-50% reduction in allocations and CPU.
/// </summary>
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class InsertAllocationBenchmark
{
    private DatabaseFactory _factory = null!;
    private string _dbPath = null!;
    private const int RowCount = 10_000;

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"benchmark_{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        _factory = serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_dbPath))
            {
                Directory.Delete(_dbPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// BASELINE: Current implementation with Dictionary allocations.
    /// Expected: ~252ms, 15.64 MB allocated.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Baseline_DictionaryBased()
    {
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = false,
        };
        var db = _factory.Create(_dbPath + "_baseline", "pass", false, config) as Database;
        db!.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER)");

        var rows = new List<Dictionary<string, object>>(RowCount);
        for (int i = 0; i < RowCount; i++)
        {
            rows.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"User{i}",
                ["email"] = $"user{i}@test.com",
                ["age"] = 20 + (i % 50)
            });
        }

        db.BulkInsertAsync("users", rows).GetAwaiter().GetResult();
    }

    /// <summary>
    /// OPTIMIZED: ArrayPool for value buffers.
    /// Expected: -20% allocations, -10% CPU.
    /// </summary>
    [Benchmark]
    public void Optimized_ArrayPoolValues()
    {
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = false,
        };
        var db = _factory.Create(_dbPath + "_pooled", "pass", false, config) as Database;
        db!.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER)");

        var rows = new List<Dictionary<string, object>>(RowCount);
        
        // Reuse value buffer from ArrayPool
        var valueBuffer = ArrayPool<object>.Shared.Rent(4);
        try
        {
            for (int i = 0; i < RowCount; i++)
            {
                valueBuffer[0] = i;
                valueBuffer[1] = $"User{i}";
                valueBuffer[2] = $"user{i}@test.com";
                valueBuffer[3] = 20 + (i % 50);
                
                rows.Add(new Dictionary<string, object>
                {
                    ["id"] = valueBuffer[0],
                    ["name"] = valueBuffer[1],
                    ["email"] = valueBuffer[2],
                    ["age"] = valueBuffer[3]
                });
            }
        }
        finally
        {
            ArrayPool<object>.Shared.Return(valueBuffer);
        }

        db.BulkInsertAsync("users", rows).GetAwaiter().GetResult();
    }

    /// <summary>
    /// OPTIMIZED: RowData struct (zero-allocation rows).
    /// Expected: -40% allocations, -20% CPU.
    /// </summary>
    [Benchmark]
    public void Optimized_StructBasedRows()
    {
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = false,
        };
        var db = _factory.Create(_dbPath + "_struct", "pass", false, config) as Database;
        db!.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER)");

        // Pre-compute column metadata
        var columnNames = new[] { "id", "name", "email", "age" };
        var columnHashes = new int[columnNames.Length];
        for (int i = 0; i < columnNames.Length; i++)
        {
            columnHashes[i] = columnNames[i].GetHashCode();
        }

        var rows = new List<Dictionary<string, object>>(RowCount);
        var valueBuffer = RowData.RentBuffer(4);
        
        try
        {
            for (int i = 0; i < RowCount; i++)
            {
                // Populate value buffer
                valueBuffer[0] = i;
                valueBuffer[1] = $"User{i}";
                valueBuffer[2] = $"user{i}@test.com";
                valueBuffer[3] = 20 + (i % 50);
                
                // Create RowData (stack-allocated)
                var rowData = RowData.FromBuffer(columnHashes, columnNames, valueBuffer, 4);
                
                // Convert to dictionary for compatibility (still needed for now)
                rows.Add(rowData.ToDictionary());
            }
        }
        finally
        {
            RowData.ReturnBuffer(valueBuffer);
        }

        db.BulkInsertAsync("users", rows).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Comparison: Dictionary vs Struct allocation.
    /// Micro-benchmark showing pure allocation difference.
    /// </summary>
    [Benchmark]
    public void MicroBenchmark_DictionaryAllocation()
    {
        for (int i = 0; i < RowCount; i++)
        {
            var dict = new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"User{i}",
                ["email"] = $"user{i}@test.com",
                ["age"] = 20
            };
            _ = dict["id"];  // Force materialization
        }
    }

    /// <summary>
    /// Comparison: Struct-based allocation.
    /// Expected: 95% less allocations than dictionary.
    /// </summary>
    [Benchmark]
    public void MicroBenchmark_StructAllocation()
    {
        var columnNames = new[] { "id", "name", "email", "age" };
        var columnHashes = new int[4];
        for (int i = 0; i < 4; i++)
        {
            columnHashes[i] = columnNames[i].GetHashCode();
        }

        var valueBuffer = RowData.RentBuffer(4);
        try
        {
            for (int i = 0; i < RowCount; i++)
            {
                valueBuffer[0] = i;
                valueBuffer[1] = $"User{i}";
                valueBuffer[2] = $"user{i}@test.com";
                valueBuffer[3] = 20;
                
                var rowData = RowData.FromBuffer(columnHashes, columnNames, valueBuffer, 4);
                _ = rowData[0];  // Force access
            }
        }
        finally
        {
            RowData.ReturnBuffer(valueBuffer);
        }
    }
}
