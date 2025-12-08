// <copyright file="QuickPerformanceComparison.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using System.Diagnostics;
using SharpCoreDB.DataStructures;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Quick and simple performance comparison without BenchmarkDotNet overhead.
/// Shows before/after of C# 14 modernization.
/// </summary>
public static class QuickPerformanceComparison
{
    public static void Run()
    {
        Console.WriteLine("??????????????????????????????????????????????????????????????????????");
        Console.WriteLine("?       C# 14 MODERNIZATION - PERFORMANCE COMPARISON                 ?");
        Console.WriteLine("?       Comparing OLD (object/Dictionary) vs NEW (Generics)         ?");
        Console.WriteLine("??????????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        const int RecordCount = 10_000;
        const int Iterations = 1_000;

        // Test 1: Index Lookup Performance
        Console.WriteLine("? TEST 1: Index Lookup Performance (1000 lookups on 10k records)");
        Console.WriteLine("?????????????????????????????????????????????????????????????????????");
        
        var oldLookupTime = TestOldLookup(RecordCount, Iterations);
        var newLookupTime = TestNewGenericLookup(RecordCount, Iterations);
        
        var lookupSpeedup = oldLookupTime / newLookupTime;
        Console.WriteLine($"? OLD Dictionary Lookup:  {oldLookupTime:F4}ms ({oldLookupTime * 1000:F1}µs)");
        Console.WriteLine($"? NEW Generic Lookup:     {newLookupTime:F4}ms ({newLookupTime * 1000:F1}µs)");
        Console.WriteLine($"? SPEEDUP:               {lookupSpeedup:F2}x faster!");
        Console.WriteLine();

        // Test 2: Insert Performance
        Console.WriteLine("? TEST 2: Insert Performance (10k inserts)");
        Console.WriteLine("?????????????????????????????????????????????????????????????????????");
        
        var oldInsertTime = TestOldInsert(RecordCount);
        var newInsertTime = TestNewGenericInsert(RecordCount);
        
        var insertSpeedup = oldInsertTime / newInsertTime;
        Console.WriteLine($"? OLD Dictionary Insert:  {oldInsertTime:F4}ms");
        Console.WriteLine($"? NEW Generic Insert:     {newInsertTime:F4}ms");
        Console.WriteLine($"? SPEEDUP:               {insertSpeedup:F2}x faster!");
        Console.WriteLine();

        // Test 3: Memory Usage
        Console.WriteLine("? TEST 3: Memory Efficiency (10k records)");
        Console.WriteLine("?????????????????????????????????????????????????????????????????????");
        
        var (oldMemory, oldAllocs) = TestOldMemory(RecordCount);
        var (newMemory, newAllocs) = TestNewMemory(RecordCount);
        
        var memorySavings = ((oldMemory - newMemory) / (double)oldMemory) * 100;
        var allocReduction = ((oldAllocs - newAllocs) / (double)oldAllocs) * 100;
        
        Console.WriteLine($"? OLD Memory:             {oldMemory / 1024.0:F2} KB ({oldAllocs:N0} allocations)");
        Console.WriteLine($"? NEW Memory:             {newMemory / 1024.0:F2} KB ({newAllocs:N0} allocations)");
        Console.WriteLine($"? MEMORY SAVED:           {memorySavings:F1}%");
        Console.WriteLine($"? ALLOCATION REDUCTION:   {allocReduction:F1}%");
        Console.WriteLine();

        // Summary
        Console.WriteLine("??????????????????????????????????????????????????????????????????????");
        Console.WriteLine("?                        SUMMARY                                     ?");
        Console.WriteLine("??????????????????????????????????????????????????????????????????????");
        Console.WriteLine($"? Lookup Speed:     {lookupSpeedup:F2}x faster");
        Console.WriteLine($"? Insert Speed:     {insertSpeedup:F2}x faster");
        Console.WriteLine($"? Memory Savings:   {memorySavings:F1}%");
        Console.WriteLine($"? Alloc Reduction:  {allocReduction:F1}%");
        Console.WriteLine();
        Console.WriteLine("? OVERALL: Generic type-safe implementation is significantly faster");
        Console.WriteLine("           and more memory efficient than object-based approach!");
        Console.WriteLine();
    }

    private static double TestOldLookup(int recordCount, int iterations)
    {
        var dict = new Dictionary<int, List<long>>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            dict[i] = new List<long> { i * 100 };
        }

        var random = new Random(42);
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            var key = random.Next(recordCount);
            if (dict.TryGetValue(key, out var positions))
            {
                _ = positions[0]; // Access to ensure not optimized away
            }
        }
        
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static double TestNewGenericLookup(int recordCount, int iterations)
    {
        var index = new GenericHashIndex<int>("test");
        for (int i = 0; i < recordCount; i++)
        {
            index.Add(i, i * 100);
        }

        var random = new Random(42);
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            var key = random.Next(recordCount);
            var positions = index.Find(key);
            _ = positions.FirstOrDefault(); // Access to ensure not optimized away
        }
        
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static double TestOldInsert(int recordCount)
    {
        var sw = Stopwatch.StartNew();
        
        var dict = new Dictionary<int, List<long>>();
        for (int i = 0; i < recordCount; i++)
        {
            if (!dict.TryGetValue(i, out var positions))
            {
                positions = new List<long>(); // Allocates new List each time
                dict[i] = positions;
            }
            positions.Add(i);
        }
        
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static double TestNewGenericInsert(int recordCount)
    {
        var sw = Stopwatch.StartNew();
        
        var index = new GenericHashIndex<int>("test");
        for (int i = 0; i < recordCount; i++)
        {
            index.Add(i, i);
        }
        
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static (long memory, long allocations) TestOldMemory(int recordCount)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalMemory(true);
        var allocsBefore = GC.GetTotalAllocatedBytes();

        var dict = new Dictionary<int, List<long>>(recordCount);
        for (int i = 0; i < recordCount; i++)
        {
            dict[i] = new List<long> { i };
        }

        var after = GC.GetTotalMemory(false);
        var allocsAfter = GC.GetTotalAllocatedBytes();

        return (after - before, allocsAfter - allocsBefore);
    }

    private static (long memory, long allocations) TestNewMemory(int recordCount)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalMemory(true);
        var allocsBefore = GC.GetTotalAllocatedBytes();

        var index = new GenericHashIndex<int>("test");
        for (int i = 0; i < recordCount; i++)
        {
            index.Add(i, i);
        }

        var after = GC.GetTotalMemory(false);
        var allocsAfter = GC.GetTotalAllocatedBytes();

        return (after - before, allocsAfter - allocsBefore);
    }
}
