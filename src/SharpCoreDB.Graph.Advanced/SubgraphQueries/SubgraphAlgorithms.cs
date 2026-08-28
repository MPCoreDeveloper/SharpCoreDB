#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCoreDB.Graph.Advanced.SubgraphQueries;

/// <summary>
/// K-Core Decomposition Algorithm.
/// Finds maximal subgraphs where every node has at least k connections within the subgraph.
/// 
/// Time Complexity: O(n + m) (linear)
/// Space Complexity: O(n + m)
/// 
/// Use cases:
/// - Finding dense subgraphs
/// - Protein interaction analysis
/// - Community structure analysis
/// 
/// Reference: Batagelj &amp; Zaversnik, "An O(m) Algorithm for Cores Decomposition", 2003
/// </summary>
public sealed class KCoreDecomposition
{
    private KCoreDecomposition() { } // Static utility class - prevent instantiation.

    /// <summary>
    /// Decomposes a graph into k-cores.
    /// Returns the k-core value for each node.
    /// </summary>
    public static async Task<(int[] KCore, Dictionary<int, List<int>> Cores)> DecomposeAsync(
        GraphData graphData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphData);

        var sw = Stopwatch.StartNew();

        var kCore = new int[graphData.NodeCount];
        var degree = new int[graphData.NodeCount];

        // Copy adjacency list for modification
        var neighbors = new HashSet<int>[graphData.NodeCount];
        for (int i = 0; i < graphData.NodeCount; i++)
        {
            neighbors[i] = new HashSet<int>(graphData.AdjacencyList[i]);
            degree[i] = neighbors[i].Count;
        }

        var degenerate = new Queue<int>();

        // Find nodes with degree < 1
        for (int i = 0; i < graphData.NodeCount; i++)
        {
            if (degree[i] < 1)
                degenerate.Enqueue(i);
        }

        // Process nodes in degenerate order
        int k = 0;
        while (degenerate.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int u = degenerate.Dequeue();

            // Update k-core
            kCore[u] = Math.Max(kCore[u], k);

            // Remove u from graph
            var neighborsCopy = neighbors[u].ToList();
            neighbors[u].Clear();

            foreach (int v in neighborsCopy)
            {
                if (neighbors[v].Remove(u))
                {
                    degree[v]--;

                    // If degree falls below current k, mark for removal
                    if (degree[v] < k + 1)
                    {
                        degenerate.Enqueue(v);
                        k = Math.Max(k, degree[v]);
                    }
                }
            }
        }

        sw.Stop();

        // Group nodes by k-core
        var coreGroups = new Dictionary<int, List<int>>();
        for (int i = 0; i < graphData.NodeCount; i++)
        {
            if (!coreGroups.ContainsKey(kCore[i]))
                coreGroups[kCore[i]] = [];
            coreGroups[kCore[i]].Add(i);
        }

        return (kCore, coreGroups);
    }

    /// <summary>
    /// Extracts nodes that form a k-core (all with core number >= k).
    /// </summary>
    public static List<int> ExtractKCore(int[] kCore, int k)
    {
        return kCore
            .Select((value, idx) => (value, idx))
            .Where(x => x.value >= k)
            .Select(x => x.idx)
            .ToList();
    }
}

/// <summary>
/// Clique Detection Algorithm (Bron-Kerbosch with pivoting).
/// Finds all maximal cliques (complete subgraphs).
/// 
/// Time Complexity: O(3^(n/3)) worst case, much better on sparse graphs
/// Space Complexity: O(n)
/// 
/// A clique is a subset where every pair of nodes is directly connected.
/// Useful for finding tightly connected communities.
/// </summary>
public sealed class CliqueDetector
{
    private CliqueDetector() { } // Static utility class - prevent instantiation.

    /// <summary>
    /// Finds all maximal cliques using Bron-Kerbosch algorithm with pivoting.
    /// </summary>
    public static async Task<List<List<int>>> FindMaximalCliquesAsync(
        GraphData graphData,
        int minCliqueSize = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphData);

        var cliques = new List<List<int>>();

        // Build adjacency matrix for fast lookup
        var adjMatrix = new bool[graphData.NodeCount][];
        for (int i = 0; i < graphData.NodeCount; i++)
        {
            adjMatrix[i] = new bool[graphData.NodeCount];
            foreach (int j in graphData.AdjacencyList[i])
            {
                adjMatrix[i][j] = true;
            }
        }

        var R = new HashSet<int>();
        var P = new HashSet<int>(Enumerable.Range(0, graphData.NodeCount));
        var X = new HashSet<int>();

        BronKerbosch(R, P, X, adjMatrix, cliques, minCliqueSize, cancellationToken);

        return cliques;
    }

    private static void BronKerbosch(
        HashSet<int> R,
        HashSet<int> P,
        HashSet<int> X,
        bool[][] adjMatrix,
        List<List<int>> cliques,
        int minSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (P.Count == 0 && X.Count == 0)
        {
            // Found a maximal clique
            if (R.Count >= minSize)
            {
                cliques.Add(new List<int>(R));
            }
            return;
        }

        // Choose pivot with maximum connections in P ∪ X
        int pivot = -1;
        int maxConnections = -1;
        foreach (int v in P.Union(X))
        {
            int connections = P.Count(u => adjMatrix[v][u]);
            if (connections > maxConnections)
            {
                maxConnections = connections;
                pivot = v;
            }
        }

        // Process P \ N(pivot)
        var pExcludePivotNeighbors = P.Where(u => !adjMatrix[pivot][u]).ToList();

        foreach (int v in pExcludePivotNeighbors)
        {
            R.Add(v);

            // N(v) = neighbors of v
            var nv = Enumerable.Range(0, adjMatrix.Length)
                .Where(u => adjMatrix[v][u])
                .ToHashSet();

            var newP = P.Intersect(nv).ToHashSet();
            var newX = X.Intersect(nv).ToHashSet();

            BronKerbosch(R, newP, newX, adjMatrix, cliques, minSize, cancellationToken);

            R.Remove(v);
            P.Remove(v);
            X.Add(v);
        }
    }
}

/// <summary>
/// Triangle Detection.
/// Finds all 3-cliques (triangles) in a graph.
/// 
/// Time Complexity: O(m^1.5) using edge iteration
/// Space Complexity: O(1) for detection, O(t) for storage (t = triangles)
/// 
/// Triangles are important for clustering coefficients and community analysis.
/// </summary>
public sealed class TriangleDetector
{
    private TriangleDetector() { } // Static utility class - prevent instantiation.

    /// <summary>
    /// Detects all triangles (3-cliques) in the graph.
    /// Returns list of (u, v, w) node triples that form triangles.
    /// </summary>
    public static async Task<List<(int u, int v, int w)>> DetectTrianglesAsync(
        GraphData graphData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphData);

        var triangles = new List<(int, int, int)>();

        for (int u = 0; u < graphData.NodeCount; u++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var neighbors = graphData.AdjacencyList[u];

            // Check pairs of neighbors
            for (int i = 0; i < neighbors.Length; i++)
            {
                int v = neighbors[i];
                for (int j = i + 1; j < neighbors.Length; j++)
                {
                    int w = neighbors[j];

                    // Check if v and w are connected
                    if (graphData.AdjacencyList[v].Contains(w))
                    {
                        // Store in canonical order
                        int a = Math.Min(u, Math.Min(v, w));
                        int c = Math.Max(u, Math.Max(v, w));
                        int b = u + v + w - a - c;

                        triangles.Add((a, b, c));
                    }
                }
            }
        }

        // Remove duplicates (from different u)
        return triangles.Distinct().ToList();
    }

    /// <summary>
    /// Counts triangles containing a specific node.
    /// </summary>
    public static int CountTrianglesForNode(GraphData graphData, int nodeIdx)
    {
        int count = 0;
        var neighbors = graphData.AdjacencyList[nodeIdx];

        for (int i = 0; i < neighbors.Length; i++)
        {
            int v = neighbors[i];
            for (int j = i + 1; j < neighbors.Length; j++)
            {
                int w = neighbors[j];

                // Check if v and w are connected
                if (graphData.AdjacencyList[v].Contains(w))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
