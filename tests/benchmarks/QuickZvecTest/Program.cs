// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Diagnostics;
using System.Text.Json;
using SharpCoreDB.VectorSearch;

/// <summary>
/// SharpCoreDB HNSW Vector Benchmark Suite.
/// Measures index build, search latency, throughput, and recall.
/// Output: JSON results file for comparison with Zvec published data.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║  SharpCoreDB HNSW Vector Benchmark Suite        ║");
        Console.WriteLine("║  Compare with: Zvec (Alibaba) published data    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.WriteLine();

        var env = new
        {
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Cores = Environment.ProcessorCount
        };
        Console.WriteLine($"Runtime: {env.Runtime}");
        Console.WriteLine($"OS: {env.OS}");
        Console.WriteLine($"Cores: {env.Cores}");
        Console.WriteLine();

        var allResults = new Dictionary<string, object>();

        // ── Z1: Index Build ──
        Console.WriteLine("━━━ Z1: Index Build ━━━");
        allResults["Z1_IndexBuild"] = RunIndexBuild();
        Console.WriteLine();

        // ── Z2: Search Latency (Top-K) ──
        Console.WriteLine("━━━ Z2: Search Latency (Top-K) ━━━");
        allResults["Z2_SearchLatency"] = RunSearchLatency();
        Console.WriteLine();

        // ── Z3: Search Throughput (QPS) ──
        Console.WriteLine("━━━ Z3: Search Throughput (QPS) ━━━");
        allResults["Z3_Throughput"] = RunThroughput();
        Console.WriteLine();

        // ── Z4: Recall@K ──
        Console.WriteLine("━━━ Z4: Recall@K ━━━");
        allResults["Z4_Recall"] = RunRecall();
        Console.WriteLine();

        // Save JSON results
        var resultsDir = Path.Combine("results");
        Directory.CreateDirectory(resultsDir);
        var jsonPath = Path.Combine(resultsDir, $"sharpcoredb_hnsw_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(allResults, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Results saved to: {jsonPath}");

        // ── Summary comparison table ──
        PrintComparisonTable(allResults);
    }

    // ══════════════════════════════════════════════════
    // Z1: Index Build — measure vectors/sec for build
    // ══════════════════════════════════════════════════
    static object RunIndexBuild()
    {
        int[] sizes = [1_000, 10_000, 100_000];
        const int Dimensions = 128;
        var results = new List<object>();

        foreach (var count in sizes)
        {
            Console.Write($"  {count:N0} vectors, {Dimensions}D ... ");
            var vectors = GenerateVectors(count, Dimensions, seed: 42);

            var config = new HnswConfig
            {
                Dimensions = Dimensions,
                DistanceFunction = DistanceFunction.Cosine,
                M = 16,
                EfConstruction = 200
            };
            var index = new HnswIndex(config);

            var memBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < vectors.Length; i++)
                index.Add(i, vectors[i].AsSpan());

            sw.Stop();
            var memAfter = GC.GetTotalMemory(true);
            var throughput = count / sw.Elapsed.TotalSeconds;
            var memMB = (memAfter - memBefore) / (1024.0 * 1024.0);

            Console.WriteLine($"{throughput:N0} vec/sec, {sw.Elapsed.TotalSeconds:F2}s, {Math.Max(0, memMB):F1}MB");

            results.Add(new
            {
                Vectors = count,
                Dimensions,
                BuildTimeSec = Math.Round(sw.Elapsed.TotalSeconds, 3),
                ThroughputVecPerSec = (int)throughput,
                MemoryMB = Math.Round(Math.Max(0, memMB), 1)
            });
        }
        return results;
    }

    // ══════════════════════════════════════════════════
    // Z2: Search Latency — p50/p95/p99 per query
    // ══════════════════════════════════════════════════
    static object RunSearchLatency()
    {
        const int IndexSize = 100_000;
        const int Dimensions = 128;
        const int NumQueries = 1_000;
        int[] kValues = [10, 100];
        var results = new List<object>();

        Console.Write($"  Building index ({IndexSize:N0} vectors) ... ");
        var vectors = GenerateVectors(IndexSize, Dimensions, seed: 42);
        var index = BuildIndex(vectors, Dimensions);
        Console.WriteLine("done");

        var queryVectors = GenerateVectors(NumQueries, Dimensions, seed: 99);

        foreach (var k in kValues)
        {
            // Warmup
            for (int i = 0; i < 50; i++)
                index.Search(queryVectors[i % NumQueries].AsSpan(), k);

            var latencies = new double[NumQueries];
            for (int i = 0; i < NumQueries; i++)
            {
                var sw = Stopwatch.StartNew();
                index.Search(queryVectors[i].AsSpan(), k);
                sw.Stop();
                latencies[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(latencies);
            var p50 = latencies[(int)(NumQueries * 0.50)];
            var p95 = latencies[(int)(NumQueries * 0.95)];
            var p99 = latencies[(int)(NumQueries * 0.99)];
            var avg = latencies.Average();

            Console.WriteLine($"  K={k,-4} p50={p50:F3}ms  p95={p95:F3}ms  p99={p99:F3}ms  avg={avg:F3}ms");

            results.Add(new
            {
                K = k,
                IndexSize,
                Queries = NumQueries,
                P50ms = Math.Round(p50, 3),
                P95ms = Math.Round(p95, 3),
                P99ms = Math.Round(p99, 3),
                AvgMs = Math.Round(avg, 3)
            });
        }
        return results;
    }

    // ══════════════════════════════════════════════════
    // Z3: Throughput — queries per second (QPS)
    // ══════════════════════════════════════════════════
    static object RunThroughput()
    {
        const int IndexSize = 100_000;
        const int Dimensions = 128;
        const int K = 10;
        const int DurationSec = 10;
        int[] threadCounts = [1, 4, 8];
        var results = new List<object>();

        Console.Write($"  Building index ({IndexSize:N0} vectors) ... ");
        var vectors = GenerateVectors(IndexSize, Dimensions, seed: 42);
        var index = BuildIndex(vectors, Dimensions);
        Console.WriteLine("done");

        var queryVectors = GenerateVectors(1_000, Dimensions, seed: 99);

        foreach (var threads in threadCounts)
        {
            long totalQueries = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DurationSec));

            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                var tid = t;
                tasks[t] = Task.Run(() =>
                {
                    int local = 0;
                    var rng = new Random(tid);
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var q = queryVectors[rng.Next(queryVectors.Length)];
                        index.Search(q.AsSpan(), K);
                        local++;
                    }
                    Interlocked.Add(ref totalQueries, local);
                });
            }

            Task.WaitAll(tasks);
            var qps = totalQueries / (double)DurationSec;
            Console.WriteLine($"  {threads} threads: {qps:N0} QPS ({totalQueries:N0} queries in {DurationSec}s)");

            results.Add(new
            {
                Threads = threads,
                DurationSec,
                TotalQueries = totalQueries,
                QPS = (int)qps
            });
        }
        return results;
    }

    // ══════════════════════════════════════════════════
    // Z4: Recall@K — accuracy vs brute-force
    // ══════════════════════════════════════════════════
    static object RunRecall()
    {
        const int IndexSize = 10_000;
        const int Dimensions = 128;
        const int NumQueries = 100;
        const int K = 10;
        var results = new List<object>();

        Console.Write($"  Building index ({IndexSize:N0} vectors) ... ");
        var vectors = GenerateVectors(IndexSize, Dimensions, seed: 42);
        var index = BuildIndex(vectors, Dimensions);
        Console.WriteLine("done");

        var queryVectors = GenerateVectors(NumQueries, Dimensions, seed: 99);

        Console.Write($"  Computing brute-force ground truth ... ");
        // Brute-force ground truth
        double totalRecall = 0;
        for (int q = 0; q < NumQueries; q++)
        {
            // HNSW results
            var hnswResults = index.Search(queryVectors[q].AsSpan(), K);
            var hnswIds = new HashSet<long>(hnswResults.Select(r => r.Id));

            // Brute-force
            var distances = new (long Id, float Dist)[IndexSize];
            for (int i = 0; i < IndexSize; i++)
            {
                float dist = CosineDistance(queryVectors[q], vectors[i]);
                distances[i] = (i, dist);
            }
            Array.Sort(distances, (a, b) => a.Dist.CompareTo(b.Dist));
            var trueTopK = new HashSet<long>(distances.Take(K).Select(d => d.Id));

            // Recall = intersection / K
            int hits = hnswIds.Intersect(trueTopK).Count();
            totalRecall += (double)hits / K;
        }
        var recall = totalRecall / NumQueries;
        Console.WriteLine($"done");
        Console.WriteLine($"  Recall@{K} = {recall:P1} ({IndexSize:N0} vectors, {NumQueries} queries)");

        results.Add(new
        {
            K,
            IndexSize,
            Queries = NumQueries,
            RecallPercent = Math.Round(recall * 100, 1)
        });
        return results;
    }

    // ══════════════════════════════════════════════════
    // Comparison Table
    // ══════════════════════════════════════════════════
    static void PrintComparisonTable(Dictionary<string, object> results)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           SharpCoreDB HNSW vs Zvec (Alibaba) Comparison                ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║                                                                        ║");
        Console.WriteLine("║  Zvec reference: alibaba/zvec (8.7K★), Proxima engine, C++/SIMD        ║");
        Console.WriteLine("║  Zvec claims: 'searches billions of vectors in milliseconds'           ║");
        Console.WriteLine("║  Zvec hardware: typically high-end server with AVX-512                  ║");
        Console.WriteLine("║                                                                        ║");
        Console.WriteLine("║  SharpCoreDB: pure C#/.NET 10, HNSW, tested on i7-10850H (AVX2)       ║");
        Console.WriteLine("║                                                                        ║");
        Console.WriteLine("║  NOTE: Zvec binary requires AVX-512 (not available on this CPU).       ║");
        Console.WriteLine("║  Zvec numbers below are from their published benchmarks on optimal HW. ║");
        Console.WriteLine("║  A direct same-hardware comparison was not possible.                   ║");
        Console.WriteLine("║                                                                        ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Metric              │ SharpCoreDB (measured) │ Zvec (published)       ║");
        Console.WriteLine("╠═══════════════════════╪════════════════════════╪════════════════════════╣");
        Console.WriteLine("║  Language             │ C# / .NET 10 (AVX2+FMA)│ C++ / SIMD (AVX-512)   ║");
        Console.WriteLine("║  SIMD Distance Calc   │ ✅ System.Intrinsics   │ ✅ Native intrinsics   ║");
        Console.WriteLine("║  Index Algorithm      │ HNSW (M=16,ef=200)    │ Proxima (HNSW-based)   ║");
        Console.WriteLine("║  Platforms            │ Windows/Linux/macOS    │ Linux/macOS only       ║");
        Console.WriteLine("║  In-Process           │ ✅ Yes                │ ✅ Yes                 ║");
        Console.WriteLine("║  .NET Integration     │ ✅ Native             │ ❌ Python/C++ only     ║");
        Console.WriteLine("║  10M vec search (QPS) │ (see results above)   │ ~15,000+ QPS           ║");
        Console.WriteLine("║  Recall@10            │ (see results above)   │ 95%+ (typical HNSW)    ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Key Differentiator: SharpCoreDB is a FULL DATABASE with SQL,          ║");
        Console.WriteLine("║  CRUD, transactions, AND vector search built-in. Zvec is a             ║");
        Console.WriteLine("║  dedicated vector-only engine optimized purely for similarity search.   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
    }

    // ══════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════
    static float[][] GenerateVectors(int count, int dimensions, int seed)
    {
        var random = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dimensions];
            for (int j = 0; j < dimensions; j++)
                vectors[i][j] = (float)(random.NextDouble() * 2 - 1);

            // Normalize
            var mag = 0f;
            for (int j = 0; j < dimensions; j++)
                mag += vectors[i][j] * vectors[i][j];
            mag = MathF.Sqrt(mag);
            if (mag > 0)
                for (int j = 0; j < dimensions; j++)
                    vectors[i][j] /= mag;
        }
        return vectors;
    }

    static HnswIndex BuildIndex(float[][] vectors, int dimensions)
    {
        var config = new HnswConfig
        {
            Dimensions = dimensions,
            DistanceFunction = DistanceFunction.Cosine,
            M = 16,
            EfConstruction = 200
        };
        var index = new HnswIndex(config);
        for (int i = 0; i < vectors.Length; i++)
            index.Add(i, vectors[i].AsSpan());
        return index;
    }

    static float CosineDistance(float[] a, float[] b)
    {
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return 1f - dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
    }
}
