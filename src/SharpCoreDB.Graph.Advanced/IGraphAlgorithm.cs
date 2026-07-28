#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCoreDB.Graph.Advanced;

/// <summary>
/// Base interface for all graph algorithms.
/// Provides common execution and result handling patterns.
/// </summary>
public interface IGraphAlgorithm<TResult>
{
    /// <summary>
    /// Gets the name of the algorithm.
    /// </summary>
    string AlgorithmName { get; }

    /// <summary>
    /// Executes the algorithm on the given graph data.
    /// </summary>
    /// <param name="graphData">The graph to analyze</param>
    /// <param name="cancellationToken">Cancellation token for long-running operations</param>
    /// <returns>Algorithm results</returns>
    Task<TResult> ExecuteAsync(GraphData graphData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets execution metrics (time, memory, iterations).
    /// </summary>
    ExecutionMetrics? LastExecutionMetrics { get; }
}

/// <summary>
/// Represents metrics for a single algorithm execution.
/// </summary>
public sealed class ExecutionMetrics
{
    /// <summary>
    /// Total execution time.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of iterations (for iterative algorithms).
    /// </summary>
    public int Iterations { get; init; }

    /// <summary>
    /// Peak memory usage in bytes.
    /// </summary>
    public long PeakMemoryBytes { get; init; }

    /// <summary>
    /// Number of nodes processed.
    /// </summary>
    public int NodesProcessed { get; init; }

    /// <summary>
    /// Number of edges processed.
    /// </summary>
    public int EdgesProcessed { get; init; }

    /// <summary>
    /// Algorithm-specific metrics (e.g., modularity for Louvain).
    /// </summary>
    public Dictionary<string, object> CustomMetrics { get; init; } = [];
}

/// <summary>
/// Represents a graph data structure for algorithm processing.
/// Optimized for high-performance analysis.
/// </summary>
public sealed class GraphData
{
    /// <summary>
    /// Unique node identifiers.
    /// </summary>
    public required ulong[] NodeIds { get; init; }

    /// <summary>
    /// Adjacency list: for each node, list of connected node indices.
    /// </summary>
    public required int[][] AdjacencyList { get; init; }

    /// <summary>
    /// Optional edge weights. If null, all edges are weighted equally.
    /// </summary>
    public double[][]? EdgeWeights { get; init; }

    /// <summary>
    /// Optional node weights/attributes.
    /// </summary>
    public double[]? NodeWeights { get; init; }

    /// <summary>
    /// Total number of nodes.
    /// </summary>
    public int NodeCount => NodeIds.Length;

    /// <summary>
    /// Total number of edges (sum of adjacency list lengths / 2 for undirected).
    /// </summary>
    public int EdgeCount
    {
        get
        {
            int sum = 0;
            foreach (var adj in AdjacencyList)
            {
                sum += adj.Length;
            }
            return sum / 2; // Assume undirected
        }
    }

    /// <summary>
    /// Whether the graph is directed.
    /// </summary>
    public bool IsDirected { get; init; } = false;

    /// <summary>
    /// Optional graph metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// Base interface for metric calculators (centrality, clustering, etc.).
/// </summary>
public interface IGraphMetric
{
    /// <summary>
    /// Gets the metric name.
    /// </summary>
    string MetricName { get; }

    /// <summary>
    /// Calculates the metric for each node.
    /// Returns (nodeId, metric_value) pairs.
    /// </summary>
    Task<GraphMetricResult[]> CalculateAsync(GraphData graphData, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single metric calculation result.
/// </summary>
public sealed class GraphMetricResult
{
    /// <summary>
    /// The node identifier.
    /// </summary>
    public required ulong NodeId { get; init; }

    /// <summary>
    /// The metric value.
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// Type of metric (e.g., "betweenness_centrality").
    /// </summary>
    public required string MetricType { get; init; }
}

/// <summary>
/// Represents a community detected in the graph.
/// </summary>
public sealed class Community
{
    /// <summary>
    /// Unique community identifier.
    /// </summary>
    public required ulong Id { get; init; }

    /// <summary>
    /// Node IDs in this community.
    /// </summary>
    public required List<ulong> Members { get; init; }

    /// <summary>
    /// Modularity contribution (for modularity-based algorithms).
    /// </summary>
    public double Modularity { get; init; }

    /// <summary>
    /// Internal density (edges within community / max possible).
    /// </summary>
    public double Density { get; init; }

    /// <summary>
    /// Centrality scores for nodes in this community.
    /// </summary>
    public Dictionary<ulong, double> CentralityScores { get; init; } = [];
}

/// <summary>
/// Base interface for community detection algorithms.
/// </summary>
public interface ICommunityDetector : IGraphAlgorithm<CommunityDetectionResult>
{
}

/// <summary>
/// Results from community detection.
/// </summary>
public sealed class CommunityDetectionResult
{
    /// <summary>
    /// Detected communities.
    /// </summary>
    public required Community[] Communities { get; init; }

    /// <summary>
    /// Overall modularity of the partition (if applicable).
    /// </summary>
    public double? OverallModularity { get; init; }

    /// <summary>
    /// Number of iterations until convergence.
    /// </summary>
    public int ConvergenceIterations { get; init; }

    /// <summary>
    /// Overall quality metrics.
    /// </summary>
    public Dictionary<string, object> QualityMetrics { get; init; } = [];
}
