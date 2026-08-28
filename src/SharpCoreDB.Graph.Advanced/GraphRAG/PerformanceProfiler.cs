#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCoreDB.Graph.Advanced.GraphRAG;

/// <summary>
/// Performance profiling and memory optimization utilities for GraphRAG operations.
/// </summary>
public static class PerformanceProfiler
{
    /// <summary>
    /// Performance metrics for a GraphRAG operation.
    /// </summary>
    public readonly record struct PerformanceMetrics(
        TimeSpan TotalDuration,
        long MemoryUsedBytes,
        int NodesProcessed,
        int EdgesProcessed,
        double OperationsPerSecond,
        string OperationName
    );

    /// <summary>
    /// Memory usage snapshot.
    /// </summary>
    private readonly record struct MemorySnapshot(long ManagedMemory, long TotalMemory);

    /// <summary>
    /// Profiles the performance of a GraphRAG search operation.
    /// </summary>
    /// <param name="engine">The GraphRAG engine instance.</param>
    /// <param name="queryEmbedding">Query embedding.</param>
    /// <param name="topK">Number of results to retrieve.</param>
    /// <param name="iterations">Number of iterations to average.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Performance metrics.</returns>
    public static async Task<PerformanceMetrics> ProfileSearchAsync(
        GraphRagEngine engine,
        float[] queryEmbedding,
        int topK = 10,
        int iterations = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        var totalDuration = TimeSpan.Zero;
        var totalMemoryUsed = 0L;
        var results = new List<List<EnhancedRanking.RankedResult>>();

        for (int i = 0; i < iterations; i++)
        {
            // Force GC before measurement
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var startMemory = GetCurrentMemory();
            var stopwatch = Stopwatch.StartNew();

            var result = await engine.SearchAsync(queryEmbedding, topK, cancellationToken: cancellationToken);

            stopwatch.Stop();
            var endMemory = GetCurrentMemory();

            totalDuration += stopwatch.Elapsed;
            totalMemoryUsed += Math.Max(0, endMemory.TotalMemory - startMemory.TotalMemory);
            results.Add(result);

            // Small delay between iterations
            await Task.Delay(100, cancellationToken);
        }

        var avgDuration = totalDuration / iterations;
        var avgMemoryUsed = totalMemoryUsed / iterations;
        var avgNodesProcessed = results.FirstOrDefault()?.Count ?? 0;
        var operationsPerSecond = avgNodesProcessed / avgDuration.TotalSeconds;

        return new PerformanceMetrics(
            TotalDuration: avgDuration,
            MemoryUsedBytes: avgMemoryUsed,
            NodesProcessed: avgNodesProcessed,
            EdgesProcessed: 0, // Not tracked in search
            OperationsPerSecond: operationsPerSecond,
            OperationName: $"GraphRAG Search (k={topK})"
        );
    }

    /// <summary>
    /// Profiles community detection performance.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">Graph table name.</param>
    /// <param name="algorithm">Algorithm to profile ("louvain", "lp", "connected").</param>
    /// <param name="iterations">Number of iterations to average.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Performance metrics.</returns>
    public static async Task<PerformanceMetrics> ProfileCommunityDetectionAsync(
        Database database,
        string tableName,
        string algorithm = "louvain",
        int iterations = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var totalDuration = TimeSpan.Zero;
        var totalMemoryUsed = 0L;
        var totalNodes = 0;
        var totalEdges = 0;

        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var startMemory = GetCurrentMemory();
            var stopwatch = Stopwatch.StartNew();

            List<(ulong nodeId, ulong communityId)> result = algorithm.ToLower() switch
            {
                "louvain" => await SqlIntegration.CommunityDetectionFunctions.DetectCommunitiesLouvainAsync(
                    database, tableName, cancellationToken: cancellationToken),
                "lp" => await SqlIntegration.CommunityDetectionFunctions.DetectCommunitiesLPAsync(
                    database, tableName, cancellationToken: cancellationToken),
                "connected" => await SqlIntegration.CommunityDetectionFunctions.GetConnectedComponentsAsync(
                    database, tableName, cancellationToken: cancellationToken),
                _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
            };

            stopwatch.Stop();
            var endMemory = GetCurrentMemory();

            totalDuration += stopwatch.Elapsed;
            totalMemoryUsed += Math.Max(0, endMemory.TotalMemory - startMemory.TotalMemory);
            totalNodes = result.Count;

            // Estimate edges (rough approximation)
            var graphData = await SqlIntegration.GraphLoader.LoadFromTableAsync(database, tableName, cancellationToken: cancellationToken);
            totalEdges = graphData.EdgeCount;

            await Task.Delay(100, cancellationToken);
        }

        var avgDuration = totalDuration / iterations;
        var avgMemoryUsed = totalMemoryUsed / iterations;
        var operationsPerSecond = totalNodes / avgDuration.TotalSeconds;

        return new PerformanceMetrics(
            TotalDuration: avgDuration,
            MemoryUsedBytes: avgMemoryUsed,
            NodesProcessed: totalNodes,
            EdgesProcessed: totalEdges,
            OperationsPerSecond: operationsPerSecond,
            OperationName: $"Community Detection ({algorithm})"
        );
    }

    /// <summary>
    /// Profiles vector search performance.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">Embedding table name.</param>
    /// <param name="queryEmbedding">Query embedding.</param>
    /// <param name="topK">Number of results.</param>
    /// <param name="iterations">Number of iterations to average.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Performance metrics.</returns>
    public static async Task<PerformanceMetrics> ProfileVectorSearchAsync(
        Database database,
        string tableName,
        float[] queryEmbedding,
        int topK = 10,
        int iterations = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        var totalDuration = TimeSpan.Zero;
        var totalMemoryUsed = 0L;
        var totalResults = 0;

        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var startMemory = GetCurrentMemory();
            var stopwatch = Stopwatch.StartNew();

            var results = await VectorSearchIntegration.SemanticSimilaritySearchAsync(
                database, tableName, queryEmbedding, topK, cancellationToken);

            stopwatch.Stop();
            var endMemory = GetCurrentMemory();

            totalDuration += stopwatch.Elapsed;
            totalMemoryUsed += Math.Max(0, endMemory.TotalMemory - startMemory.TotalMemory);
            totalResults = results.Count;

            await Task.Delay(50, cancellationToken);
        }

        var avgDuration = totalDuration / iterations;
        var avgMemoryUsed = totalMemoryUsed / iterations;
        var operationsPerSecond = totalResults / avgDuration.TotalSeconds;

        return new PerformanceMetrics(
            TotalDuration: avgDuration,
            MemoryUsedBytes: avgMemoryUsed,
            NodesProcessed: totalResults,
            EdgesProcessed: 0,
            OperationsPerSecond: operationsPerSecond,
            OperationName: $"Vector Search (k={topK})"
        );
    }

    /// <summary>
    /// Runs comprehensive performance benchmarks for GraphRAG operations.
    /// </summary>
    /// <param name="engine">The GraphRAG engine.</param>
    /// <param name="queryEmbedding">Query embedding for search benchmarks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of benchmark results.</returns>
    public static async Task<Dictionary<string, PerformanceMetrics>> RunComprehensiveBenchmarkAsync(
        GraphRagEngine engine,
        float[] queryEmbedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        var results = new Dictionary<string, PerformanceMetrics>();

        // Benchmark search operations
        results["Search_k10"] = await ProfileSearchAsync(engine, queryEmbedding, 10, 3, cancellationToken);
        results["Search_k50"] = await ProfileSearchAsync(engine, queryEmbedding, 50, 3, cancellationToken);

        // Benchmark community detection
        results["Community_Louvain"] = await ProfileCommunityDetectionAsync(
            engine.GetDatabase(), engine.GetGraphTableName(), "louvain", 2, cancellationToken);

        results["Community_Connected"] = await ProfileCommunityDetectionAsync(
            engine.GetDatabase(), engine.GetGraphTableName(), "connected", 2, cancellationToken);

        // Benchmark vector search
        results["VectorSearch_k10"] = await ProfileVectorSearchAsync(
            engine.GetDatabase(), engine.GetEmbeddingTableName(), queryEmbedding, 10, 3, cancellationToken);

        return results;
    }

    /// <summary>
    /// Generates a performance report from benchmark results.
    /// </summary>
    /// <param name="results">Benchmark results.</param>
    /// <returns>Formatted performance report.</returns>
    public static string GeneratePerformanceReport(Dictionary<string, PerformanceMetrics> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var report = new System.Text.StringBuilder();
        report.AppendLine("=== GraphRAG Performance Report ===");
        report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine();

        foreach (var (_, metrics) in results.OrderBy(r => r.Key))
        {
            report.AppendLine($"Operation: {metrics.OperationName}");
            report.AppendLine($"  Duration: {metrics.TotalDuration.TotalMilliseconds:F2}ms");
            report.AppendLine($"  Memory Used: {metrics.MemoryUsedBytes / 1024.0:F2} KB");
            report.AppendLine($"  Nodes Processed: {metrics.NodesProcessed}");
            report.AppendLine($"  Edges Processed: {metrics.EdgesProcessed}");
            report.AppendLine($"  Operations/sec: {metrics.OperationsPerSecond:F2}");
            report.AppendLine();
        }

        // Summary statistics
        var avgDuration = results.Values.Average(m => m.TotalDuration.TotalMilliseconds);
        var totalMemory = results.Values.Sum(m => m.MemoryUsedBytes) / 1024.0;
        var totalNodes = results.Values.Sum(m => m.NodesProcessed);

        report.AppendLine("=== Summary Statistics ===");
        report.AppendLine($"Average Operation Time: {avgDuration:F2}ms");
        report.AppendLine($"Total Memory Used: {totalMemory:F2} KB");
        report.AppendLine($"Total Nodes Processed: {totalNodes}");
        report.AppendLine();

        // Performance recommendations
        report.AppendLine("=== Recommendations ===");
        if (avgDuration > 100)
        {
            report.AppendLine("- Consider caching results for repeated queries");
            report.AppendLine("- Evaluate if graph size needs optimization");
        }
        if (totalMemory > 50 * 1024) // 50MB
        {
            report.AppendLine("- High memory usage detected - consider streaming for large graphs");
            report.AppendLine("- Review caching strategy to reduce memory footprint");
        }
        if (results.Values.Any(m => m.OperationsPerSecond < 100))
        {
            report.AppendLine("- Low throughput detected - consider parallel processing");
            report.AppendLine("- Review algorithm complexity for optimization opportunities");
        }

        return report.ToString();
    }

    /// <summary>
    /// Gets current memory usage snapshot.
    /// </summary>
    private static MemorySnapshot GetCurrentMemory()
    {
        return new MemorySnapshot(
            ManagedMemory: GC.GetTotalMemory(false),
            TotalMemory: Environment.WorkingSet
        );
    }

    // Extension methods to access private fields (for profiling)
    // In a real implementation, these would be public properties
    private static Database GetDatabase(this GraphRagEngine engine) =>
        (Database)typeof(GraphRagEngine).GetField("_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine)!;

    private static string GetGraphTableName(this GraphRagEngine engine) =>
        (string)typeof(GraphRagEngine).GetField("_graphTableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine)!;

    private static string GetEmbeddingTableName(this GraphRagEngine engine) =>
        (string)typeof(GraphRagEngine).GetField("_embeddingTableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine)!;
}

/// <summary>
/// Memory optimization utilities for GraphRAG operations.
/// </summary>
public static class MemoryOptimizer
{
    /// <summary>
    /// Optimizes memory usage for large graph operations.
    /// </summary>
    /// <param name="action">The action to optimize.</param>
    /// <param name="forceGC">Whether to force garbage collection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task OptimizeMemoryAsync(
        Func<CancellationToken, Task> action,
        bool forceGC = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (forceGC)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        await action(cancellationToken);

        if (forceGC)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Processes large result sets in batches to reduce memory pressure.
    /// </summary>
    /// <typeparam name="T">Type of items to process.</typeparam>
    /// <param name="items">Items to process.</param>
    /// <param name="batchSize">Size of each batch.</param>
    /// <param name="processor">Function to process each batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task ProcessInBatchesAsync<T>(
        IEnumerable<T> items,
        int batchSize,
        Func<List<T>, CancellationToken, Task> processor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(processor);
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));

        var batch = new List<T>(batchSize);

        foreach (var item in items)
        {
            batch.Add(item);

            if (batch.Count >= batchSize)
            {
                await processor(batch, cancellationToken);
                batch.Clear();

                // Allow cancellation and yield control
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        // Process remaining items
        if (batch.Count > 0)
        {
            await processor(batch, cancellationToken);
        }
    }

    /// <summary>
    /// Estimates memory usage for different data structures.
    /// </summary>
    public static class MemoryEstimator
    {
        public static long EstimateGraphMemory(GraphData graph) =>
            graph.NodeCount * 8L + // Node IDs
            graph.AdjacencyList.Sum(list => list.Length * 4L) + // Adjacency lists
            (graph.EdgeWeights?.Sum(list => list.Length * 8L) ?? 0); // Edge weights

        public static long EstimateEmbeddingMemory(int nodeCount, int dimensions) =>
            nodeCount * dimensions * 4L; // float = 4 bytes

        public static long EstimateCommunityMemory(int nodeCount) =>
            nodeCount * 16L; // (ulong, ulong) tuple
    }
}
