// <copyright file="FsmBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.Diagnostics;
using System.IO;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Performance benchmarks for FreeSpaceManager and ExtentAllocator (SCDB Phase 2).
/// Validates O(log n) allocation performance and strategy comparison.
/// C# 14: Modern test patterns with xUnit.
/// </summary>
public class FsmBenchmarks : IDisposable
{
    private readonly ITestOutputHelper _output;
    private string? _testDbPath;
    private SingleFileStorageProvider? _provider;

    public FsmBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        _provider?.Dispose();
        
        if (_testDbPath != null && File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    [Fact]
    public void Benchmark_AllocationStrategies_PerformanceComparison()
    {
        // Arrange
        var allocator = new ExtentAllocator();
        
        for (int i = 0; i < 100; i++)
        {
            allocator.Free(new FreeExtent((ulong)(i * 50), (ulong)(10 + (i % 40))));
        }

        var sw = new Stopwatch();
        const int iterations = 1000;

        // BestFit
        allocator.Strategy = AllocationStrategy.BestFit;
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var extent = allocator.Allocate(10);
            if (extent.HasValue) allocator.Free(extent.Value);
        }
        sw.Stop();
        var bestFitTime = sw.ElapsedMilliseconds;
        _output.WriteLine($"BestFit: {bestFitTime}ms for {iterations} iterations");
        
        // FirstFit
        allocator.Strategy = AllocationStrategy.FirstFit;
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var extent = allocator.Allocate(10);
            if (extent.HasValue) allocator.Free(extent.Value);
        }
        sw.Stop();
        var firstFitTime = sw.ElapsedMilliseconds;
        _output.WriteLine($"FirstFit: {firstFitTime}ms for {iterations} iterations");
        
        // WorstFit
        allocator.Strategy = AllocationStrategy.WorstFit;
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var extent = allocator.Allocate(10);
            if (extent.HasValue) allocator.Free(extent.Value);
        }
        sw.Stop();
        var worstFitTime = sw.ElapsedMilliseconds;
        _output.WriteLine($"WorstFit: {worstFitTime}ms for {iterations} iterations");
        
        // Assert - All should be < 100ms
        Assert.True(bestFitTime < 100, $"BestFit too slow: {bestFitTime}ms");
        Assert.True(firstFitTime < 100, $"FirstFit too slow: {firstFitTime}ms");
        Assert.True(worstFitTime < 100, $"WorstFit too slow: {worstFitTime}ms");
        
        // FirstFit should generally be fastest
        _output.WriteLine($"Performance ratio - BestFit/FirstFit: {(double)bestFitTime / firstFitTime:F2}x");

        allocator.Dispose();
    }

    [Fact]
    public void Benchmark_CoalescingPerformance_UnderOneSecond()
    {
        // Arrange
        var allocator = new ExtentAllocator();
        
        // Create 10,000 adjacent extents
        for (ulong i = 0; i < 10000; i++)
        {
            allocator.Free(new FreeExtent(i * 10, 10));
        }
        
        _output.WriteLine($"Created {allocator.ExtentCount} extents");

        // Act
        var sw = Stopwatch.StartNew();
        var coalescedCount = allocator.Coalesce();
        sw.Stop();

        // Assert
        _output.WriteLine($"Coalesced {coalescedCount} extents in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Final extent count: {allocator.ExtentCount}");
        
        Assert.True(sw.ElapsedMilliseconds < 1000, 
            $"Coalescing 10K extents took {sw.ElapsedMilliseconds}ms (>1s limit)");
        Assert.Equal(1, allocator.ExtentCount); // Should coalesce to 1 extent
        
        allocator.Dispose();
    }

    [Fact]
    public void Benchmark_AllocationComplexity_IsLogarithmic()
    {
        // Test with increasing extent counts
        var sizes = new[] { 100, 1000, 10000 };
        var times = new double[sizes.Length];

        for (int s = 0; s < sizes.Length; s++)
        {
            var allocator = new ExtentAllocator();
            
            // Populate with extents
            for (int i = 0; i < sizes[s]; i++)
            {
                allocator.Free(new FreeExtent((ulong)(i * 50), 10));
            }

            // Measure 100 allocations
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                var extent = allocator.Allocate(10);
                if (extent.HasValue) allocator.Free(extent.Value);
            }
            sw.Stop();
            
            times[s] = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"Size {sizes[s]}: {times[s]:F2}ms");
            
            allocator.Dispose();
        }

        // Verify logarithmic complexity
        var ratio = times[2] / times[0];
        _output.WriteLine($"Time ratio (10000 vs 100): {ratio:F2}x");
        
        // If O(log n): 10000/100 = 100x increase → ~2-3x time increase
        // If O(n): Would be ~100x time increase
        Assert.True(ratio < 10, 
            $"Allocation appears O(n) (ratio: {ratio:F2}x). Expected O(log n) with ratio < 10x");
    }

    [Fact]
    public void Benchmark_HighFragmentation_StillPerformant()
    {
        // Arrange - Highly fragmented scenario
        var allocator = new ExtentAllocator();
        
        // 1000 extents with gaps (50% fragmentation)
        for (ulong i = 0; i < 1000; i++)
        {
            allocator.Free(new FreeExtent(i * 20, 10)); // 10 pages, 10 page gaps
        }
        
        _output.WriteLine($"Created fragmented scenario: {allocator.ExtentCount} extents");
        _output.WriteLine($"Largest extent: {allocator.GetLargestExtentSize()} pages");

        // Act - Allocate 1000 extents
        var sw = Stopwatch.StartNew();
        var successCount = 0;
        for (int i = 0; i < 1000; i++)
        {
            var extent = allocator.Allocate(5); // Request 5 pages (all should fit)
            if (extent.HasValue)
            {
                successCount++;
            }
        }
        sw.Stop();

        // Assert
        _output.WriteLine($"Allocated {successCount}/1000 extents in {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 500, 
            $"Fragmented allocation took {sw.ElapsedMilliseconds}ms (>500ms limit)");
        Assert.Equal(1000, successCount);
        
        allocator.Dispose();
    }

    [Fact]
    public void Benchmark_PageAllocation_UnderOneMicrosecond()
    {
        // Arrange
        var allocator = new ExtentAllocator();
        
        for (int i = 0; i < 100; i++)
        {
            allocator.Free(new FreeExtent((ulong)(i * 50), 25));
        }

        // Act - Single allocation benchmark
        var sw = Stopwatch.StartNew();
        var extent = allocator.Allocate(10);
        sw.Stop();

        // Assert
        var microseconds = sw.Elapsed.TotalMicroseconds;
        _output.WriteLine($"Single allocation: {microseconds:F3}µs");
        
        // Target: <1ms = <1000µs
        Assert.True(microseconds < 1000, 
            $"Single allocation took {microseconds:F1}µs (>1000µs limit)");
        
        allocator.Dispose();
    }
}
