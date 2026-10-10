// <copyright file="ReciprocalRankFusion.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore fusion.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Fusion;

/// <summary>
/// Per-leg weights for reciprocal-rank fusion. The defaults reproduce plain RRF over both legs.
/// </summary>
/// <param name="Lexical">Weight applied to the lexical leg's reciprocal rank.</param>
/// <param name="Vector">Weight applied to the vector leg's reciprocal rank.</param>
/// <param name="RrfK">
/// The RRF constant. 60 is the conventional value, chosen so results stay comparable across engines.
/// </param>
public sealed record FusionWeights(double Lexical = 1.0, double Vector = 1.0, double RrfK = 60.0);

/// <summary>
/// A fused hit, carrying the per-leg ranks and scores so a caller can report leg movement separately
/// from post-fusion movement ("the answer changed" and "one leg changed" are different findings).
/// </summary>
public sealed record FusedHit(
    string ChunkId,
    double Score,
    int? LexicalRank,
    int? VectorRank,
    float? LexicalScore,
    float? VectorScore);

/// <summary>
/// Reciprocal-rank fusion over two candidate lists.
/// </summary>
/// <remarks>
/// Fusion consumes <b>ranks</b>, never raw leg scores. PostgreSQL <c>ts_rank</c> and BM25 are not
/// numerically comparable, and neither is a BM25 score against a cosine distance; a merge that adds
/// them produces plausible results in the wrong order, silently. Each list must already be in its own
/// leg's best-first order — ranks are assigned from position, so handing over an unsorted list gives a
/// wrong answer rather than an error.
/// </remarks>
public static class ReciprocalRankFusion
{
    /// <summary>The fusion policy version; bump when the formula or the tie-break changes.</summary>
    public const uint PolicyVersion = 1;

    /// <summary>Fuse two best-first candidate lists.</summary>
    /// <param name="lexical">Lexical candidates, best-first (higher score first).</param>
    /// <param name="vector">Vector candidates, best-first (lower distance first).</param>
    /// <param name="weights">Optional per-leg weights.</param>
    /// <returns>Fused hits, best score first, ties broken by chunk id.</returns>
    public static IReadOnlyList<FusedHit> Fuse(
        IReadOnlyList<Candidate> lexical,
        IReadOnlyList<Candidate> vector,
        FusionWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(lexical);
        ArgumentNullException.ThrowIfNull(vector);
        weights ??= new FusionWeights();

        var builders = new Dictionary<string, FusedHitBuilder>(StringComparer.Ordinal);
        ApplyLeg(lexical, weights.Lexical, weights.RrfK, isLexical: true, builders);
        ApplyLeg(vector, weights.Vector, weights.RrfK, isLexical: false, builders);

        var hits = new List<FusedHit>(builders.Count);
        foreach (FusedHitBuilder builder in builders.Values)
        {
            hits.Add(builder.Build());
        }

        hits.Sort(static (a, b) =>
        {
            int byScore = Descending(a.Score, b.Score);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.ChunkId, b.ChunkId);
        });

        return hits;
    }

    private static void ApplyLeg(
        IReadOnlyList<Candidate> leg,
        double weight,
        double rrfK,
        bool isLexical,
        Dictionary<string, FusedHitBuilder> builders)
    {
        for (int i = 0; i < leg.Count; i++)
        {
            Candidate candidate = leg[i];
            if (string.IsNullOrEmpty(candidate.ChunkId))
            {
                continue;
            }

            int rank = i + 1; // 1-based; the caller guarantees best-first order
            if (!builders.TryGetValue(candidate.ChunkId, out FusedHitBuilder? builder))
            {
                builder = new FusedHitBuilder(candidate.ChunkId);
                builders[candidate.ChunkId] = builder;
            }

            builder.Score += weight / (rrfK + rank);

            if (isLexical)
            {
                builder.LexicalRank ??= rank;
                builder.LexicalScore ??= candidate.Score;
            }
            else
            {
                builder.VectorRank ??= rank;
                builder.VectorScore ??= candidate.Score;
            }
        }
    }

    /// <summary>
    /// Descending order over scores that may not be finite, as a TOTAL order. A NaN compares equal to
    /// everything under <c>CompareTo</c>, which is not transitive and makes sort order arbitrary; here
    /// it sorts LAST, deterministically.
    /// </summary>
    private static int Descending(double a, double b)
    {
        double keyA = double.IsNaN(a) ? double.NegativeInfinity : a;
        double keyB = double.IsNaN(b) ? double.NegativeInfinity : b;
        return keyB.CompareTo(keyA);
    }

    private sealed class FusedHitBuilder(string chunkId)
    {
        public string ChunkId { get; } = chunkId;

        public double Score { get; set; }

        public int? LexicalRank { get; set; }

        public int? VectorRank { get; set; }

        public float? LexicalScore { get; set; }

        public float? VectorScore { get; set; }

        public FusedHit Build() => new(ChunkId, Score, LexicalRank, VectorRank, LexicalScore, VectorScore);
    }
}
