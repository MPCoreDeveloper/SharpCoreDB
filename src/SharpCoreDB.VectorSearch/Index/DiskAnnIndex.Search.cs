// <copyright file="DiskAnnIndex.Search.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.VectorSearch.Index;

using SharpCoreDB.VectorSearch;

/// <summary>Greedy beam search for <see cref="DiskAnnIndex"/>.</summary>
public sealed partial class DiskAnnIndex
{
    // Per-thread traversal scratch: a query must not allocate its search state, and the buffers are
    // only ever live inside one search on a thread, so they cannot alias across searches.
    [ThreadStatic] private static PriorityQueue<int, float>? t_frontierScratch;
    [ThreadStatic] private static PriorityQueue<int, float>? t_bestScratch;
    [ThreadStatic] private static List<(int Slot, float Distance)>? t_hitsScratch;
    [ThreadStatic] private static HashSet<int>? t_hitSetScratch;
    // Visited marking uses a stamped int[] instead of a per-search HashSet. A beam search touches
    // its visited set once per neighbour it examines (~2,000 times per search at L_build=64/R=32),
    // and an array read is several times cheaper than hashing plus a bucket probe. The stamp is what
    // makes the reused array safe: an entry counts as visited only if it carries THIS search's id,
    // so stale marks from any earlier search (or from a differently sized index on the same thread)
    // are simply not equal to it. One array per thread, sized to the largest index that thread saw.
    [ThreadStatic] private static int[]? t_visitedStamp;
    [ThreadStatic] private static int t_visitedStampId;

    /// <inheritdoc />
    public IReadOnlyList<VectorSearchResult> Search(ReadOnlySpan<float> query, int k)
        => Search(query, k, _config.QuerySearchListSize);

    /// <summary>
    /// Searches with an explicit search-list (beam) size, overriding the configured floor. Raising
    /// the beam trades latency for recall without touching the graph; lowering it buys latency.
    /// </summary>
    /// <param name="query">The query vector. Length must equal <see cref="Dimensions"/>.</param>
    /// <param name="k">Maximum number of results to return.</param>
    /// <param name="searchListSize">Beam width for this query (must be positive).</param>
    /// <returns>Results ordered by ascending distance (closest first).</returns>
    public IReadOnlyList<VectorSearchResult> Search(ReadOnlySpan<float> query, int k, int searchListSize)
    {
        if (query.Length != _config.Dimensions)
        {
            throw new ArgumentException(
                $"Query vector has {query.Length} dimensions, index requires {_config.Dimensions}");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(searchListSize);

        if (_liveCount == 0)
        {
            return [];
        }

        Graph? graph = _graph;
        if (graph is null)
        {
            EnsureBuilt();
            graph = _graph;
        }

        if (graph is null || graph.EntrySlot < 0)
        {
            return [];
        }

        // The effective beam is the query floor, widened when the caller asks for more than the floor.
        int beam = Math.Max(k, searchListSize);
        List<(int Slot, float Distance)> hits = GreedySearchCore(query, beam, graph, excludeSlot: -1);

        int take = Math.Min(k, hits.Count);
        var results = new VectorSearchResult[take];
        int produced = 0;

        foreach ((int slot, float distance) in hits)
        {
            if (produced == take)
            {
                break;
            }

            long id = _ids[slot];
            if (id == Tombstone)
            {
                continue;
            }

            results[produced++] = new VectorSearchResult(id, distance);
        }

        if (produced == take)
        {
            return results;
        }

        var trimmed = new VectorSearchResult[produced];
        Array.Copy(results, trimmed, produced);
        return trimmed;
    }

    /// <summary>
    /// Greedy beam search from the entry point over the frozen graph. Works entirely in slots.
    /// </summary>
    /// <remarks>
    /// The returned list is per-thread scratch, valid until the next search on this thread; callers
    /// consume it immediately (both call sites do).
    /// </remarks>
    private List<(int Slot, float Distance)> GreedySearchCore(ReadOnlySpan<float> query, int beam, Graph graph, int excludeSlot)
    {
        List<(int Slot, float Distance)> hits = t_hitsScratch ??= [];
        hits.Clear();

        // Membership set of the same hits, so callers can test "was this slot already returned?"
        // in O(1) instead of rescanning the list.
        HashSet<int> hitSet = t_hitSetScratch ??= [];
        hitSet.Clear();

        if (graph.EntrySlot < 0 || graph.NeighborSlots.Length == 0 || excludeSlot == graph.EntrySlot)
        {
            return hits;
        }

        // Traversal state is per-THREAD scratch, cleared and reused: a search must not allocate its
        // search state. One search at a time per thread, so the buffers cannot alias.
        int[] visitedStamp = t_visitedStamp ??= [];
        if (visitedStamp.Length < graph.NeighborSlots.Length)
        {
            visitedStamp = new int[graph.NeighborSlots.Length];
            t_visitedStamp = visitedStamp;
            t_visitedStampId = 0;
        }

        int stamp = ++t_visitedStampId;
        if (stamp == int.MaxValue)
        {
            // A wrap would make stale marks look current, so start over on a cleared array.
            Array.Clear(visitedStamp);
            t_visitedStampId = stamp = 1;
        }

        visitedStamp[graph.EntrySlot] = stamp;

        PriorityQueue<int, float> frontier = t_frontierScratch ??= new PriorityQueue<int, float>();
        frontier.Clear();

        PriorityQueue<int, float> best = t_bestScratch ??= new PriorityQueue<int, float>();
        best.Clear();

        // Cosine evaluates every neighbour against the SAME query, so the query's squared norm is
        // hoisted out of the traversal: one reduction instead of one per candidate, and the
        // accumulation order is unchanged so the distances are bit-identical.
        bool cosine = _config.DistanceFunction == DistanceFunction.Cosine;
        float querySquaredNorm = cosine ? DistanceMetrics.SquaredNorm(query) : 0f;

        float entryDistance = DistanceTo(query, graph.EntrySlot, cosine, querySquaredNorm);
        frontier.Enqueue(graph.EntrySlot, entryDistance);
        best.Enqueue(graph.EntrySlot, -entryDistance);

        while (frontier.TryPeek(out int current, out float currentDistance))
        {
            // Once the closest unexpanded node is worse than the beam's worst hit, no improvement is possible.
            if (best.Count >= beam && best.TryPeek(out _, out float negativeWorst) && currentDistance > -negativeWorst)
            {
                break;
            }

            frontier.Dequeue();

            foreach (int neighbor in graph.NeighborSlots[current])
            {
                if (neighbor == excludeSlot || visitedStamp[neighbor] == stamp)
                {
                    continue;
                }

                visitedStamp[neighbor] = stamp;

                float distance = DistanceTo(query, neighbor, cosine, querySquaredNorm);
                if (best.Count < beam)
                {
                    frontier.Enqueue(neighbor, distance);
                    best.Enqueue(neighbor, -distance);
                }
                else
                {
                    best.TryPeek(out _, out float worst);
                    if (distance < -worst)
                    {
                        frontier.Enqueue(neighbor, distance);
                        best.Enqueue(neighbor, -distance);
                        best.Dequeue();
                    }
                }
            }
        }

        while (best.TryDequeue(out int slot, out float negativeDistance))
        {
            hits.Add((slot, -negativeDistance));
            hitSet.Add(slot);
        }

        hits.Sort(static (a, b) =>
        {
            int byDistance = a.Distance.CompareTo(b.Distance);
            return byDistance != 0 ? byDistance : a.Slot.CompareTo(b.Slot);
        });

        return hits;
    }

    /// <summary>Greedy search whose query is an indexed vector (used during construction).</summary>
    private List<(int Slot, float Distance)> GreedySearchSlot(int slot, int beam, List<int>[] neighborSlots, int entrySlot)
    {
        var query = _data.AsSpan(slot * _config.Dimensions, _config.Dimensions);
        return GreedySearchCore(query, beam, new Graph(neighborSlots, entrySlot, _length), slot);
    }
}
