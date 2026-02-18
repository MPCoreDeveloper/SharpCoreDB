// <copyright file="ParallelGraphTraversalEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SharpCoreDB.Graph.Metrics;
using SharpCoreDB.Interfaces;

/// <summary>
/// Parallel graph traversal engine using work-stealing BFS.
/// ✅ GraphRAG Phase 6.1: Multi-threaded graph exploration for 2-4x speedup.
/// ✅ GraphRAG Phase 6.3: Comprehensive metrics collection for parallelism analysis.
/// </summary>
public sealed class ParallelGraphTraversalEngine
{
    private readonly int _degreeOfParallelism;
    private readonly int _minNodesForParallel;
    private readonly GraphMetricsCollector? _metricsCollector;

    /// <summary>Internal counter wrapper for metrics collection in async methods.</summary>
    private sealed class MetricsContext
    {
        public long EdgesTraversed;
        public long WorkStealingOps;
    }

    /// <summary>
    /// Initializes a new parallel graph traversal engine.
    /// </summary>
    /// <param name="degreeOfParallelism">Number of parallel workers (default: processor count).</param>
    /// <param name="minNodesForParallel">Minimum nodes to enable parallelism (default: 1000).</param>
    /// <param name="metricsCollector">Optional metrics collector for observability. Default: Global collector if metrics enabled.</param>
    public ParallelGraphTraversalEngine(
        int? degreeOfParallelism = null,
        int minNodesForParallel = 1000,
        GraphMetricsCollector? metricsCollector = null)
    {
        _degreeOfParallelism = degreeOfParallelism ?? Environment.ProcessorCount;
        _minNodesForParallel = minNodesForParallel;
        _metricsCollector = metricsCollector;

        if (_degreeOfParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism), "Must be at least 1");
    }

    /// <summary>
    /// Traverses the graph in parallel using BFS.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task producing the reachable row IDs.</returns>
    public async Task<IReadOnlyCollection<long>> TraverseBfsParallelAsync(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (maxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Max depth must be non-negative");

        using var activity = OpenTelemetryIntegration.StartGraphTraversalActivity("ParallelGraphTraversal.BFS");
        activity?.SetTag("graph.startNodeId", startNodeId);
        activity?.SetTag("graph.maxDepth", maxDepth);
        activity?.SetTag("graph.degreeOfParallelism", _degreeOfParallelism);

        var sw = Stopwatch.StartNew();

        // Small graphs: use sequential for better performance
        var estimatedSize = Math.Min(1000, table.GetCachedRowCount());
        if (estimatedSize < _minNodesForParallel || _degreeOfParallelism == 1)
        {
            return await TraverseBfsSequentialAsync(table, startNodeId, relationshipColumn, maxDepth, cancellationToken);
        }

        // Parallel BFS
        var visited = new ConcurrentDictionary<long, byte>();
        var currentLevel = new ConcurrentBag<long> { startNodeId };
        visited.TryAdd(startNodeId, 0);

        long totalEdgesTraversed = 0;

        for (int depth = 0; depth < maxDepth && !currentLevel.IsEmpty; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextLevel = new ConcurrentBag<long>();

            // Process current level in parallel
            await Parallel.ForEachAsync(
                currentLevel,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _degreeOfParallelism,
                    CancellationToken = cancellationToken
                },
                async (nodeId, ct) =>
                {
                    var neighbors = await GetNeighborsAsync(table, nodeId, relationshipColumn, ct);

                    foreach (var neighbor in neighbors)
                    {
                        if (visited.TryAdd(neighbor, 0))
                        {
                            nextLevel.Add(neighbor);
                            Interlocked.Increment(ref totalEdgesTraversed);
                        }
                    }
                });

            currentLevel = nextLevel;
        }

        sw.Stop();

        // Report metrics to global collector if enabled
        _metricsCollector?.RecordParallelTraversal(
            nodesVisited: visited.Count,
            edgesTraversed: totalEdgesTraversed,
            degreeOfParallelism: _degreeOfParallelism,
            executionTimeMs: sw.ElapsedMilliseconds);

        // OpenTelemetry tags
        activity?.SetTag("graph.nodesVisited", visited.Count);
        activity?.SetTag("graph.edgesTraversed", totalEdgesTraversed);
        activity?.SetTag("graph.executionTimeMs", sw.ElapsedMilliseconds);

        return visited.Keys.ToList();
    }

    /// <summary>
    /// Traverses the graph in parallel using work-stealing channel-based BFS.
    /// ✅ Advanced: Uses Channel&lt;T&gt; for better work distribution.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task producing the reachable row IDs.</returns>
    public async Task<IReadOnlyCollection<long>> TraverseBfsChannelAsync(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (maxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Max depth must be non-negative");

        var sw = Stopwatch.StartNew();
        var visited = new ConcurrentDictionary<long, int>(); // value = depth
        var channel = Channel.CreateUnbounded<(long NodeId, int Depth)>();
        var metrics = new MetricsContext();

        // Add start node
        visited.TryAdd(startNodeId, 0);
        await channel.Writer.WriteAsync((startNodeId, 0), cancellationToken);

        // Worker tasks
        var workers = new Task[_degreeOfParallelism];
        var completionSource = new TaskCompletionSource();

        for (int i = 0; i < _degreeOfParallelism; i++)
        {
            workers[i] = WorkerTaskAsync(
                table,
                relationshipColumn,
                maxDepth,
                channel,
                visited,
                completionSource,
                metrics,
                cancellationToken);
        }

        // Wait for all workers to complete
        await completionSource.Task;

        // Signal completion
        channel.Writer.Complete();
        await Task.WhenAll(workers);

        // Log metrics
        sw.Stop();
        _metricsCollector?.RecordParallelTraversal(
            nodesVisited: visited.Count,
            edgesTraversed: metrics.EdgesTraversed,
            degreeOfParallelism: _degreeOfParallelism,
            executionTimeMs: sw.ElapsedMilliseconds);

        return visited.Keys.ToList();
    }

    /// <summary>
    /// Worker task for channel-based parallel BFS.
    /// </summary>
    private async Task WorkerTaskAsync(
        ITable table,
        string relationshipColumn,
        int maxDepth,
        Channel<(long NodeId, int Depth)> channel,
        ConcurrentDictionary<long, int> visited,
        TaskCompletionSource completionSource,
        MetricsContext metrics,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var (nodeId, depth) in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (depth >= maxDepth)
                    continue;

                var neighbors = await GetNeighborsAsync(table, nodeId, relationshipColumn, cancellationToken);

                foreach (var neighbor in neighbors)
                {
                    if (visited.TryAdd(neighbor, depth + 1))
                    {
                        await channel.Writer.WriteAsync((neighbor, depth + 1), cancellationToken);
                        Interlocked.Increment(ref metrics.EdgesTraversed);
                    }
                }

                // Check if work queue is empty (all workers idle)
                if (channel.Reader.Count == 0)
                {
                    // Small delay to allow other workers to add items
                    Interlocked.Increment(ref metrics.WorkStealingOps);
                    await Task.Delay(10, cancellationToken);

                    if (channel.Reader.Count == 0)
                    {
                        // No more work - signal completion
                        completionSource.TrySetResult();
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
    }

    /// <summary>
    /// Sequential BFS fallback for small graphs.
    /// </summary>
    private async Task<IReadOnlyCollection<long>> TraverseBfsSequentialAsync(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<long> { startNodeId };
        var queue = new Queue<(long NodeId, int Depth)>();
        queue.Enqueue((startNodeId, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (nodeId, depth) = queue.Dequeue();

            if (depth >= maxDepth)
                continue;

            var neighbors = await GetNeighborsAsync(table, nodeId, relationshipColumn, cancellationToken);

            foreach (var neighbor in neighbors)
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue((neighbor, depth + 1));
                }
            }
        }

        return visited.ToList();
    }

    /// <summary>
    /// Gets neighbors of a node via ROWREF column.
    /// </summary>
    private async Task<List<long>> GetNeighborsAsync(
        ITable table,
        long nodeId,
        string relationshipColumn,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Placeholder for async table operations

        var neighbors = new List<long>();

        try
        {
            var rows = table.Select($"id={nodeId}");
            if (rows.Count == 0)
                return neighbors;

            var row = rows[0];
            if (row.TryGetValue(relationshipColumn, out var value))
            {
                if (value is long neighborId && neighborId > 0)
                {
                    neighbors.Add(neighborId);
                }
                else if (value != DBNull.Value && value != null)
                {
                    // Try convert
                    if (long.TryParse(value.ToString(), out var parsed))
                    {
                        neighbors.Add(parsed);
                    }
                }
            }
        }
        catch
        {
            // Node not found or invalid - return empty
        }

        return neighbors;
    }
}
