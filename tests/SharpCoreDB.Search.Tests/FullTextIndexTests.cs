// <copyright file="FullTextIndexTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using SharpCoreDB.Search.Lexical;
using Xunit;

namespace SharpCoreDB.Search.Tests;

/// <summary>Analyzer pipeline and BM25 full-text ranking.</summary>
public class FullTextIndexTests
{
    [Fact]
    public void The_analyzer_contract_version_is_recorded()
        => Assert.False(string.IsNullOrWhiteSpace(LexicalAnalyzer.ContractVersion));

    [Fact]
    public void The_analyzer_drops_stop_words()
    {
        IReadOnlyList<string> terms = LexicalAnalyzer.Terms("The Continental Congress met in Philadelphia");

        Assert.DoesNotContain("the", terms);
        Assert.DoesNotContain("in", terms);
        Assert.Contains("philadelphia", terms);
    }

    [Fact]
    public void Bm25_ranks_the_matching_document_first()
    {
        var index = new FullTextIndex();
        index.Add(1, "the city and its press, described at length");
        index.Add(2, "boston, december 20: the tea was destroyed last thursday");
        index.Add(3, "a travel narrative about a very long journey");

        IReadOnlyList<LexicalHit> hits = index.Search("boston tea", 3);

        Assert.Equal(3, index.DocumentCount);
        Assert.Equal(2, hits[0].Id);
    }

    [Fact]
    public void Stop_words_alone_produce_no_query_terms()
    {
        var index = new FullTextIndex();
        index.Add(1, "the city and its press");

        Assert.Empty(index.Search("the and of", 5));
    }

    [Fact]
    public void Stemming_makes_newspapers_match_newspaper()
    {
        var index = new FullTextIndex();
        index.Add(1, "reprinting what the colonial newspaper said of it");
        index.Add(2, "the city and its press, described at length");

        IReadOnlyList<LexicalHit> hits = index.Search("newspapers", 2);

        Assert.Equal(1, hits[0].Id);
    }

    [Fact]
    public void A_phrase_beats_the_same_terms_scattered()
    {
        var index = new FullTextIndex();
        index.Add(1, "the tea was destroyed and boston was quiet that week");
        index.Add(2, "boston tea was destroyed");

        IReadOnlyList<LexicalHit> hits = index.Search("boston tea", 2);

        Assert.Equal(2, hits[0].Id);
    }

    [Fact]
    public void Ties_break_on_id_so_results_are_reproducible()
    {
        var index = new FullTextIndex();
        index.Add(5, "alpha");
        index.Add(2, "alpha");

        IReadOnlyList<LexicalHit> hits = index.Search("alpha", 2);

        Assert.Equal(2, hits[0].Id);
        Assert.Equal(5, hits[1].Id);
    }

    [Fact]
    public void An_empty_index_answers_nothing()
    {
        var index = new FullTextIndex();

        Assert.Empty(index.Search("anything", 5));
        Assert.Equal(0, index.DocumentCount);
    }

    [Fact]
    public void A_non_matching_query_answers_nothing()
    {
        var index = new FullTextIndex();
        index.Add(1, "alpha beta gamma");

        Assert.Empty(index.Search("zulu", 5));
    }
}
