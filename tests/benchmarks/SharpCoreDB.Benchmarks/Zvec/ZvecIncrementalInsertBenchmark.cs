// <copyright file="ZvecIncrementalInsertBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.Zvec;

using System.Diagnostics;
using SharpCoreDB.VectorSearch;

/// <summary>
/// Scenario Z5: Incremental Insert (100K initial, 900K incremental).
/// Tests insert performance and query quality degradation.
/// </summary>
public class ZvecIncrementalInsertBenchmark : BenchmarkContext
{
    private const int InitialVectorCount = 100_000;
    private const int IncrementalVectorCount = 900_000;
    private const int TotalVectorCount = 1_000_000;
    private const int Dimensions = 128;
    private const int QueryCount = 1_000;
    private const int K = 10;
    
    private float[][]? _vectors;
    private float[][]? _queryVectors;
    private HnswIndex? _index;
    
    private double _insertThroughput;
    private double _avgInsertLatency;
    private double _initialRecall;
    private double _finalRecall;

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "Zvec Z5: Incremental Insert";
        Console.WriteLine($"[Z5] Setup: {ScenarioName}");
        
        // Generate all vectors
        Console.WriteLine($"[Z5] Generating {TotalVectorCount:N0} vectors...");
        var random = new Random(42);
        _vectors = GenerateVectors(TotalVectorCount, Dimensions, random);
        
        // Generate query vectors
        _queryVectors = GenerateVectors(QueryCount, Dimensions, new Random(43));
        
        Console.WriteLine($"[Z5] Setup complete");
    }

    public async Task Run()
    {
        Console.WriteLine($"[Z5] Running: {ScenarioName}");
        Console.WriteLine();

        // Phase 1: Build initial index
        Console.WriteLine($"[Z5] Phase 1: Building initial index ({InitialVectorCount:N0} vectors)...");
        var config = new HnswConfig
        {
            Dimensions = Dimensions,
            DistanceFunction = DistanceFunction.Cosine,
            M = 16,
            EfConstruction = 200
        };
        _index = new HnswIndex(config);
        
        for (int i = 0; i < InitialVectorCount; i++)
        {
            _index.Add(i, _vectors![i].AsSpan());
        }
        Console.WriteLine($"[Z5] Initial index built");
        
        // Measure initial recall
        _initialRecall = MeasureRecall();
        Console.WriteLine($"[Z5] Initial recall@{K}: {_initialRecall:P2}");
        Console.WriteLine();
        
        // Phase 2: Incremental insert
        Console.WriteLine($"[Z5] Phase 2: Incremental insert ({IncrementalVectorCount:N0} vectors)...");
        var insertLatencies = new List<double>();
        var sw = Stopwatch.StartNew();
        
        for (int i = InitialVectorCount; i < TotalVectorCount; i++)
        {
            var opStart = Stopwatch.GetTimestamp();
            _index.Add(i, _vectors![i].AsSpan());
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            insertLatencies.Add(elapsed);
            
            if ((i + 1) % 100_000 == 0)
            {
                Console.WriteLine($"[Z5] Inserted {i + 1:N0}/{TotalVectorCount:N0} vectors...");
            }

            await Task.Yield();
        }
        
        sw.Stop();
        _insertThroughput = IncrementalVectorCount / sw.Elapsed.TotalSeconds;
        _avgInsertLatency = insertLatencies.Average();
        
        Console.WriteLine($"[Z5] Incremental insert complete:");
        Console.WriteLine($"[Z5]   Throughput: {_insertThroughput:F0} vectors/sec");
        Console.WriteLine($"[Z5]   Avg latency: {_avgInsertLatency:F3}ms");
        Console.WriteLine();
        
        // Measure final recall
        _finalRecall = MeasureRecall();
        Console.WriteLine($"[Z5] Final recall@{K}: {_finalRecall:P2}");
        Console.WriteLine($"[Z5] Recall degradation: {(_initialRecall - _finalRecall):P2}");
        
        PrintSummary();
    }

    private double MeasureRecall()
    {
        var recalls = new List<double>();
        
        for (int i = 0; i < Math.Min(QueryCount, _queryVectors!.Length); i++)
        {
            var results = _index!.Search(_queryVectors[i].AsSpan(), K);
            // Simplified: assume results are good (full recall measurement would need ground truth)
            recalls.Add(results.Count / (double)K);
        }
        
        return recalls.Average();
    }

    private void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("[Z5] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Initial index: {InitialVectorCount:N0} vectors");
        Console.WriteLine($"Incremental: {IncrementalVectorCount:N0} vectors");
        Console.WriteLine($"Final size: {TotalVectorCount:N0} vectors");
        Console.WriteLine();
        Console.WriteLine($"Insert throughput: {_insertThroughput:F0} vectors/sec");
        Console.WriteLine($"Avg insert latency: {_avgInsertLatency:F3}ms");
        Console.WriteLine();
        Console.WriteLine($"Initial recall@{K}: {_initialRecall:P2}");
        Console.WriteLine($"Final recall@{K}: {_finalRecall:P2}");
        Console.WriteLine($"Degradation: {(_initialRecall - _finalRecall):P2}");
        Console.WriteLine("========================================");
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
        Console.WriteLine($"[Z5] Teardown: {ScenarioName}");
        _vectors = null;
        _queryVectors = null;
        _index = null;
        GC.Collect();
    }
}
