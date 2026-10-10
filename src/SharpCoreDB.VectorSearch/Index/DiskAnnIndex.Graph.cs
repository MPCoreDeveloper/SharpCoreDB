// <copyright file="DiskAnnIndex.Graph.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.VectorSearch.Index;

using SharpCoreDB.VectorSearch;

/// <summary>Vamana graph construction for <see cref="DiskAnnIndex"/>.</summary>
public sealed partial class DiskAnnIndex
{
    private void EnsureBuilt()
    {
        lock (_writeLock)
        {
            if (_graph is null)
            {
                BuildGraph();
            }
        }
    }

    /// <summary>
    /// Build the Vamana graph: a seeded random R-regular initialization followed by
    /// <see cref="DiskAnnConfig.BuildPasses"/> passes of greedy search + robust prune + back-edges,
    /// exactly as the DiskANN paper describes.
    /// </summary>
    private void BuildGraph()
    {
        int length = _length;
        var neighborSlots = new List<int>[length];
        for (int i = 0; i < length; i++)
        {
            neighborSlots[i] = new List<int>(_config.MaxNeighbors);
        }

        var liveSlots = new List<int>(_liveCount);
        for (int s = 0; s < length; s++)
        {
            if (_ids[s] != Tombstone)
            {
                liveSlots.Add(s);
            }
        }

        if (liveSlots.Count == 0)
        {
            _graph = new Graph(neighborSlots, -1, length);
            return;
        }

        int r = _config.MaxNeighbors;
        double alpha = _config.Alpha;
        int lBuild = _config.ConstructionSearchListSize;

        int entrySlot = ComputeMedoidSlot(liveSlots);

        // 1. Deterministic random R-regular initialization, so the graph has navigable structure
        //    before the first greedy search. Per-node seeded RNGs keep it deterministic AND parallel.
        Parallel.ForEach(
            liveSlots,
            s =>
            {
                var rng = new Random(RngSeed + s);
                for (int e = 0; e < r; e++)
                {
                    int t = liveSlots[rng.Next(liveSlots.Count)];
                    if (t != s)
                    {
                        AddEdgeUnique(neighborSlots, s, t, r);
                    }
                }
            });

        // 2. Vamana passes (sequential, evolving graph — see the note above).
        for (int pass = 0; pass < _config.BuildPasses; pass++)
        {
            // Phase A: for every node, search the current graph, robust-prune, and RECORD the
            // back-edges it wants instead of applying them one at a time.
            var backEdgesPerTarget = new List<int>[length];
            foreach (int s in liveSlots)
            {
                List<(int Slot, float Distance)> candidates = GreedySearchSlot(s, lBuild, neighborSlots, entrySlot);

                // The greedy search already returned the best of these, with their distances already
                // in `candidates`. Re-adding them puts DUPLICATE slots into the prune's working set,
                // and the dominance loop then computes the same pair twice: measured at ~13% of the
                // working set, worth ~14% of the prune's runtime (PERFORMANCE_NOTES.md, section 4).
                HashSet<int> hits = t_hitSetScratch ?? [];
                foreach (int existing in neighborSlots[s])
                {
                    if (existing != s && !hits.Contains(existing))
                    {
                        candidates.Add((existing, DistanceSlotToSlot(s, existing)));
                    }
                }

                List<int> pruned = RobustPrune(s, candidates, alpha, r);

                neighborSlots[s].Clear();
                foreach (int slot in pruned)
                {
                    neighborSlots[s].Add(slot);
                }

                foreach (int target in pruned)
                {
                    (backEdgesPerTarget[target] ??= []).Add(s);
                }
            }

            // Phase B: apply the recorded back-edges. Each target's list is touched by exactly one
            // task, so this parallelises cleanly and stays deterministic (the source order was
            // fixed by the sequential phase above).
            //
            // This is where the build's cost used to live: pruning after EVERY overflowing insertion
            // made a single target's list cost O(R^3) in distance computations. Batching the inserts
            // and pruning ONCE per target makes it O(R^2).
            Parallel.ForEach(
                liveSlots,
                t =>
                {
                    List<int>? sources = backEdgesPerTarget[t];
                    if (sources is null || sources.Count == 0)
                    {
                        return;
                    }

                    List<int> list = neighborSlots[t];
                    foreach (int source in sources)
                    {
                        if (source != t && !list.Contains(source))
                        {
                            list.Add(source);
                        }
                    }

                    if (list.Count <= r)
                    {
                        return;
                    }

                    var candidates = new List<(int Slot, float Distance)>(list.Count);
                    foreach (int slot in list)
                    {
                        candidates.Add((slot, DistanceSlotToSlot(t, slot)));
                    }

                    List<int> pruned = RobustPrune(t, candidates, alpha, r);
                    list.Clear();
                    foreach (int slot in pruned)
                    {
                        list.Add(slot);
                    }
                });

        }

        _graph = new Graph(neighborSlots, entrySlot, length);
    }

    /// <summary>
    /// The medoid: the live vertex closest to the centroid. Electing a real vertex keeps every
    /// indexed id returnable.
    /// </summary>
    private int ComputeMedoidSlot(List<int> liveSlots)
    {
        int dims = _config.Dimensions;
        var centroid = new float[dims];
        foreach (int s in liveSlots)
        {
            var v = _data.AsSpan(s * dims, dims);
            for (int d = 0; d < dims; d++)
            {
                centroid[d] += v[d];
            }
        }

        float inv = 1f / liveSlots.Count;
        for (int d = 0; d < dims; d++)
        {
            centroid[d] *= inv;
        }

        int best = liveSlots[0];
        float bestDistance = DistanceToSlot(centroid, best);
        foreach (int s in liveSlots)
        {
            float distance = DistanceToSlot(centroid, s);
            if (distance < bestDistance || (distance == bestDistance && _ids[s] < _ids[best]))
            {
                bestDistance = distance;
                best = s;
            }
        }

        return best;
    }
}
