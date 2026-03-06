// <copyright file="ZvecThroughputBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.Zvec;

using System.Diagnostics;
using System.Collections.Concurrent;
using SharpCoreDB.VectorSearch;

/// <summary>
/// Scenario Z3: Throughput Under Load (concurrent clients, 60 seconds).
/// Tests QPS with 1, 4, 8, 16 concurrent clients.
/// </summary>
public class ZvecThroughputBenchmark : BenchmarkContext
{
    private const int VectorCount = 1_000_000;
    private const int Dimensions = 128;
    private const int TestDurationSeconds = 60;
    private const int K = 10; // Top-10 queries
    
    private float[][]? _vectors;
    private float[][]? _queryVectors;
    private HnswIndex? _index;
    
    private readonly Dictionary<int, ThroughputResult> _resultsByClientCount = new();

    private class ThroughputResult
    {
        public int ClientCount { get; set; }
        public int TotalQueries { get; set; }
        public double DurationSeconds { get; set; }
        public double QPS { get; set; }
        public List<double> Latencies { get; set; } = [];
        public double CpuUsagePercent { get; set; }
        public long MemoryUsageBytes { get; set; }
    }

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "Zvec Z3: Throughput Under Load";
        Console.WriteLine($"[Z3] Setup: {ScenarioName}");
        
        // Generate dataset vectors
        Console.WriteLine($"[Z3] Generating {VectorCount:N0} dataset vectors...");
        var random = new Random(42);
        _vectors = GenerateVectors(VectorCount, Dimensions, random);
        
        // Generate query vectors (larger pool for concurrent access)
        Console.WriteLine($"[Z3] Generating 10,000 query vectors...");
        _queryVectors = GenerateVectors(10_000, Dimensions, new Random(43));
        
        // Build HNSW index
        Console.WriteLine($"[Z3] Building HNSW index...");
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
                Console.WriteLine($"[Z3] Indexed {i + 1:N0}/{VectorCount:N0} vectors...");
            }
        }
        
        Console.WriteLine($"[Z3] Index build complete");
    }

    public async Task Run()
    {
        Console.WriteLine($"[Z3] Running: {ScenarioName}");
        Console.WriteLine($"[Z3] Test duration: {TestDurationSeconds} seconds per client count");
        Console.WriteLine();

        if (_index == null || _queryVectors == null)
        {
            throw new InvalidOperationException("Index or query vectors not initialized");
        }

        var clientCounts = new[] { 1, 4, 8, 16 };
        
        foreach (var clientCount in clientCounts)
        {
            Console.WriteLine($"[Z3] === {clientCount} Concurrent Client(s) ===");
            await RunThroughputTest(clientCount);
            Console.WriteLine();
            
            // Cool-down period
            await Task.Delay(2000);
            GC.Collect();
        }

        PrintSummary();
    }

    private async Task RunThroughputTest(int clientCount)
    {
        var totalQueries = 0;
        var allLatencies = new ConcurrentBag<double>();
        var sw = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);
        
        // Create client tasks
        var tasks = new List<Task>();
        var cts = new CancellationTokenSource();
        
        for (int clientId = 0; clientId < clientCount; clientId++)
        {
            var id = clientId;
            tasks.Add(Task.Run(async () =>
            {
                var queryIndex = 0;
                var localQueries = 0;
                
                while (!cts.Token.IsCancellationRequested)
                {
                    var queryVector = _queryVectors![queryIndex % _queryVectors.Length];
                    queryIndex++;
                    
                    var opStart = Stopwatch.GetTimestamp();
                    var results = _index!.Search(queryVector.AsSpan(), K);
                    var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
                    
                    allLatencies.Add(elapsed);
                    localQueries++;
                    
                    await Task.Yield();
                }
                
                Interlocked.Add(ref totalQueries, localQueries);
            }, cts.Token));
        }
        
        // Run for specified duration
        await Task.Delay(TestDurationSeconds * 1000);
        cts.Cancel();
        
        await Task.WhenAll(tasks);
        sw.Stop();
        
        var memoryAfter = GC.GetTotalMemory(false);
        var memoryUsage = memoryAfter - memoryBefore;
        
        var result = new ThroughputResult
        {
            ClientCount = clientCount,
            TotalQueries = totalQueries,
            DurationSeconds = sw.Elapsed.TotalSeconds,
            QPS = totalQueries / sw.Elapsed.TotalSeconds,
            Latencies = allLatencies.ToList(),
            MemoryUsageBytes = memoryUsage
        };
        
        _resultsByClientCount[clientCount] = result;
        
        Console.WriteLine($"[Z3] Throughput test complete:");
        Console.WriteLine($"[Z3]   Clients: {clientCount}");
        Console.WriteLine($"[Z3]   Total queries: {totalQueries:N0}");
        Console.WriteLine($"[Z3]   Duration: {result.DurationSeconds:F2}s");
        Console.WriteLine($"[Z3]   QPS: {result.QPS:F0}");
        Console.WriteLine($"[Z3]   Latency p50: {CalculatePercentile(result.Latencies, 50):F3}ms");
        Console.WriteLine($"[Z3]   Latency p95: {CalculatePercentile(result.Latencies, 95):F3}ms");
        Console.WriteLine($"[Z3]   Latency p99: {CalculatePercentile(result.Latencies, 99):F3}ms");
        Console.WriteLine($"[Z3]   Memory delta: {memoryUsage / (1024.0 * 1024.0):F2} MB");
    }

    private void PrintSummary()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[Z3] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Dataset: {VectorCount:N0} vectors, {Dimensions} dimensions");
        Console.WriteLine($"Query: Top-{K}");
        Console.WriteLine($"Duration: {TestDurationSeconds}s per test");
        Console.WriteLine();
        
        Console.WriteLine("Throughput Scaling:");
        Console.WriteLine("Clients | QPS    | p50 (ms) | p99 (ms) | Queries");
        Console.WriteLine("--------|--------|----------|----------|----------");
        
        foreach (var clientCount in _resultsByClientCount.Keys.OrderBy(x => x))
        {
            var result = _resultsByClientCount[clientCount];
            Console.WriteLine($"{clientCount,7} | {result.QPS,6:F0} | {CalculatePercentile(result.Latencies, 50),8:F3} | {CalculatePercentile(result.Latencies, 99),8:F3} | {result.TotalQueries,8:N0}");
        }
        
        Console.WriteLine();
        
        // Calculate scaling efficiency
        if (_resultsByClientCount.ContainsKey(1))
        {
            var baselineQPS = _resultsByClientCount[1].QPS;
            Console.WriteLine("Scaling Efficiency:");
            foreach (var clientCount in _resultsByClientCount.Keys.OrderBy(x => x).Skip(1))
            {
                var result = _resultsByClientCount[clientCount];
                var idealQPS = baselineQPS * clientCount;
                var efficiency = (result.QPS / idealQPS) * 100;
                Console.WriteLine($"  {clientCount} clients: {efficiency:F1}% efficient ({result.QPS:F0} QPS vs {idealQPS:F0} ideal)");
            }
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
        Console.WriteLine($"[Z3] Teardown: {ScenarioName}");
        _vectors = null;
        _queryVectors = null;
        _index = null;
        GC.Collect();
    }
}
