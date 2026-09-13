// <copyright file="RoutingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Cases ported from munarium-datastore routing.rs.
// </copyright>

using SharpCoreDB.VectorSearch.Routing;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

/// <summary>Bounded, scale-free routing evidence and its ranking.</summary>
public class RoutingTests
{
    [Fact]
    public void Phrases_are_adjacent_content_words_only()
    {
        IReadOnlyList<(string First, string Second)> phrases =
            RoutingSignals.QueryPhrases("What cities did George Washington visit?");

        Assert.Equal(2, phrases.Count);
        Assert.Equal(("george", "washington"), phrases[0]);
        Assert.Equal(("washington", "visit"), phrases[1]);
    }

    [Fact]
    public void Content_terms_drop_stopwords()
    {
        IReadOnlyList<string> terms =
            RoutingSignals.ContentTerms("How did colonial newspapers report the Boston Tea Party?");

        Assert.Equal(
            new[] { "colonial", "newspapers", "report", "boston", "tea", "party" },
            terms);
    }

    [Fact]
    public void Multi_term_density_decides_when_phrases_are_absent()
    {
        const string query = "How did colonial newspapers report the Boston Tea Party?";
        IReadOnlyList<string> terms = RoutingSignals.ContentTerms(query);
        IReadOnlyList<(string First, string Second)> phrases = RoutingSignals.QueryPhrases(query);

        var narrativeTexts = new List<string>();
        for (int i = 0; i < 19; i++)
        {
            narrativeTexts.Add("the city and its press, described at length");
        }

        narrativeTexts.Add("reprinting what the colonial newspapers said of it");

        var newspaperTexts = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            newspaperTexts.Add("boston, december 20: the tea was destroyed last thursday");
        }

        RoutingEvidence narrative = RoutingEvidenceBuilder.Build(terms, phrases, narrativeTexts, 0.13);
        RoutingEvidence newspaper = RoutingEvidenceBuilder.Build(terms, phrases, newspaperTexts, 0.16);

        // Coverage ties them (each pool holds two of the six content terms); multi-term fraction does not.
        Assert.Equal(narrative.TermCoverage, newspaper.TermCoverage);
        Assert.True(newspaper.MultiTermFraction > 0.9);
        Assert.True(narrative.MultiTermFraction < 0.1);

        var pools = new List<(string Name, RoutingEvidence Evidence)>
        {
            ("narrative", narrative),
            ("newspaper", newspaper),
        };

        Assert.Equal(1, RoutingSignals.Rank(pools, 3.0)[0]);
        Assert.Equal(1, RoutingSignals.Rank(pools, 0.0)[0]);
    }

    [Fact]
    public void Empty_pools_are_excluded()
    {
        IReadOnlyList<string> terms = RoutingSignals.ContentTerms("anything");
        RoutingEvidence empty = RoutingEvidenceBuilder.Build(terms, [], []);
        RoutingEvidence full = RoutingEvidenceBuilder.Build(terms, [], ["anything at all"]);

        var pools = new List<(string Name, RoutingEvidence Evidence)>
        {
            ("empty", empty),
            ("full", full),
        };

        Assert.Equal(new[] { 1 }, RoutingSignals.Rank(pools, 3.0));
    }

    [Fact]
    public void Ties_break_deterministically_on_the_pool_name()
    {
        IReadOnlyList<string> terms = RoutingSignals.ContentTerms("alpha beta");
        string[] texts = ["alpha beta gamma"];
        RoutingEvidence a = RoutingEvidenceBuilder.Build(terms, [], texts, 1.0);
        RoutingEvidence b = RoutingEvidenceBuilder.Build(terms, [], texts, 1.0);

        var first = new List<(string Name, RoutingEvidence Evidence)> { ("zeta", a), ("m", b) };
        Assert.Equal(new[] { 1, 0 }, RoutingSignals.Rank(first, 0.0));

        var second = new List<(string Name, RoutingEvidence Evidence)> { ("b", a), ("a", b) };
        Assert.Equal(new[] { 1, 0 }, RoutingSignals.Rank(second, 0.0));
    }

    [Fact]
    public void Evidence_is_bounded_regardless_of_engine_magnitudes()
    {
        IReadOnlyList<string> terms = RoutingSignals.ContentTerms("boston tea");
        RoutingEvidence small = RoutingEvidenceBuilder.Build(terms, [], ["boston tea here"]);
        RoutingEvidence large = RoutingEvidenceBuilder.Build(terms, [], ["boston tea here"]);

        // Scale-free: identical evidence for identical text, whatever the engine's score magnitudes.
        Assert.Equal(small.TermCoverage, large.TermCoverage);
        Assert.InRange(small.TermCoverage, 0.0, 1.0);
        Assert.InRange(small.MultiTermFraction, 0.0, 1.0);
        Assert.InRange(small.PhraseFraction, 0.0, 1.0);
    }
}
