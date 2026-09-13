// <copyright file="DiskAnnIndex.Prune.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.VectorSearch.Index;

using SharpCoreDB.VectorSearch;

/// <summary>Robust prune, edge helpers and distance helpers for <see cref="DiskAnnIndex"/>.</summary>
/// <remarks>
/// Everything here works in <b>slots</b>, not ids: admission, pruning and traversal never touch the
/// id-to-slot map, which is the single hottest lookup in a beam search.
/// </remarks>
public sealed partial class DiskAnnIndex
{
    /// <summary>
    /// Vamana's RobustPrune: greedily keep the closest candidate, then drop every candidate it
    /// already covers (<c>alpha * d(best, p') &lt;= d(self, p')</c>). A larger alpha keeps
    /// longer-range edges, which is what buys recall.
    /// </summary>
    private List<int> RobustPrune(int slot, List<(int Slot, float Distance)> candidates, double alpha, int r)
    {
        var ordered = new List<(int Slot, float Distance)>(candidates.Count);
        foreach ((int candidateSlot, float distance) in candidates)
        {
            if (candidateSlot != slot && _ids[candidateSlot] != Tombstone)
            {
                ordered.Add((candidateSlot, distance));
            }
        }

        ordered.Sort(static (a, b) =>
        {
            int byDistance = a.Distance.CompareTo(b.Distance);
            return byDistance != 0 ? byDistance : a.Slot.CompareTo(b.Slot);
        });

        var result = new List<int>(r);
        while (ordered.Count > 0 && result.Count < r)
        {
            (int slot, float distance) best = ordered[0];
            ordered.RemoveAt(0);
            result.Add(best.slot);

            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                float bestToCandidate = DistanceSlotToSlot(best.slot, ordered[i].Slot);
                if (alpha * bestToCandidate <= ordered[i].Distance)
                {
                    ordered.RemoveAt(i);
                }
            }
        }

        return result;
    }

    private static void AddEdgeUnique(List<int>[] neighborSlots, int fromSlot, int toSlot, int r)
    {
        List<int> list = neighborSlots[fromSlot];
        if (list.Count >= r || list.Contains(toSlot))
        {
            return;
        }

        list.Add(toSlot);
    }

    private float DistanceSlotToSlot(int a, int b)
        => DistanceMetrics.Compute(SlotVector(a), SlotVector(b), _config.DistanceFunction);

    private ReadOnlySpan<float> SlotVector(int slot)
        => _data.AsSpan(slot * _config.Dimensions, _config.Dimensions);

    private float DistanceToSlot(ReadOnlySpan<float> query, int slot)
        => DistanceMetrics.Compute(query, SlotVector(slot), _config.DistanceFunction);

    /// <summary>
    /// Distance from a query to a slot, reusing the query's pre-computed squared norm when the metric
    /// is cosine (a search evaluates thousands of candidates against one query, so that term is
    /// constant across the traversal).
    /// </summary>
    private float DistanceTo(ReadOnlySpan<float> query, int slot, bool cosine, float querySquaredNorm)
        => cosine
            ? DistanceMetrics.CosineDistanceWithQuerySquaredNorm(query, SlotVector(slot), querySquaredNorm)
            : DistanceMetrics.Compute(query, SlotVector(slot), _config.DistanceFunction);
}
