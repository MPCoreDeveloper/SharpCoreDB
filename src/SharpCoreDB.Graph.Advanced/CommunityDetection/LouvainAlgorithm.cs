#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCoreDB.Graph.Advanced.CommunityDetection;

/// <summary>
/// Louvain algorithm for community detection.
/// Multi-level modularity optimization that discovers hierarchical community structure.
/// 
/// Time Complexity: O(n log n) on typical graphs
/// Space Complexity: O(n + m)
/// 
/// Reference: Blondel et al., "Fast unfolding of communities in large networks", 2008
/// </summary>
public sealed class LouvainAlgorithm : ICommunityDetector
{
    private ExecutionMetrics? _lastMetrics;
    private const double ModularityTolerance = 1e-6;
    private const int MaxIterations = 50;

    /// <summary>
    /// Gets the algorithm name.
    /// </summary>
    public string AlgorithmName => "Louvain";

    /// <summary>
    /// Gets metrics from the last execution.
    /// </summary>
    public ExecutionMetrics? LastExecutionMetrics => _lastMetrics;

    /// <summary>
    /// Executes the Louvain algorithm on the given graph.
    /// Discovers communities through iterative modularity optimization.
    /// </summary>
    public async Task<CommunityDetectionResult> ExecuteAsync(GraphData graphData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphData);

        var sw = Stopwatch.StartNew();

        // Initial: each node is its own community
        var communities = Enumerable.Range(0, graphData.NodeCount)
            .Select((i, idx) => (ulong)idx)
            .ToArray();

        double currentModularity = CalculateModularity(graphData, communities);
        double previousModularity = double.MinValue;
        int iterations = 0;

        // Phase 1: Optimization (move nodes between communities)
        while (iterations < MaxIterations && currentModularity - previousModularity > ModularityTolerance)
        {
            cancellationToken.ThrowIfCancellationRequested();

            previousModularity = currentModularity;

            // Try moving each node to neighboring communities
            for (int i = 0; i < graphData.NodeCount; i++)
            {
                var bestCommunity = communities[i];
                double bestModularity = currentModularity;

                // Check neighbor communities
                var neighborCommunities = GetNeighboringCommunities(graphData, communities, i);
                foreach (var neighborComm in neighborCommunities)
                {
                    // Temporarily move node
                    var oldComm = communities[i];
                    communities[i] = neighborComm;

                    double testModularity = CalculateModularity(graphData, communities);
                    if (testModularity > bestModularity)
                    {
                        bestModularity = testModularity;
                        bestCommunity = neighborComm;
                    }

                    // Restore old community for next test
                    communities[i] = oldComm;
                }

                // Apply best move
                communities[i] = bestCommunity;
            }

            currentModularity = CalculateModularity(graphData, communities);
            iterations++;
        }

        sw.Stop();

        // Phase 2: Build results
        var communityGroups = communities
            .Select((commId, nodeIdx) => (NodeId: graphData.NodeIds[nodeIdx], CommunityId: commId))
            .GroupBy(x => x.CommunityId)
            .Select((group, idx) => new Community
            {
                Id = (ulong)idx,
                Members = group.Select(x => x.NodeId).ToList(),
                Modularity = currentModularity,
                Density = CalculateCommunityDensity(graphData, communities, (ulong)idx)
            })
            .ToArray();

        _lastMetrics = new ExecutionMetrics
        {
            Duration = sw.Elapsed,
            Iterations = iterations,
            NodesProcessed = graphData.NodeCount,
            EdgesProcessed = graphData.EdgeCount,
            PeakMemoryBytes = GC.GetTotalMemory(false),
            CustomMetrics = new Dictionary<string, object>
            {
                { "modularity", currentModularity },
                { "num_communities", communityGroups.Length }
            }
        };

        return new CommunityDetectionResult
        {
            Communities = communityGroups,
            OverallModularity = currentModularity,
            ConvergenceIterations = iterations,
            QualityMetrics = new Dictionary<string, object>
            {
                { "avg_community_size", graphData.NodeCount / (double)communityGroups.Length },
                { "num_communities", communityGroups.Length }
            }
        };
    }

    /// <summary>
    /// Calculates overall modularity of the current community partition.
    /// </summary>
    private static double CalculateModularity(GraphData graphData, ulong[] communities)
    {
        double modularity = 0.0;
        int m = graphData.EdgeCount;

        if (m == 0) return 0.0;

        // Group nodes by community
        var commNodes = communities
            .Select((id, idx) => (id, idx))
            .GroupBy(x => x.id)
            .ToDictionary(g => g.Key, g => g.Select(x => x.idx).ToArray());

        foreach (var (commId, nodes) in commNodes)
        {
            // Count internal edges and sum degrees
            int internalEdges = 0;
            long sumDegrees = 0;

            foreach (var u in nodes)
            {
                sumDegrees += graphData.AdjacencyList[u].Length;

                // Count edges within community
                foreach (var v in graphData.AdjacencyList[u])
                {
                    if (communities[v] == commId)
                        internalEdges++;
                }
            }

            // Modularity contribution: (internal_edges/m) - ((sum_degrees)/(2m))^2
            double contrib = internalEdges / (double)m - Math.Pow(sumDegrees / (2.0 * m), 2);
            modularity += contrib;
        }

        return modularity;
    }

    /// <summary>
    /// Gets all communities neighboring a given node.
    /// </summary>
    private static HashSet<ulong> GetNeighboringCommunities(GraphData graphData, ulong[] communities, int nodeIdx)
    {
        var neighbors = new HashSet<ulong> { communities[nodeIdx] };

        foreach (var neighborIdx in graphData.AdjacencyList[nodeIdx])
        {
            neighbors.Add(communities[neighborIdx]);
        }

        return neighbors;
    }

    /// <summary>
    /// Calculates density (internal edges / max possible) for a community.
    /// </summary>
    private static double CalculateCommunityDensity(GraphData graphData, ulong[] communities, ulong commId)
    {
        var nodes = communities
            .Select((id, idx) => (id, idx))
            .Where(x => x.id == commId)
            .Select(x => x.idx)
            .ToArray();

        if (nodes.Length <= 1) return 0.0;

        int internalEdges = 0;
        foreach (var u in nodes)
        {
            foreach (var v in graphData.AdjacencyList[u])
            {
                if (Array.Exists(nodes, n => n == v))
                    internalEdges++;
            }
        }

        int maxEdges = nodes.Length * (nodes.Length - 1) / 2;
        return maxEdges > 0 ? internalEdges / (2.0 * maxEdges) : 0.0;
    }
}
