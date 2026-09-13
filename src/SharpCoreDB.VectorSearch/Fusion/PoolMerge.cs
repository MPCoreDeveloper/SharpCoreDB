// <copyright file="PoolMerge.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore fusion.rs (fuse_pools).
// </copyright>

namespace SharpCoreDB.VectorSearch.Fusion;

/// <summary>
/// A measurement carrying the <b>domain</b> that produced it (e.g. <c>pg/ts_rank@1</c> vs
/// <c>tantivy/bm25@1</c>). Two measurements from different domains are never numerically compared.
/// </summary>
public sealed record Measure(string Domain, double Value);

/// <summary>One candidate from one pool, with the optional per-leg measurements.</summary>
public sealed record PoolCandidate(string Pool, int Ordinal, string ChunkId, Measure? Lexical, Measure? Vector);

/// <summary>Per-leg weights for a pool merge.</summary>
public sealed record PoolMergeWeights(double Lexical = 1.0, double Vector = 1.0, double RrfK = 60.0);

/// <summary>What the merge observed, so a recorded result stays interpretable.</summary>
public sealed record PoolMergeDiagnostics(
    bool MixedDomain,
    IReadOnlyList<string> LexicalDomains,
    IReadOnlyList<string> VectorDomains,
    uint PolicyVersion);

/// <summary>The merged ranking (original ordinals, best first) plus its diagnostics.</summary>
public sealed record PoolMergeOutcome(IReadOnlyList<(int Ordinal, double Score)> Ranked, PoolMergeDiagnostics Diagnostics);

/// <summary>
/// Domain-aware merging of a multi-pool (multi-engine) search.
/// </summary>
/// <remarks>
/// When every measurement in a leg comes from ONE domain, raw magnitudes are sound and decide the
/// order. The moment a leg mixes two engines (PostgreSQL <c>ts_rank</c> and BM25 have wildly
/// different magnitudes), comparing raw values would silently route by engine rather than by
/// relevance — so the merge falls back to interleaving by each pool's own internal rank, and the
/// diagnostics record that it happened.
/// </remarks>
public static class PoolMerge
{
    /// <summary>The pool-merge policy version.</summary>
    public const uint PolicyVersion = 1;

    /// <summary>Merge pool candidates, best first.</summary>
    /// <param name="candidates">Candidates from every pool.</param>
    /// <param name="limit">Maximum number of ranked ordinals to return.</param>
    /// <param name="weights">Optional per-leg weights.</param>
    public static PoolMergeOutcome FusePools(
        IReadOnlyList<PoolCandidate> candidates,
        int limit,
        PoolMergeWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        weights ??= new PoolMergeWeights();

        IReadOnlyList<string> lexicalDomains = DistinctDomains(candidates, static c => c.Lexical);
        IReadOnlyList<string> vectorDomains = DistinctDomains(candidates, static c => c.Vector);
        bool mixed = lexicalDomains.Count > 1 || vectorDomains.Count > 1;

        var diagnostics = new PoolMergeDiagnostics(mixed, lexicalDomains, vectorDomains, PolicyVersion);

        if (limit <= 0 || candidates.Count == 0)
        {
            return new PoolMergeOutcome([], diagnostics);
        }

        IReadOnlyList<(int Ordinal, double Score)> ranked = mixed
            ? FuseByRank(candidates, limit, weights)
            : FuseByRawValue(candidates, limit, weights);

        return new PoolMergeOutcome(ranked, diagnostics);
    }

    private static IReadOnlyList<string> DistinctDomains(
        IReadOnlyList<PoolCandidate> candidates,
        Func<PoolCandidate, Measure?> selector)
    {
        var domains = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PoolCandidate candidate in candidates)
        {
            Measure? measure = selector(candidate);
            if (measure is not null && seen.Add(measure.Domain))
            {
                domains.Add(measure.Domain);
            }
        }

        return domains;
    }

    private static IReadOnlyList<(int Ordinal, double Score)> FuseByRawValue(
        IReadOnlyList<PoolCandidate> candidates,
        int limit,
        PoolMergeWeights weights)
    {
        Dictionary<int, int> lexicalRanks = Ranks(candidates, static c => c.Lexical, static m => m.Value, descending: true);
        Dictionary<int, int> vectorRanks = Ranks(candidates, static c => c.Vector, static m => m.Value, descending: false);

        var scored = new List<(int Ordinal, double Score, string ChunkId)>(candidates.Count);
        foreach (PoolCandidate candidate in candidates)
        {
            double score = 0.0;
            if (lexicalRanks.TryGetValue(candidate.Ordinal, out int lexicalRank))
            {
                score += weights.Lexical / (weights.RrfK + lexicalRank);
            }

            if (vectorRanks.TryGetValue(candidate.Ordinal, out int vectorRank))
            {
                score += weights.Vector / (weights.RrfK + vectorRank);
            }

            scored.Add((candidate.Ordinal, score, candidate.ChunkId));
        }

        scored.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.Ordinal.CompareTo(b.Ordinal);
        });

        return scored.Take(limit).Select(static s => (s.Ordinal, s.Score)).ToList();
    }

    private static Dictionary<int, int> Ranks(
        IReadOnlyList<PoolCandidate> candidates,
        Func<PoolCandidate, Measure?> selector,
        Func<Measure, double> value,
        bool descending)
    {
        var ordered = new List<PoolCandidate>();
        foreach (PoolCandidate candidate in candidates)
        {
            if (selector(candidate) is not null)
            {
                ordered.Add(candidate);
            }
        }

        ordered.Sort((a, b) =>
        {
            double va = value(selector(a)!);
            double vb = value(selector(b)!);
            int byValue = descending ? vb.CompareTo(va) : va.CompareTo(vb);
            return byValue != 0 ? byValue : a.Ordinal.CompareTo(b.Ordinal);
        });

        var ranks = new Dictionary<int, int>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            ranks[ordered[i].Ordinal] = i + 1;
        }

        return ranks;
    }

    private static IReadOnlyList<(int Ordinal, double Score)> FuseByRank(
        IReadOnlyList<PoolCandidate> candidates,
        int limit,
        PoolMergeWeights weights)
    {
        var poolIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PoolCandidate candidate in candidates)
        {
            if (!poolIndex.ContainsKey(candidate.Pool))
            {
                poolIndex[candidate.Pool] = poolIndex.Count;
            }
        }

        var entries = new List<(int Ordinal, int Rank, int Pool, double Score)>(candidates.Count);
        foreach (IGrouping<string, PoolCandidate> group in candidates.GroupBy(static c => c.Pool, StringComparer.Ordinal))
        {
            var poolCandidates = group.ToList();
            Dictionary<int, int> lexicalRanks = Ranks(poolCandidates, static c => c.Lexical, static m => m.Value, descending: true);
            Dictionary<int, int> vectorRanks = Ranks(poolCandidates, static c => c.Vector, static m => m.Value, descending: false);

            foreach (PoolCandidate candidate in poolCandidates)
            {
                int best = int.MaxValue;
                if (lexicalRanks.TryGetValue(candidate.Ordinal, out int lexicalRank))
                {
                    best = Math.Min(best, lexicalRank);
                }

                if (vectorRanks.TryGetValue(candidate.Ordinal, out int vectorRank))
                {
                    best = Math.Min(best, vectorRank);
                }

                double score = best == int.MaxValue ? 0.0 : 1.0 / (weights.RrfK + best);
                entries.Add((candidate.Ordinal, best, poolIndex[candidate.Pool], score));
            }
        }

        entries.Sort(static (a, b) =>
        {
            int byRank = a.Rank.CompareTo(b.Rank);
            if (byRank != 0)
            {
                return byRank;
            }

            int byPool = a.Pool.CompareTo(b.Pool);
            return byPool != 0 ? byPool : a.Ordinal.CompareTo(b.Ordinal);
        });

        return entries.Take(limit).Select(static e => (e.Ordinal, e.Score)).ToList();
    }
}
