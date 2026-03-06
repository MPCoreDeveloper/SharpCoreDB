// <copyright file="ZvecTopKLatencyBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.Zvec;

using System.Diagnostics;
using SharpCoreDB.VectorSearch;

/// <summary>
/// Scenario Z2: Top-K Query Latency (1M vectors, 10K queries).
/// Measures query performance with varying K values (10, 100, 1000).
/// </summary>
public class ZvecTopKLatencyBenchmark : BenchmarkContext
{
    private const int VectorCount = 1_000_000;
    private const int Dimensions = 128;
    private const int QueryCount = 10_000;
    private float[][]? _vectors;
    private float[][]? _queryVectors;
    private HnswIndex? _index;
    
    private readonly Dictionary<int, List<double>> _latenciesByK = new();
    private readonly Dictionary<int, List<int>> _resultCountsByK = new();

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "Zvec Z2: Top-K Query Latency";
        Console.WriteLine($"[Z2] Setup: {ScenarioName}");
        
        // Generate dataset vectors
        Console.WriteLine($"[Z2] Generating {VectorCount:N0} dataset vectors...");
        var random = new Random(42);
        _vectors = GenerateVectors(VectorCount, Dimensions, random);
        
        // Generate query vectors
        Console.WriteLine($"[Z2] Generating {QueryCount:N0} query vectors...");
        _queryVectors = GenerateVectors(QueryCount, Dimensions, new Random(43));
        
        // Build HNSW index
        Console.WriteLine($"[Z2] Building HNSW index...");
        var config = new HnswConfig
        {
            Dimensions = Dimensions,
            DistanceFunction = DistanceFunction.Cosine,
            M = 16,
            EfConstruction = 200
        };
        _index = new HnswIndex(config);
        
        for (int i = 0; i < _vectors.Length; i++)
        {
            _index.Add(i, _vectors[i].AsSpan());
            
            if ((i + 1) % 200_000 == 0)
            {
                Console.WriteLine($"[Z2] Indexed {i + 1:N0}/{VectorCount:N0} vectors...");
            }
        }
        
        Console.WriteLine($"[Z2] Index build complete");
    }

    public async Task Run()
    {
        Console.WriteLine($"[Z2] Running: {ScenarioName}");
        Console.WriteLine();

        if (_index == null || _queryVectors == null)
        {
            throw new InvalidOperationException("Index or query vectors not initialized");
        }

        var kValues = new[] { 10, 100, 1000 };
        
        foreach (var k in kValues)
        {
            Console.WriteLine($"[Z2] === Top-{k} Query Latency ===");
            await RunTopKQueries(k);
            Console.WriteLine();
        }

        PrintSummary();
    }

    private async Task RunTopKQueries(int k)
    {
        var latencies = new List<double>();
        var resultCounts = new List<int>();
        
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < _queryVectors!.Length; i++)
        {
            var opStart = Stopwatch.GetTimestamp();
            var results = _index!.Search(_queryVectors[i].AsSpan(), k);
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            
            latencies.Add(elapsed);
            resultCounts.Add(results.Count);
            
            if ((i + 1) % 1_000 == 0)
            {
                Console.WriteLine($"[Z2] Executed {i + 1:N0}/{QueryCount:N0} queries...");
            }

            await Task.Yield();
        }
        
        sw.Stop();
        
        _latenciesByK[k] = latencies;
        _resultCountsByK[k] = resultCounts;
        
        Console.WriteLine($"[Z2] Top-{k} queries complete:");
        Console.WriteLine($"[Z2]   Total time: {sw.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"[Z2]   QPS: {QueryCount / sw.Elapsed.TotalSeconds:F0}");
        Console.WriteLine($"[Z2]   Latency p50: {CalculatePercentile(latencies, 50):F3}ms");
        Console.WriteLine($"[Z2]   Latency p95: {CalculatePercentile(latencies, 95):F3}ms");
        Console.WriteLine($"[Z2]   Latency p99: {CalculatePercentile(latencies, 99):F3}ms");
        Console.WriteLine($"[Z2]   Latency max: {latencies.Max():F3}ms");
        Console.WriteLine($"[Z2]   Avg results: {resultCounts.Average():F1}");
    }

    private void PrintSummary()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[Z2] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Dataset: {VectorCount:N0} vectors, {Dimensions} dimensions");
        Console.WriteLine($"Queries: {QueryCount:N0}");
        Console.WriteLine();
        
        foreach (var k in _latenciesByK.Keys.OrderBy(x => x))
        {
            var latencies = _latenciesByK[k];
            var qps = QueryCount / (latencies.Sum() / 1000);
            
            Console.WriteLine($"Top-{k}:");
            Console.WriteLine($"  QPS: {qps:F0}");
            Console.WriteLine($"  Latency p50: {CalculatePercentile(latencies, 50):F3} ms");
            Console.WriteLine($"  Latency p95: {CalculatePercentile(latencies, 95):F3} ms");
            Console.WriteLine($"  Latency p99: {CalculatePercentile(latencies, 99):F3} ms");
            Console.WriteLine($"  Latency p99.9: {CalculatePercentile(latencies, 99.9):F3} ms");
            Console.WriteLine($"  Latency max: {latencies.Max():F3} ms");
            Console.WriteLine();
        }
        
        Console.WriteLine("========================================");
    }

    private static double CalculatePercentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        
        var sorted = new List<double>(values);
        sorted.Sort();
        
        var index = (int)Math.Ceiling((percentile / 100.0) * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));
        
        return sorted[index];
    }

    private static float[][] GenerateVectors(int count, int dimensions, Random random)
    {
        var vectors = new float[count][];
        
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dimensions];
            for (int j = 0; j < dimensions; j++)
            {
                vectors[i][j] = (float)(random.NextDouble() * 2 - 1);
            }
            
            // Normalize
            var magnitude = Math.Sqrt(vectors[i].Sum(x => x * x));
            for (int j = 0; j < dimensions; j++)
            {
                vectors[i][j] /= (float)magnitude;
            }
        }
        
        return vectors;
    }

    public override void Teardown()
    {
        base.Teardown();
        Console.WriteLine($"[Z2] Teardown: {ScenarioName}");
        _vectors = null;
        _queryVectors = null;
        _index = null;
        GC.Collect();
    }
}
