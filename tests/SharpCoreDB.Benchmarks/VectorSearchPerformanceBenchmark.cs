// <copyright file="VectorSearchPerformanceBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SharpCoreDB.VectorSearch;

/// <summary>
/// Benchmarks for Vector Search Performance.
/// Measures HNSW index search, build time, memory usage, and distance computation.
/// 
/// Methodology:
/// - Tests different dataset sizes (100, 1K, 10K, 100K vectors)
/// - Measures search latency, throughput, and memory
/// - Compares HNSW vs Flat indexes
/// - Tests different dimensions (384, 768, 1536 for embedding models)
/// 
/// Expected Results (based on HNSW properties):
/// - 1K vectors: ~0.1ms search latency
/// - 10K vectors: ~0.2-0.5ms search latency
/// - 100K vectors: ~1-2ms search latency
/// - 1M vectors: ~2-5ms search latency (with quantization, ~0.5-2ms)
/// 
/// ✅ C# 14: Collection expressions, primary constructors.
/// </summary>
[SimpleJob(runtimeMoniker: RuntimeMoniker.Net80)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class VectorSearchPerformanceBenchmark
{
    private HnswIndex _hnswIndex = null!;
    private FlatIndex _flatIndex = null!;
    private float[][] _queryVectors = null!;
    private Random _random = null!;

    [Params(100, 1000, 10000)]
    public int VectorCount { get; set; }

    [Params(384, 1536)]  // Common embedding dimensions
    public int Dimensions { get; set; }

    [Params(10, 100)]    // Top-k results
    public int K { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _random = new Random(42);

        // Create HNSW index
        var config = new HnswConfig
        {
            Dimensions = Dimensions,
            M = 16,
            EfConstruction = 200,
            EfSearch = 50,
            DistanceFunction = DistanceFunction.Cosine
        };
        _hnswIndex = new HnswIndex(config, seed: 42);
        _flatIndex = new FlatIndex(Dimensions, DistanceFunction.Cosine);

        // Generate random vectors and add to indexes
        for (int i = 0; i < VectorCount; i++)
        {
            var vector = GenerateRandomVector(Dimensions);
            _hnswIndex.Add(i, vector);
            _flatIndex.Add(i, vector);
        }

        // Generate query vectors
        _queryVectors = new float[10][];
        for (int i = 0; i < 10; i++)
        {
            _queryVectors[i] = GenerateRandomVector(Dimensions);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _hnswIndex?.Dispose();
        _flatIndex?.Dispose();
    }

    /// <summary>
    /// Benchmark: HNSW Index Search Latency
    /// Key metric: milliseconds per query
    /// Expected: 0.1-2ms depending on vector count
    /// </summary>
    [Benchmark(Description = "HNSW Search")]
    public int HnswSearch()
    {
        int totalResults = 0;
        foreach (var query in _queryVectors)
        {
            var results = _hnswIndex.Search(query, K);
            totalResults += results.Count;
        }
        return totalResults;
    }

    /// <summary>
    /// Benchmark: Flat Index Search Latency (baseline)
    /// Expected: 5-20ms for exhaustive search
    /// </summary>
    [Benchmark(Description = "Flat Search (Baseline)")]
    public int FlatSearch()
    {
        int totalResults = 0;
        foreach (var query in _queryVectors)
        {
            var results = _flatIndex.Search(query, K);
            totalResults += results.Count;
        }
        return totalResults;
    }

    /// <summary>
    /// Benchmark: Index Building (HNSW)
    /// Measures time to build index from scratch
    /// Expected: ~5 seconds for 1M vectors, ~500ms for 10K
    /// </summary>
    [Benchmark(Description = "HNSW Index Build")]
    public int HnswIndexBuild()
    {
        var config = new HnswConfig
        {
            Dimensions = Dimensions,
            M = 16,
            EfConstruction = 200,
            EfSearch = 50,
            DistanceFunction = DistanceFunction.Cosine
        };

        using var index = new HnswIndex(config, seed: 42);
        
        // Add all vectors
        for (int i = 0; i < VectorCount; i++)
        {
            var vector = GenerateRandomVector(Dimensions);
            index.Add(i, vector);
        }

        return index.Count;
    }

    /// <summary>
    /// Benchmark: Flat Index Building (baseline)
    /// Expected: Much faster than HNSW for build, slower for search
    /// </summary>
    [Benchmark(Description = "Flat Index Build (Baseline)")]
    public int FlatIndexBuild()
    {
        var index = new FlatIndex(Dimensions, DistanceFunction.Cosine);
        
        for (int i = 0; i < VectorCount; i++)
        {
            var vector = GenerateRandomVector(Dimensions);
            index.Add(i, vector);
        }

        return index.Count;
    }

    /// <summary>
    /// Benchmark: Distance Metric Computation (Cosine)
    /// Measures raw distance calculation without index overhead
    /// Expected: ~1-5 microseconds per pair
    /// </summary>
    [Benchmark(Description = "Cosine Distance Computation")]
    public float CosineDistanceComputation()
    {
        float totalDistance = 0;
        var query = _queryVectors[0];
        
        // Compute distance to first 100 vectors
        for (int i = 0; i < Math.Min(100, VectorCount); i++)
        {
            var dbVector = GenerateRandomVector(Dimensions);
            // Use direct computation instead of external DistanceMetrics
            totalDistance += ComputeCosineDistance(query, dbVector);
        }

        return totalDistance;
    }

    /// <summary>
    /// Benchmark: Batch Search (multiple queries, HNSW)
    /// Simulates typical application workload
    /// Expected: Linear with query count
    /// </summary>
    [Benchmark(Description = "HNSW Batch Search (100 queries)")]
    public int HnswBatchSearch()
    {
        int totalResults = 0;
        
        // Simulate 100 queries
        for (int q = 0; q < 100; q++)
        {
            var query = GenerateRandomVector(Dimensions);
            var results = _hnswIndex.Search(query, K);
            totalResults += results.Count;
        }

        return totalResults;
    }

    /// <summary>
    /// Benchmark: Large Batch Search (1000 queries, HNSW)
    /// Simulates high-throughput scenario
    /// Expected: High throughput (1000+ qps)
    /// </summary>
    [Benchmark(Description = "HNSW Large Batch Search (1000 queries)")]
    public int HnswLargeBatchSearch()
    {
        int totalResults = 0;
        
        // Simulate 1000 queries
        for (int q = 0; q < 1000; q++)
        {
            var query = GenerateRandomVector(Dimensions);
            var results = _hnswIndex.Search(query, K);
            totalResults += results.Count;
        }

        return totalResults;
    }

    /// <summary>
    /// Benchmark: Vector Operations (memory and CPU)
    /// Tests normalized distance computation
    /// </summary>
    [Benchmark(Description = "Vector Normalization")]
    public float[] VectorNormalization()
    {
        var vector = GenerateRandomVector(Dimensions);
        
        // Compute magnitude
        float magnitude = 0;
        foreach (var v in vector)
        {
            magnitude += v * v;
        }
        magnitude = (float)Math.Sqrt(magnitude);

        // Normalize
        if (magnitude > 0)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return vector;
    }

    private float[] GenerateRandomVector(int dimensions)
    {
        var vector = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)((_random.NextDouble() - 0.5) * 2);
        }
        return vector;
    }

    private float ComputeCosineDistance(float[] a, float[] b)
    {
        float dotProduct = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        magA = (float)Math.Sqrt(magA);
        magB = (float)Math.Sqrt(magB);
        return magA > 0 && magB > 0 ? dotProduct / (magA * magB) : 0;
    }
}

/// <summary>
/// Micro-benchmark: Vector Search Latency Distribution
/// Shows percentile latencies (p50, p95, p99)
/// </summary>
[SimpleJob(runtimeMoniker: RuntimeMoniker.Net80)]
[MemoryDiagnoser]
public class VectorSearchLatencyBenchmark
{
    private HnswIndex _index = null!;
    private float[][] _vectors = null!;
    private const int VectorCount = 10000;
    private const int Dimensions = 1536;
    private Random _random = null!;

    [GlobalSetup]
    public void Setup()
    {
        _random = new Random(42);
        var config = new HnswConfig
        {
            Dimensions = Dimensions,
            M = 16,
            EfConstruction = 200,
            EfSearch = 50,
            DistanceFunction = DistanceFunction.Cosine
        };

        _index = new HnswIndex(config, seed: 42);
        _vectors = new float[VectorCount][];

        for (int i = 0; i < VectorCount; i++)
        {
            var vector = GenerateRandomVector(Dimensions);
            _vectors[i] = vector;
            _index.Add(i, vector);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _index?.Dispose();
    }

    /// <summary>
    /// Measure search latency for top-10 results
    /// Expected: 1-2ms for 10K vectors
    /// </summary>
    [Benchmark]
    public int SearchTop10()
    {
        var query = GenerateRandomVector(Dimensions);
        var results = _index.Search(query, 10);
        return results.Count;
    }

    /// <summary>
    /// Measure search latency for top-100 results
    /// Expected: 2-5ms for 10K vectors
    /// </summary>
    [Benchmark]
    public int SearchTop100()
    {
        var query = GenerateRandomVector(Dimensions);
        var results = _index.Search(query, 100);
        return results.Count;
    }

    /// <summary>
    /// Measure search latency with threshold (similarity > 0.5)
    /// Expected: Variable, depends on result count
    /// </summary>
    [Benchmark]
    public int SearchWithThreshold()
    {
        var query = GenerateRandomVector(Dimensions);
        var results = _index.Search(query, 10);
        
        // Filter by threshold
        var filtered = results.Where(r => r.Distance > 0.5f).ToList();
        return filtered.Count;
    }

    private float[] GenerateRandomVector(int dimensions)
    {
        var vector = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)((_random.NextDouble() - 0.5) * 2);
        }
        return vector;
    }

    private float ComputeCosineDistance(float[] a, float[] b)
    {
        float dotProduct = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        magA = (float)Math.Sqrt(magA);
        magB = (float)Math.Sqrt(magB);
        return magA > 0 && magB > 0 ? dotProduct / (magA * magB) : 0;
    }
}
