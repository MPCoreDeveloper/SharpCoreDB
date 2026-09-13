// <copyright file="FusionTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Cases ported from munarium-datastore fusion.rs.
// </copyright>

using SharpCoreDB.VectorSearch.Fusion;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

/// <summary>Rank-based fusion: ordering, determinism and diagnostics.</summary>
public class FusionTests
{
    private const string LexDomain = "pg/ts_rank@1";
    private const string VectorDomain = "pg/cosine@1";
    private const string Bm25Domain = "tantivy/bm25@1";

    [Fact]
    public void A_document_in_both_legs_outranks_one_in_a_single_leg()
    {
        var lexical = new List<Candidate> { new("a", 9f), new("b", 8f), new("c", 7f) };
        var vector = new List<Candidate> { new("b", 0.1f), new("d", 0.2f) };

        IReadOnlyList<FusedHit> fused = ReciprocalRankFusion.Fuse(lexical, vector);

        Assert.Equal("b", fused[0].ChunkId);
        FusedHit b = fused.Single(h => h.ChunkId == "b");
        Assert.Equal(2, b.LexicalRank);
        Assert.Equal(1, b.VectorRank);
    }

    [Fact]
    public void Ties_break_on_chunk_id_so_results_are_reproducible()
    {
        var lexical = new List<Candidate> { new("z", 1f) };
        var vector = new List<Candidate> { new("a", 1f) };

        IReadOnlyList<FusedHit> fused = ReciprocalRankFusion.Fuse(lexical, vector);

        Assert.Equal("a", fused[0].ChunkId);
        Assert.Equal("z", fused[1].ChunkId);
    }

    [Fact]
    public void Leg_ranks_are_carried_for_leg_level_diagnostics()
    {
        var lexical = new List<Candidate> { new("x", 5f) };
        var vector = new List<Candidate> { new("y", 0.5f) };

        IReadOnlyList<FusedHit> fused = ReciprocalRankFusion.Fuse(lexical, vector);

        FusedHit x = fused.Single(h => h.ChunkId == "x");
        Assert.Equal(1, x.LexicalRank);
        Assert.Null(x.VectorRank);
        Assert.Equal(5f, x.LexicalScore);
        Assert.Null(x.VectorScore);
    }

    [Fact]
    public void Weights_move_the_legs_independently()
    {
        var lexical = new List<Candidate> { new("lex", 9f) };
        var vector = new List<Candidate> { new("vec", 0.1f) };

        IReadOnlyList<FusedHit> vectorHeavy = ReciprocalRankFusion.Fuse(lexical, vector, new FusionWeights(Lexical: 0.1, Vector: 5.0));
        Assert.Equal("vec", vectorHeavy[0].ChunkId);

        IReadOnlyList<FusedHit> lexicalHeavy = ReciprocalRankFusion.Fuse(lexical, vector, new FusionWeights(Lexical: 5.0, Vector: 0.1));
        Assert.Equal("lex", lexicalHeavy[0].ChunkId);
    }

    [Fact]
    public void Pools_that_mix_lexical_domains_interleave_by_rank_and_are_flagged()
    {
        // Postgres pool: raw ts_rank magnitudes ~0.2. Tantivy pool: BM25 magnitudes ~8.
        var candidates = new List<PoolCandidate>
        {
            new("pg-pool", 0, "pg-a", new Measure(LexDomain, 0.20), null),
            new("pg-pool", 1, "pg-b", new Measure(LexDomain, 0.15), null),
            new("tv-pool", 2, "tv-a", new Measure(Bm25Domain, 8.0), null),
            new("tv-pool", 3, "tv-b", new Measure(Bm25Domain, 6.0), null),
        };

        PoolMergeOutcome outcome = PoolMerge.FusePools(candidates, 4);

        Assert.True(outcome.Diagnostics.MixedDomain);
        Assert.Equal(2, outcome.Diagnostics.LexicalDomains.Count);
        Assert.Equal(PoolMerge.PolicyVersion, outcome.Diagnostics.PolicyVersion);

        // Raw comparison would put both Tantivy hits first (8.0 and 6.0 beat 0.20). Rank interleave
        // alternates the pools' rank-1s, then their rank-2s — magnitudes never decided anything.
        Assert.Equal(new[] { 0, 2, 1, 3 }, outcome.Ranked.Select(r => r.Ordinal).ToArray());
    }

    [Fact]
    public void A_single_domain_merge_is_not_flagged()
    {
        var candidates = new List<PoolCandidate>
        {
            new("a", 0, "a-0", new Measure(LexDomain, 0.5), new Measure(VectorDomain, 0.3)),
            new("b", 1, "b-0", new Measure(LexDomain, 0.4), new Measure(VectorDomain, 0.2)),
        };

        PoolMergeOutcome outcome = PoolMerge.FusePools(candidates, 5);

        Assert.False(outcome.Diagnostics.MixedDomain);
        Assert.Equal([LexDomain], outcome.Diagnostics.LexicalDomains);
        Assert.Equal([VectorDomain], outcome.Diagnostics.VectorDomains);
    }

    [Fact]
    public void A_shared_embedder_keeps_the_vector_leg_single_domain_and_raw_values_decide()
    {
        var candidates = new List<PoolCandidate>
        {
            new("pg-pool", 0, "pg-a", null, new Measure(VectorDomain, 0.2)),
            new("tv-pool", 1, "tv-a", null, new Measure(VectorDomain, -0.1)),
        };

        PoolMergeOutcome outcome = PoolMerge.FusePools(candidates, 5);

        Assert.False(outcome.Diagnostics.MixedDomain);
        // The closer vector leads: the raw distances decided.
        Assert.Equal(1, outcome.Ranked[0].Ordinal);
    }

    [Fact]
    public void A_NaN_measure_sorts_last_rather_than_making_the_order_arbitrary()
    {
        var candidates = new List<PoolCandidate>
        {
            new("p", 0, "nan", new Measure(LexDomain, double.NaN), null),
            new("p", 1, "real", new Measure(LexDomain, 0.5), null),
        };

        PoolMergeOutcome outcome = PoolMerge.FusePools(candidates, 5);

        Assert.Equal(1, outcome.Ranked[0].Ordinal);
    }
}
