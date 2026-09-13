// <copyright file="MunariumTokenizerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Vectors ported from munarium-datastore tokenizer.rs.
// </copyright>

using SharpCoreDB.Search.Lexical;
using Xunit;

namespace SharpCoreDB.Search.Tests;

/// <summary>Classifying-tokenizer contract vectors.</summary>
public class MunariumTokenizerTests
{
    private static string[] Toks(string input)
        => MunariumTokenizer.Tokenize(input).Select(t => t.Text).ToArray();

    public static TheoryData<string, string[]> ClassifyingVectors => new()
    {
        { "CVE-2025-31337", ["CVE", "-2025", "-31337"] },
        { "ABC-1234-XY", ["ABC", "-1234", "XY"] },
        { "dv-0003", ["dv", "-0003"] },
        { "+3.5", ["+3.5"] },
        { "900000.50", ["900000.50"] },
        { "900000.5", ["900000.5"] },
        { "4.2.1", ["4.2.1"] },
        { "198.51.100.140", ["198.51.100.140"] },
        { "v4.2.1", ["v4.2.1"] },
        { "07/638,337", ["07/638", "337"] },
        { "10/123,456 over US7654321B2", ["10/123", "456", "over", "US7654321B2"] },
        { "0.1%", ["0.1"] },
        { "$0.00", ["0.00"] },
        { "21 CFR 211.100", ["21", "CFR", "211.100"] },
        { "Tolerance 1.2e-9 metres", ["Tolerance", "1.2e-9", "metres"] },
        { "45eagles", ["45eagles"] },
        { "12elephants walked", ["12elephants", "walked"] },
        { "2e9", ["2e9"] },
        { "1.2e-9", ["1.2e-9"] },
        { "1.2e-9x", ["1.2", "e", "-9", "x"] },
        { "The continental congress met in Philadelphia.", ["The", "continental", "congress", "met", "in", "Philadelphia"] },
        { "café naïve", ["café", "naïve"] },
    };

    [Theory]
    [MemberData(nameof(ClassifyingVectors))]
    public void Classifying_vectors_match(string input, string[] expected)
        => Assert.Equal(expected, Toks(input));

    [Theory]
    [InlineData("")]
    [InlineData("   .,;!  ")]
    public void Punctuation_only_input_emits_nothing(string input)
        => Assert.Empty(Toks(input));

    [Fact]
    public void Hyphenated_compounds_emit_whole_then_parts()
    {
        Assert.Equal(new[] { "well-established", "well", "established" }, Toks("well-established"));
        Assert.Equal(
            new[] { "cloud-native", "cloud", "native", "multi-tenant", "multi", "tenant" },
            Toks("cloud-native multi-tenant"));
        Assert.Equal(new[] { "state-of-art", "state", "of", "art" }, Toks("state-of-art"));
    }

    [Fact]
    public void A_decimal_and_its_short_form_are_different_tokens()
        => Assert.NotEqual(Toks("900000.50"), Toks("900000.5"));

    [Fact]
    public void Positions_count_compounds_and_parts_separately()
    {
        IReadOnlyList<LexicalToken> tokens = MunariumTokenizer.Tokenize("well-established fact");

        Assert.Equal(new[] { 0, 1, 2, 3 }, tokens.Select(t => t.Position).ToArray());
        Assert.Equal("fact", tokens[3].Text);
    }

    [Fact]
    public void Offsets_are_exact_source_spans()
    {
        const string text = "see CVE-2025-31337 now";

        foreach (LexicalToken token in MunariumTokenizer.Tokenize(text))
        {
            Assert.Equal(token.Text, text[token.OffsetFrom..token.OffsetTo]);
        }
    }

    [Fact]
    public void Source_casing_is_preserved_for_the_analyzer_to_fold()
    {
        // The tokenizer does not case-fold; the analyzer decides what folding means.
        Assert.Equal(new[] { "The", "Continental" }, Toks("The Continental"));
    }
}
