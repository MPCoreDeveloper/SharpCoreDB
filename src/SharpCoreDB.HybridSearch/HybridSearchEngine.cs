// <copyright file="HybridSearchEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.HybridSearch;

using System.Globalization;
using SharpCoreDB.Search.Lexical;
using SharpCoreDB.VectorSearch;
using SharpCoreDB.VectorSearch.Fusion;

/// <summary>Per-leg knobs for a hybrid query.</summary>
public sealed record HybridSearchOptions
{
    /// <summary>How many candidates each leg contributes before fusion. Default 100.</summary>
    public int LegLimit { get; init; } = 100;

    /// <summary>Weight applied to the lexical leg's reciprocal rank.</summary>
    public double LexicalWeight { get; init; } = 1.0;

    /// <summary>Weight applied to the vector leg's reciprocal rank.</summary>
    public double VectorWeight { get; init; } = 1.0;

    /// <summary>The RRF constant (60 is conventional).</summary>
    public double RrfK { get; init; } = 60.0;
}

/// <summary>One fused hit, with its per-leg provenance.</summary>
public sealed record HybridHit(
    long Id,
    double Score,
    int? LexicalRank,
    int? VectorRank,
    float? LexicalScore,
    float? VectorScore);

/// <summary>The fused result plus how many candidates each leg produced.</summary>
public sealed record HybridSearchResult(
    IReadOnlyList<HybridHit> Hits,
    int LexicalCandidateCount,
    int VectorCandidateCount);

/// <summary>
/// Hybrid (lexical + vector) retrieval: run both legs, then fuse by rank.
/// </summary>
/// <remarks>
/// Either leg may be absent. A document that appears in both legs is promoted by RRF exactly because
/// two independent retrieval methods agreed on it — which is the whole point of the fusion, and why
/// the legs must stay independent (no adapter is allowed to hide fusion inside its own search).
/// </remarks>
public sealed class HybridSearchEngine
{
    private readonly FullTextIndex? _lexical;
    private readonly IVectorIndex? _vector;
    private readonly HybridSearchOptions _options;

    /// <summary>Initialize the engine over an optional lexical leg and an optional vector leg.</summary>
    public HybridSearchEngine(
        FullTextIndex? lexical = null,
        IVectorIndex? vector = null,
        HybridSearchOptions? options = null)
    {
        if (lexical is null && vector is null)
        {
            throw new ArgumentException("a hybrid engine needs at least one leg (lexical or vector)");
        }

        _lexical = lexical;
        _vector = vector;
        _options = options ?? new HybridSearchOptions();
    }

    /// <summary>Run the configured legs and fuse them.</summary>
    /// <param name="query">The lexical query (optional when only the vector leg is configured).</param>
    /// <param name="embedding">The query embedding (optional when only the lexical leg is configured).</param>
    /// <param name="k">Maximum fused hits to return.</param>
    public HybridSearchResult Search(string? query, float[]? embedding, int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        IReadOnlyList<Candidate> lexicalCandidates = [];
        if (_lexical is not null && !string.IsNullOrWhiteSpace(query))
        {
            var lexical = new List<Candidate>();
            foreach (LexicalHit hit in _lexical.Search(query, _options.LegLimit))
            {
                lexical.Add(new Candidate(Key(hit.Id), hit.Score));
            }

            lexicalCandidates = lexical;
        }

        IReadOnlyList<Candidate> vectorCandidates = [];
        if (_vector is not null && embedding is not null)
        {
            var vector = new List<Candidate>();
            foreach (VectorSearchResult hit in _vector.Search(embedding, _options.LegLimit))
            {
                vector.Add(new Candidate(Key(hit.Id), hit.Distance));
            }

            vectorCandidates = vector;
        }

        var weights = new FusionWeights(_options.LexicalWeight, _options.VectorWeight, _options.RrfK);
        IReadOnlyList<FusedHit> fused = ReciprocalRankFusion.Fuse(lexicalCandidates, vectorCandidates, weights);

        var hits = new List<HybridHit>(Math.Min(k, fused.Count));
        foreach (FusedHit hit in fused)
        {
            if (hits.Count == k)
            {
                break;
            }

            hits.Add(new HybridHit(
                long.Parse(hit.ChunkId, CultureInfo.InvariantCulture),
                hit.Score,
                hit.LexicalRank,
                hit.VectorRank,
                hit.LexicalScore,
                hit.VectorScore));
        }

        return new HybridSearchResult(hits, lexicalCandidates.Count, vectorCandidates.Count);
    }

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
}
