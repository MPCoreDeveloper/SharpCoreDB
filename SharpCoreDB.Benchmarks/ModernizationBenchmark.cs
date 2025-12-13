// <copyright file="ModernizationBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.DataStructures;
using System.Diagnostics;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks comparing OLD (Dictionary-based) vs NEW (Generic type-safe) implementations.
/// Measures the impact of our C# 14 modernization with generics.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ModernizationBenchmark
{
    private const int RecordCount = 10_000;
    private GenericHashIndex<int>? _genericIndex;
    private Dictionary<int, List<long>>? _oldStyleIndex;
    private Random? _random;

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine("=== C# 14 Modernization Benchmark ===");
        Console.WriteLine($"Testing with {RecordCount:N0} records");
        Console.WriteLine();

        _random = new Random(42);
        
        // Setup NEW generic index
        _genericIndex = new GenericHashIndex<int>("test_column");
        for (int i = 0; i < RecordCount; i++)
        {
            _genericIndex.Add(i, i * 100);
        }

        // Setup OLD dictionary-based index
        _oldStyleIndex = new Dictionary<int, List<long>>(RecordCount);
        for (int i = 0; i < RecordCount; i++)
        {
            if (!_oldStyleIndex.TryGetValue(i, out var positions))
            {
                positions = new List<long>();
                _oldStyleIndex[i] = positions;
            }
            positions.Add(i * 100);
        }

        Console.WriteLine("? Setup completed - Ready to benchmark!");
    }

    #region Lookup Benchmarks

    [Benchmark(Baseline = true, Description = "OLD: Dictionary Lookup")]
    public long OldDictionaryLookup()
    {
        long sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            var key = _random!.Next(RecordCount);
            if (_oldStyleIndex!.TryGetValue(key, out var positions))
            {
                sum += positions[0];
            }
        }
        return sum;
    }

    [Benchmark(Description = "NEW: Generic Type-Safe Lookup")]
    public long NewGenericLookup()
    {
        long sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            var key = _random!.Next(RecordCount);
            var positions = _genericIndex!.Find(key);
            sum += positions.FirstOrDefault();
        }
        return sum;
    }

    #endregion

    #region Insert Benchmarks

    [Benchmark(Description = "OLD: Dictionary Insert (1000 records)")]
    public void OldDictionaryInsert()
    {
        var dict = new Dictionary<int, List<long>>();
        for (int i = 0; i < 1000; i++)
        {
            if (!dict.TryGetValue(i, out var positions))
            {
                positions = new List<long>(); // ALLOCATES!
                dict[i] = positions;
            }
            positions.Add(i);
        }
    }

    [Benchmark(Description = "NEW: Generic Insert (1000 records)")]
    public void NewGenericInsert()
    {
        var index = new GenericHashIndex<int>("test");
        for (int i = 0; i < 1000; i++)
        {
            index.Add(i, i); // Optimized with pre-sized Dictionary
        }
    }

    #endregion

    #region Memory Efficiency

    [Benchmark(Description = "OLD: Memory Usage (10k records)")]
    public Dictionary<int, List<long>> OldMemoryUsage()
    {
        var dict = new Dictionary<int, List<long>>();
        for (int i = 0; i < RecordCount; i++)
        {
            var list = new List<long> { i };
            dict[i] = list;
        }
        return dict;
    }

    [Benchmark(Description = "NEW: Memory Usage (10k records)")]
    public GenericHashIndex<int> NewMemoryUsage()
    {
        var index = new GenericHashIndex<int>("test");
        for (int i = 0; i < RecordCount; i++)
        {
            index.Add(i, i);
        }
        return index;
    }

    #endregion

    #region Statistics Overhead

    [Benchmark(Description = "NEW: Get Index Statistics")]
    public void GetIndexStatistics()
    {
        var stats = _genericIndex!.GetStatistics();
        // Access properties to ensure not optimized away
        _ = stats.UniqueKeys;
        _ = stats.Selectivity;
        _ = stats.MemoryUsageBytes;
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine();
        Console.WriteLine("=== Benchmark Complete ===");
        
        if (_genericIndex != null)
        {
            var stats = _genericIndex.GetStatistics();
            Console.WriteLine($"Final Index Statistics:");
            Console.WriteLine($"  Unique Keys: {stats.UniqueKeys:N0}");
            Console.WriteLine($"  Total Entries: {stats.TotalEntries:N0}");
            Console.WriteLine($"  Memory Usage: {stats.MemoryUsageBytes / 1024.0:F2} KB");
            Console.WriteLine($"  Selectivity: {stats.Selectivity:F4}");
        }
    }
}
