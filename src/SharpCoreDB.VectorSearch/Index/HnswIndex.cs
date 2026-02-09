// <copyright file="HnswIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

/// <summary>
/// Hierarchical Navigable Small World (HNSW) approximate nearest neighbor index.
/// <para>
/// Algorithm based on "Efficient and robust approximate nearest neighbor search using
/// Hierarchical Navigable Small World graphs" (Malkov &amp; Yashunin, 2018).
/// </para>
/// <para>
/// Thread safety: concurrent reads are lock-free via <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and volatile neighbor arrays. Writes (Insert/Remove) are serialized via <see cref="Lock"/>.
/// </para>
/// </summary>
public sealed class HnswIndex : IVectorIndex
{
    private readonly ConcurrentDictionary<long, HnswNode> _nodes = new();
    private readonly HnswConfig _config;
    private readonly Lock _writeLock = new();
    private readonly Random _levelRng;

    private volatile HnswNode? _entryPoint;
    private volatile int _maxLevel;

    /// <summary>
    /// Initializes a new HNSW index with the specified configuration.
    /// </summary>
    /// <param name="config">HNSW parameters (M, efConstruction, dimensions, etc.).</param>
    /// <param name="seed">Optional RNG seed for deterministic level assignment (testing).</param>
    public HnswIndex(HnswConfig config, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config;
        _levelRng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <inheritdoc />
    public VectorIndexType IndexType => VectorIndexType.Hnsw;

    /// <inheritdoc />
    public int Count => _nodes.Count;

    /// <inheritdoc />
    public int Dimensions => _config.Dimensions;

    /// <inheritdoc />
    public DistanceFunction DistanceFunction => _config.DistanceFunction;

    /// <summary>Gets the current maximum layer in the graph.</summary>
    public int MaxLevel => _maxLevel;

    /// <summary>Gets the HNSW configuration for this index.</summary>
    public HnswConfig Config => _config;

    /// <inheritdoc />
    public long EstimatedMemoryBytes
    {
        get
        {
            long total = 256; // Base overhead
            foreach (var (_, node) in _nodes)
            {
                total += node.EstimatedBytes;
            }

            return total;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(long id, ReadOnlySpan<float> vector)
    {
        if (vector.Length != _config.Dimensions)
        {
            throw new ArgumentException(
                $"Vector has {vector.Length} dimensions, index requires {_config.Dimensions}");
        }

        int nodeLevel = RandomLevel();
        var newNode = new HnswNode(id, vector, nodeLevel);

        lock (_writeLock)
        {
            if (!_nodes.TryAdd(id, newNode))
            {
                throw new ArgumentException($"Vector with id {id} already exists in the index");
            }

            var ep = _entryPoint;

            // First node — becomes entry point
            if (ep is null)
            {
                _entryPoint = newNode;
                _maxLevel = nodeLevel;
                return;
            }

            float[] queryVec = newNode.Vector;

            // Phase 1: Greedy traverse from top to the node's insertion layer
            var currentBest = ep;
            float currentDist = ComputeDistance(queryVec, ep.Vector);

            for (int layer = _maxLevel; layer > nodeLevel; layer--)
            {
                (currentBest, currentDist) = GreedyClosest(queryVec, currentBest, currentDist, layer);
            }

            // Phase 2: Insert at each layer from nodeLevel down to 0
            for (int layer = Math.Min(nodeLevel, _maxLevel); layer >= 0; layer--)
            {
                int maxConn = layer == 0 ? _config.EffectiveMaxM0 : _config.M;

                // Search for candidates at this layer
                var candidates = SearchLayer(queryVec, currentBest, _config.EfConstruction, layer);

                // Select best neighbors
                var selectedIds = SelectNeighbors(queryVec, candidates, maxConn);

                // Set this node's neighbors at this layer
                SetNeighbors(newNode, layer, selectedIds);

                // Bidirectional: add this node as neighbor to each selected node
                foreach (long neighborId in selectedIds)
                {
                    if (_nodes.TryGetValue(neighborId, out var neighborNode))
                    {
                        AddNeighborBidirectional(neighborNode, newNode.Id, queryVec, layer, maxConn);
                    }
                }

                // Update traversal start for next layer down
                if (candidates.Count > 0)
                {
                    if (_nodes.TryGetValue(candidates[0].Id, out var closest))
                    {
                        currentBest = closest;
                        currentDist = candidates[0].Distance;
                    }
                }
            }

            // Update entry point if new node has a higher level
            if (nodeLevel > _maxLevel)
            {
                _maxLevel = nodeLevel;
                _entryPoint = newNode;
            }
        }
    }

    /// <inheritdoc />
    public bool Remove(long id)
    {
        lock (_writeLock)
        {
            if (!_nodes.TryRemove(id, out var removedNode))
                return false;

            // Remove this node from all neighbors' connection lists
            for (int layer = 0; layer <= removedNode.MaxLayer; layer++)
            {
                long[] neighbors = removedNode.Neighbors[layer];
                foreach (long neighborId in neighbors)
                {
                    if (_nodes.TryGetValue(neighborId, out var neighborNode))
                    {
                        RemoveFromNeighborList(neighborNode, id, layer);
                    }
                }
            }

            // If we removed the entry point, pick a new one
            if (_entryPoint?.Id == id)
            {
                _entryPoint = null;
                _maxLevel = 0;

                // Find the node with the highest layer to become new entry
                foreach (var (_, node) in _nodes)
                {
                    if (node.MaxLayer > _maxLevel || _entryPoint is null)
                    {
                        _maxLevel = node.MaxLayer;
                        _entryPoint = node;
                    }
                }
            }

            return true;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public IReadOnlyList<VectorSearchResult> Search(ReadOnlySpan<float> query, int k)
    {
        if (query.Length != _config.Dimensions)
        {
            throw new ArgumentException(
                $"Query vector has {query.Length} dimensions, index requires {_config.Dimensions}");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var ep = _entryPoint;
        if (ep is null)
            return [];

        float[] queryArr = query.ToArray();

        // Phase 1: Greedy traverse from top layer to layer 1
        var currentBest = ep;
        float currentDist = ComputeDistance(queryArr, ep.Vector);

        for (int layer = _maxLevel; layer > 0; layer--)
        {
            (currentBest, currentDist) = GreedyClosest(queryArr, currentBest, currentDist, layer);
        }

        // Phase 2: Search layer 0 with ef = max(efSearch, k)
        int ef = Math.Max(_config.EfSearch, k);
        var candidates = SearchLayer(queryArr, currentBest, ef, layer: 0);

        // Return top-k from candidates
        int resultCount = Math.Min(k, candidates.Count);
        var results = new VectorSearchResult[resultCount];
        for (int i = 0; i < resultCount; i++)
        {
            results[i] = candidates[i];
        }

        return results;
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_writeLock)
        {
            _nodes.Clear();
            _entryPoint = null;
            _maxLevel = 0;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Clear();
    }

    // ──────────────────────────────────────────────────────────────────
    // Persistence: Snapshot / Restore for serialization
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a read-only snapshot of the graph for serialization.
    /// </summary>
    public HnswSnapshot GetSnapshot()
    {
        var nodes = new List<HnswNodeSnapshot>(_nodes.Count);
        foreach (var (_, node) in _nodes)
        {
            nodes.Add(new HnswNodeSnapshot(node.Id, node.Vector, node.MaxLayer, node.Neighbors));
        }

        return new HnswSnapshot(_entryPoint?.Id ?? -1, nodes);
    }

    /// <summary>
    /// Restores a node directly into the graph during deserialization.
    /// Bypasses the normal insert algorithm — assumes neighbor lists are already correct.
    /// </summary>
    /// <param name="id">Node identifier.</param>
    /// <param name="vector">Vector data.</param>
    /// <param name="maxLayer">Maximum layer for this node.</param>
    /// <param name="neighbors">Pre-built neighbor arrays per layer.</param>
    /// <param name="isEntryPoint">Whether this node is the graph entry point.</param>
    public void RestoreNode(long id, float[] vector, int maxLayer, long[][] neighbors, bool isEntryPoint)
    {
        var node = new HnswNode(id, vector, maxLayer);
        node.Neighbors = neighbors;
        _nodes[id] = node;

        if (isEntryPoint || _entryPoint is null)
        {
            _entryPoint = node;
            if (maxLayer > _maxLevel)
                _maxLevel = maxLayer;
        }

        if (maxLayer > _maxLevel)
            _maxLevel = maxLayer;
    }

    // ──────────────────────────────────────────────────────────────────
    // Private: Core HNSW algorithm methods
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Greedy traversal at a single layer — walks to the closest node to the query.
    /// Returns the closest node and its distance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private (HnswNode Node, float Distance) GreedyClosest(
        float[] query, HnswNode startNode, float startDist, int layer)
    {
        var current = startNode;
        float currentDist = startDist;
        bool improved = true;

        while (improved)
        {
            improved = false;
            long[] neighbors = current.Neighbors.Length > layer
                ? current.Neighbors[layer]
                : [];

            foreach (long neighborId in neighbors)
            {
                if (_nodes.TryGetValue(neighborId, out var neighborNode))
                {
                    float dist = ComputeDistance(query, neighborNode.Vector);
                    if (dist < currentDist)
                    {
                        current = neighborNode;
                        currentDist = dist;
                        improved = true;
                    }
                }
            }
        }

        return (current, currentDist);
    }

    /// <summary>
    /// Searches a single layer for the ef nearest neighbors to the query vector.
    /// Uses a priority-queue-based beam search (candidate set + result set).
    /// Returns results sorted by ascending distance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private List<VectorSearchResult> SearchLayer(
        float[] query, HnswNode entryNode, int ef, int layer)
    {
        var visited = new HashSet<long> { entryNode.Id };
        float entryDist = ComputeDistance(query, entryNode.Vector);

        // Candidates: min-heap (closest first for expansion)
        var candidates = new PriorityQueue<long, float>();
        candidates.Enqueue(entryNode.Id, entryDist);

        // Results: max-heap via TopKHeap (worst at root for easy eviction)
        var results = new TopKHeap(ef);
        results.TryAdd(entryNode.Id, entryDist);

        while (candidates.Count > 0)
        {
            candidates.TryDequeue(out long candidateId, out float candidateDist);

            // If the closest candidate is farther than the worst result, we're done
            if (candidateDist > results.WorstDistance)
                break;

            if (!_nodes.TryGetValue(candidateId, out var candidateNode))
                continue;

            long[] neighbors = candidateNode.Neighbors.Length > layer
                ? candidateNode.Neighbors[layer]
                : [];

            foreach (long neighborId in neighbors)
            {
                if (!visited.Add(neighborId))
                    continue;

                if (!_nodes.TryGetValue(neighborId, out var neighborNode))
                    continue;

                float dist = ComputeDistance(query, neighborNode.Vector);

                // Add to results if there's room or it's better than the worst
                if (results.Count < ef || dist < results.WorstDistance)
                {
                    candidates.Enqueue(neighborId, dist);
                    results.TryAdd(neighborId, dist);
                }
            }
        }

        return [.. results.ToSortedArray()];
    }

    /// <summary>
    /// Selects the best neighbors from a candidate list using the simple heuristic:
    /// pick the closest maxConn candidates.
    /// </summary>
    private static long[] SelectNeighbors(
        float[] query, List<VectorSearchResult> candidates, int maxConn)
    {
        int count = Math.Min(candidates.Count, maxConn);
        var selected = new long[count];

        for (int i = 0; i < count; i++)
        {
            selected[i] = candidates[i].Id;
        }

        return selected;
    }

    /// <summary>
    /// Sets the neighbor list for a node at a specific layer (atomic replace).
    /// </summary>
    private static void SetNeighbors(HnswNode node, int layer, long[] neighborIds)
    {
        var neighbors = node.Neighbors;
        if (layer < neighbors.Length)
        {
            // Clone the outer array, replace the target layer, atomic swap
            var updated = new long[neighbors.Length][];
            Array.Copy(neighbors, updated, neighbors.Length);
            updated[layer] = neighborIds;
            node.Neighbors = updated;
        }
    }

    /// <summary>
    /// Adds a new neighbor to an existing node's connection list at the given layer.
    /// If the neighbor list exceeds maxConn, prunes the farthest connection.
    /// </summary>
    private void AddNeighborBidirectional(
        HnswNode node, long newNeighborId, float[] newNeighborVec, int layer, int maxConn)
    {
        var neighbors = node.Neighbors;
        if (layer >= neighbors.Length)
            return;

        long[] currentNeighbors = neighbors[layer];

        // Check if already connected
        if (Array.IndexOf(currentNeighbors, newNeighborId) >= 0)
            return;

        if (currentNeighbors.Length < maxConn)
        {
            // Room available — just append
            var expanded = new long[currentNeighbors.Length + 1];
            Array.Copy(currentNeighbors, expanded, currentNeighbors.Length);
            expanded[^1] = newNeighborId;
            SetNeighbors(node, layer, expanded);
        }
        else
        {
            // Full — evaluate whether new neighbor is better than the worst existing one
            float newDist = ComputeDistance(node.Vector, newNeighborVec);
            float worstDist = 0f;
            int worstIdx = -1;

            for (int i = 0; i < currentNeighbors.Length; i++)
            {
                if (_nodes.TryGetValue(currentNeighbors[i], out var existing))
                {
                    float dist = ComputeDistance(node.Vector, existing.Vector);
                    if (dist > worstDist)
                    {
                        worstDist = dist;
                        worstIdx = i;
                    }
                }
                else
                {
                    // Stale reference — replace it
                    worstIdx = i;
                    worstDist = float.MaxValue;
                }
            }

            if (worstIdx >= 0 && newDist < worstDist)
            {
                var replaced = new long[currentNeighbors.Length];
                Array.Copy(currentNeighbors, replaced, currentNeighbors.Length);
                replaced[worstIdx] = newNeighborId;
                SetNeighbors(node, layer, replaced);
            }
        }
    }

    /// <summary>
    /// Removes a node ID from another node's neighbor list at the given layer.
    /// </summary>
    private static void RemoveFromNeighborList(HnswNode node, long removeId, int layer)
    {
        var neighbors = node.Neighbors;
        if (layer >= neighbors.Length)
            return;

        long[] currentNeighbors = neighbors[layer];
        int idx = Array.IndexOf(currentNeighbors, removeId);
        if (idx < 0)
            return;

        // Build new array without the removed ID
        var updated = new long[currentNeighbors.Length - 1];
        int dest = 0;
        for (int i = 0; i < currentNeighbors.Length; i++)
        {
            if (i != idx)
            {
                updated[dest++] = currentNeighbors[i];
            }
        }

        SetNeighbors(node, layer, updated);
    }

    /// <summary>
    /// Generates a random layer for a new node using the standard HNSW probability
    /// distribution: floor(-ln(uniform) × levelMultiplier).
    /// </summary>
    private int RandomLevel()
    {
        double r;
        lock (_levelRng)
        {
            r = _levelRng.NextDouble();
        }

        // Avoid log(0)
        if (r < double.Epsilon)
            r = double.Epsilon;

        return (int)(-Math.Log(r) * _config.EffectiveLevelMultiplier);
    }

    /// <summary>
    /// Computes distance between two vectors using the configured distance function.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ComputeDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        return DistanceMetrics.Compute(a, b, _config.DistanceFunction);
    }
}
