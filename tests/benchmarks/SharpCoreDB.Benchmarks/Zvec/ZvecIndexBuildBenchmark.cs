// <copyright file="ZvecIndexBuildBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.Zvec;

using System.Diagnostics;
using SharpCoreDB.VectorSearch;

/// <summary>
/// Scenario Z1: Index Build (1M vectors, 128 dimensions).
/// Compares HNSW index construction time vs brute-force baseline.
/// </summary>
public class ZvecIndexBuildBenchmark : BenchmarkContext
{
    private const int VectorCount = 1_000_000;
    private const int Dimensions = 128;
    private float[][]? _vectors;
    
    private double _hnswBuildTimeSeconds;
    private long _hnswMemoryBytes;
    private double _bruteForceBuildTimeSeconds;
    private long _bruteForceMemoryBytes;

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "Zvec Z1: Index Build (1M vectors, 128D)";
        Console.WriteLine($"[Z1] Setup: {ScenarioName}");
        Console.WriteLine($"[Z1] Generating {VectorCount:N0} vectors with {Dimensions} dimensions...");
        
        // Generate random vectors
        var random = new Random(42); // Fixed seed for reproducibility
        _vectors = new float[VectorCount][];
        
        for (int i = 0; i < VectorCount; i++)
        {
            _vectors[i] = new float[Dimensions];
            for (int j = 0; j < Dimensions; j++)
            {
                _vectors[i][j] = (float)(random.NextDouble() * 2 - 1); // Range: [-1, 1]
            }
            
            // Normalize vector
            var magnitude = Math.Sqrt(_vectors[i].Sum(x => x * x));
            for (int j = 0; j < Dimensions; j++)
            {
                _vectors[i][j] /= (float)magnitude;
            }
            
            if ((i + 1) % 100_000 == 0)
            {
                Console.WriteLine($"[Z1] Generated {i + 1:N0}/{VectorCount:N0} vectors...");
            }
        }
        
        Console.WriteLine($"[Z1] Vector generation complete");
    }

    public async Task Run()
    {
        Console.WriteLine($"[Z1] Running: {ScenarioName}");
        Console.WriteLine();

        if (_vectors == null)
        {
            throw new InvalidOperationException("Vectors not generated. Call Setup() first.");
        }

        // Benchmark 1: HNSW Index Build
        Console.WriteLine("[Z1] === HNSW Index Build ===");
        await BuildHnswIndex();
        Console.WriteLine();

        // Benchmark 2: Brute-Force Baseline
        Console.WriteLine("[Z1] === Brute-Force Baseline ===");
        await BuildBruteForceIndex();
        Console.WriteLine();

        // Summary
        PrintSummary();
    }

    private async Task BuildHnswIndex()
    {
        Console.WriteLine("[Z1] Building HNSW index...");
        Console.WriteLine($"[Z1] Parameters: M=16, ef_construction=200");
        
        var sw = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        
        // Create HNSW index with correct API
        var config = new HnswConfig
        {
            Dimensions = Dimensions,
            DistanceFunction = DistanceFunction.Cosine,
            M = 16,
            EfConstruction = 200
        };
        var index = new HnswIndex(config);
        
        // Build index
        for (int i = 0; i < _vectors!.Length; i++)
        {
            index.Add(i, _vectors[i].AsSpan());
            
            if ((i + 1) % 100_000 == 0)
            {
                Console.WriteLine($"[Z1] Indexed {i + 1:N0}/{VectorCount:N0} vectors... ({sw.Elapsed.TotalSeconds:F1}s elapsed)");
            }
        }
        
        sw.Stop();
        var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
        
        _hnswBuildTimeSeconds = sw.Elapsed.TotalSeconds;
        _hnswMemoryBytes = memoryAfter - memoryBefore;
        
        Console.WriteLine($"[Z1] HNSW build complete:");
        Console.WriteLine($"[Z1]   Time: {_hnswBuildTimeSeconds:F2}s");
        Console.WriteLine($"[Z1]   Throughput: {VectorCount / _hnswBuildTimeSeconds:F0} vectors/sec");
        Console.WriteLine($"[Z1]   Memory: {_hnswMemoryBytes / (1024.0 * 1024.0):F2} MB");
        
        await Task.CompletedTask;
    }

    private async Task BuildBruteForceIndex()
    {
        Console.WriteLine("[Z1] Building brute-force baseline (in-memory array)...");
        
        var sw = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        
        // Brute-force is just storing vectors in memory
        var bruteForceIndex = new float[_vectors!.Length][];
        Array.Copy(_vectors, bruteForceIndex, _vectors.Length);
        
        sw.Stop();
        var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
        
        _bruteForceBuildTimeSeconds = sw.Elapsed.TotalSeconds;
        _bruteForceMemoryBytes = memoryAfter - memoryBefore;
        
        Console.WriteLine($"[Z1] Brute-force build complete:");
        Console.WriteLine($"[Z1]   Time: {_bruteForceBuildTimeSeconds:F2}s");
        Console.WriteLine($"[Z1]   Throughput: {VectorCount / _bruteForceBuildTimeSeconds:F0} vectors/sec");
        Console.WriteLine($"[Z1]   Memory: {_bruteForceMemoryBytes / (1024.0 * 1024.0):F2} MB");
        
        await Task.CompletedTask;
    }

    private void PrintSummary()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[Z1] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Dataset: {VectorCount:N0} vectors, {Dimensions} dimensions");
        Console.WriteLine();
        
        Console.WriteLine("HNSW Index:");
        Console.WriteLine($"  Build Time: {_hnswBuildTimeSeconds:F2}s");
        Console.WriteLine($"  Throughput: {VectorCount / _hnswBuildTimeSeconds:F0} vectors/sec");
        Console.WriteLine($"  Memory: {_hnswMemoryBytes / (1024.0 * 1024.0):F2} MB");
        Console.WriteLine();
        
        Console.WriteLine("Brute-Force Baseline:");
        Console.WriteLine($"  Build Time: {_bruteForceBuildTimeSeconds:F2}s");
        Console.WriteLine($"  Throughput: {VectorCount / _bruteForceBuildTimeSeconds:F0} vectors/sec");
        Console.WriteLine($"  Memory: {_bruteForceMemoryBytes / (1024.0 * 1024.0):F2} MB");
        Console.WriteLine();
        
        var speedup = _bruteForceBuildTimeSeconds / _hnswBuildTimeSeconds;
        var memoryRatio = (double)_hnswMemoryBytes / _bruteForceMemoryBytes;
        
        Console.WriteLine("Comparison:");
        Console.WriteLine($"  HNSW vs Brute-Force:");
        Console.WriteLine($"    Time: {(speedup < 1 ? $"{(1/speedup):F2}x slower" : $"{speedup:F2}x faster")}");
        Console.WriteLine($"    Memory: {memoryRatio:F2}x overhead");
        Console.WriteLine();
        Console.WriteLine("========================================");
    }

    public override void Teardown()
    {
        base.Teardown();
        Console.WriteLine($"[Z1] Teardown: {ScenarioName}");
        _vectors = null;
        GC.Collect();
    }
}
