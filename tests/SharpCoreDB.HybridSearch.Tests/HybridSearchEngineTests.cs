// <copyright file="HybridSearchEngineTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using SharpCoreDB.Search.Lexical;
using SharpCoreDB.VectorSearch;
using SharpCoreDB.VectorSearch.Index;
using Xunit;

namespace SharpCoreDB.HybridSearch.Tests;

/// <summary>Hybrid retrieval: the two legs run independently, then fuse by rank.</summary>
public class HybridSearchEngineTests
{
    private const int Dimensions = 4;

    private static FullTextIndex Lexical(params (long Id, string Text)[] docs)
    {
        var index = new FullTextIndex();
        foreach ((long id, string text) in docs)
        {
            index.Add(id, text);
        }

        return index;
    }

    private static FlatIndex Vector(params (long Id, float[] Vector)[] docs)
    {
        var index = new FlatIndex(Dimensions, DistanceFunction.Cosine);
        foreach ((long id, float[] vector) in docs)
        {
            index.Add(id, vector);
        }

        return index;
    }

    [Fact]
    public void A_document_both_legs_agree_on_ranks_first()
    {
        // Doc 2 is lexical-best and vector-best (nearest to the query embedding).
        FullTextIndex lexical = Lexical(
            (1, "the city and its press, described at length"),
            (2, "boston tea destroyed"),
            (3, "a travel narrative"));

        using FlatIndex vector = Vector(
            (1, [1.0f, 0.0f, 0.0f, 0.0f]),
            (2, [0.0f, 1.0f, 0.0f, 0.0f]),
            (3, [0.0f, 0.0f, 1.0f, 0.0f]));

        var engine = new HybridSearchEngine(lexical, vector);
        HybridSearchResult result = engine.Search("boston tea", [0.0f, 1.0f, 0.0f, 0.0f], 3);

        Assert.Equal(2, result.Hits[0].Id);
        Assert.NotNull(result.Hits[0].LexicalRank);
        Assert.NotNull(result.Hits[0].VectorRank);
        Assert.True(result.LexicalCandidateCount > 0);
        Assert.True(result.VectorCandidateCount > 0);
    }

    [Fact]
    public void The_lexical_leg_alone_is_enough()
    {
        FullTextIndex lexical = Lexical((1, "alpha beta"), (2, "gamma delta"));
        var engine = new HybridSearchEngine(lexical);

        HybridSearchResult result = engine.Search("alpha", null, 5);

        Assert.Equal(1, result.Hits[0].Id);
        Assert.Null(result.Hits[0].VectorRank);
        Assert.Equal(0, result.VectorCandidateCount);
    }

    [Fact]
    public void The_vector_leg_alone_is_enough()
    {
        using FlatIndex vector = Vector((7, [0.0f, 1.0f, 0.0f, 0.0f]), (8, [1.0f, 0.0f, 0.0f, 0.0f]));
        var engine = new HybridSearchEngine(vector: vector);

        HybridSearchResult result = engine.Search(null, [0.0f, 1.0f, 0.0f, 0.0f], 5);

        Assert.Equal(7, result.Hits[0].Id);
        Assert.Null(result.Hits[0].LexicalRank);
        Assert.Equal(0, result.LexicalCandidateCount);
    }

    [Fact]
    public void A_stop_word_only_query_simply_produces_no_lexical_candidates()
    {
        FullTextIndex lexical = Lexical((1, "alpha beta"));
        using FlatIndex vector = Vector((1, [0.0f, 1.0f, 0.0f, 0.0f]));
        var engine = new HybridSearchEngine(lexical, vector);

        HybridSearchResult result = engine.Search("the and of", [0.0f, 1.0f, 0.0f, 0.0f], 5);

        Assert.Equal(0, result.LexicalCandidateCount);
        Assert.Equal(1, result.Hits[0].Id);
    }

    [Fact]
    public void Per_leg_scores_are_carried_on_every_hit()
    {
        FullTextIndex lexical = Lexical((1, "boston tea"));
        using FlatIndex vector = Vector((1, [0.0f, 1.0f, 0.0f, 0.0f]));
        var engine = new HybridSearchEngine(lexical, vector);

        HybridSearchResult result = engine.Search("boston", [0.0f, 1.0f, 0.0f, 0.0f], 1);
        HybridHit hit = result.Hits[0];

        Assert.NotNull(hit.LexicalScore);
        Assert.NotNull(hit.VectorScore);
        Assert.True(hit.LexicalScore > 0);
    }

    [Fact]
    public void An_engine_needs_at_least_one_leg()
        => Assert.Throws<ArgumentException>(() => new HybridSearchEngine());
}
