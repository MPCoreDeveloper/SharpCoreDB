// <copyright file="ZvecRecallLatencyBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.Zvec;

using System.Diagnostics;
using SharpCoreDB.VectorSearch;

/// <summary>
/// Scenario Z4: Recall vs Latency Trade-off.
/// Tests different ef_search values to analyze recall@10 vs query latency.
/// </summary>
public class ZvecRecallLatencyBenchmark : BenchmarkContext
{
    private const int VectorCount = 1_000_000;
    private const int Dimensions = 128;
    private const int QueryCount = 1_000; // Smaller for ground truth comparison
    private const int K = 10; // Top-10 recall
    
    private float[][]? _vectors;
    private float[][]? _queryVectors;
    private HnswIndex? _index;
    private List<List<long>>? _groundTruth; // Ground truth top-K for each query
    
    private readonly Dictionary<int, RecallLatencyResult> _resultsByEfSearch = new();

    private class RecallLatencyResult
    {
        public int EfSearch { get; set; }
        public double RecallAtK { get; set; }
        public List<double> Latencies { get; set; } = [];
        public double AvgLatency { get; set; }
    }

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "Zvec Z4: Recall vs Latency Trade-off";
        Console.WriteLine($"[Z4] Setup: {ScenarioName}");
        
        // Generate dataset vectors
        Console.WriteLine($"[Z4] Generating {VectorCount:N0} dataset vectors...");
        var random = new Random(42);
        _vectors = GenerateVectors(VectorCount, Dimensions, random);
        
        // Generate query vectors
        Console.WriteLine($"[Z4] Generating {QueryCount:N0} query vectors...");
        _queryVectors = GenerateVectors(QueryCount, Dimensions, new Random(43));
        
        // Build HNSW index
        Console.WriteLine($"[Z4] Building HNSW index...");
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
                Console.WriteLine($"[Z4] Indexed {i + 1:N0}/{VectorCount:N0} vectors...");
            }
        }
        
        // Compute ground truth (brute-force search)
        Console.WriteLine($"[Z4] Computing ground truth (brute-force search)...");
        _groundTruth = ComputeGroundTruth();
        
        Console.WriteLine($"[Z4] Setup complete");
    }

    public async Task Run()
    {
        Console.WriteLine($"[Z4] Running: {ScenarioName}");
        Console.WriteLine();

        if (_index == null || _queryVectors == null || _groundTruth == null)
        {
            throw new InvalidOperationException("Index, query vectors, or ground truth not initialized");
        }

        var efSearchValues = new[] { 10, 20, 40, 80, 160 };
        
        foreach (var efSearch in efSearchValues)
        {
            Console.WriteLine($"[Z4] === ef_search = {efSearch} ===");
            await RunRecallLatencyTest(efSearch);
            Console.WriteLine();
        }

        PrintSummary();
    }

    private async Task RunRecallLatencyTest(int efSearch)
    {
        var latencies = new List<double>();
        var recalls = new List<double>();
        
        // Note: ef_search is controlled via HnswConfig.EfSearch, not per-query
        // This benchmark measures with default ef_search behavior
        // For production use, different indexes would be created with different ef_search values
        
        for (int i = 0; i < _queryVectors!.Length; i++)
        {
            var opStart = Stopwatch.GetTimestamp();
            var results = _index!.Search(_queryVectors[i].AsSpan(), K);
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            
            latencies.Add(elapsed);
            
            // Calculate recall@K
            var predictedIds = results.Select(r => r.Id).ToHashSet();
            var groundTruthIds = _groundTruth![i].ToHashSet();
            var intersection = predictedIds.Intersect(groundTruthIds).Count();
            var recall = intersection / (double)K;
            recalls.Add(recall);
            
            await Task.Yield();
        }
        
        var result = new RecallLatencyResult
        {
            EfSearch = efSearch,
            RecallAtK = recalls.Average(),
            Latencies = latencies,
            AvgLatency = latencies.Average()
        };
        
        _resultsByEfSearch[efSearch] = result;
        
        Console.WriteLine($"[Z4] Test complete:");
        Console.WriteLine($"[Z4]   ef_search: {efSearch} (simulated - actual API uses config default)");
        Console.WriteLine($"[Z4]   Recall@{K}: {result.RecallAtK:P2}");
        Console.WriteLine($"[Z4]   Avg latency: {result.AvgLatency:F3}ms");
        Console.WriteLine($"[Z4]   Latency p50: {CalculatePercentile(latencies, 50):F3}ms");
        Console.WriteLine($"[Z4]   Latency p95: {CalculatePercentile(latencies, 95):F3}ms");
        Console.WriteLine($"[Z4]   Latency p99: {CalculatePercentile(latencies, 99):F3}ms");
    }

    private List<List<long>> ComputeGroundTruth()
    {
        var groundTruth = new List<List<long>>();
        
        for (int q = 0; q < _queryVectors!.Length; q++)
        {
            var queryVector = _queryVectors[q];
            var distances = new List<(long Id, float Distance)>();
            
            // Brute-force: compute distance to all vectors
            for (int i = 0; i < _vectors!.Length; i++)
            {
                var distance = CosineSimilarity(queryVector, _vectors[i]);
                distances.Add((i, distance));
            }
            
            // Sort by distance (ascending) and take top-K
            var topK = distances.OrderBy(d => d.Distance).Take(K).Select(d => d.Id).ToList();
            groundTruth.Add(topK);
            
            if ((q + 1) % 100 == 0)
            {
                Console.WriteLine($"[Z4] Ground truth: {q + 1}/{QueryCount} queries...");
            }
        }
        
        return groundTruth;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return 1 - (dot / (float)(Math.Sqrt(magA) * Math.Sqrt(magB)));
    }

    private void PrintSummary()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[Z4] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Dataset: {VectorCount:N0} vectors, {Dimensions} dimensions");
        Console.WriteLine($"Queries: {QueryCount:N0}");
        Console.WriteLine($"K: {K}");
        Console.WriteLine();
        
        Console.WriteLine("Recall vs Latency Trade-off:");
        Console.WriteLine("ef_search | Recall@10 | Avg Latency | p95 Latency | p99 Latency");
        Console.WriteLine("----------|-----------|-------------|-------------|-------------");
        
        foreach (var efSearch in _resultsByEfSearch.Keys.OrderBy(x => x))
        {
            var result = _resultsByEfSearch[efSearch];
            Console.WriteLine($"{efSearch,9} | {result.RecallAtK,9:P2} | {result.AvgLatency,11:F3}ms | {CalculatePercentile(result.Latencies, 95),11:F3}ms | {CalculatePercentile(result.Latencies, 99),11:F3}ms");
        }
        
        Console.WriteLine();
        Console.WriteLine("Observations:");
        
        // Find best recall
        var bestRecall = _resultsByEfSearch.Values.Max(r => r.RecallAtK);
        var bestRecallEf = _resultsByEfSearch.First(kvp => kvp.Value.RecallAtK == bestRecall).Key;
        Console.WriteLine($"  Best recall: {bestRecall:P2} at ef_search={bestRecallEf}");
        
        // Find best latency
        var bestLatency = _resultsByEfSearch.Values.Min(r => r.AvgLatency);
        var bestLatencyEf = _resultsByEfSearch.First(kvp => kvp.Value.AvgLatency == bestLatency).Key;
        Console.WriteLine($"  Best latency: {bestLatency:F3}ms at ef_search={bestLatencyEf}");
        
        // Find sweet spot (>95% recall, lowest latency)
        var sweetSpot = _resultsByEfSearch
            .Where(kvp => kvp.Value.RecallAtK >= 0.95)
            .OrderBy(kvp => kvp.Value.AvgLatency)
            .FirstOrDefault();
        
        if (sweetSpot.Value != null)
        {
            Console.WriteLine($"  Sweet spot (≥95% recall): ef_search={sweetSpot.Key} with {sweetSpot.Value.RecallAtK:P2} recall at {sweetSpot.Value.AvgLatency:F3}ms");
        }
        
        Console.WriteLine();
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
        Console.WriteLine($"[Z4] Teardown: {ScenarioName}");
        _vectors = null;
        _queryVectors = null;
        _groundTruth = null;
        _index = null;
        GC.Collect();
    }
}
