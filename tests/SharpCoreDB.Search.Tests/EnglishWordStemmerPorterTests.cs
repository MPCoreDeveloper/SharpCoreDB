// <copyright file="EnglishWordStemmerPorterTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Search.Tests;

using SharpCoreDB.Search.Lexical;
using Xunit;

/// <summary>
/// Porter conformance of <see cref="EnglishWordStemmer"/>: the word pairs from Porter's own paper, the
/// idempotence invariant, and the word-only contract that keeps digit-bearing tokens out of stemming.
/// Added with the mutation baseline — 112 mutants survived. The existing tests checked a handful of words,
/// so a broken suffix table, a wrong measure boundary or an inverted predicate stayed green; a stemmer that
/// disagrees with the reference silently splits an index (indexed value ≠ queried value).
/// </summary>
public sealed class EnglishWordStemmerPorterTests
{
    /// <summary>
    /// The classic examples from Porter, "An algorithm for suffix stripping" (1980), one per rule, plus the
    /// pairs every Porter port is checked against.
    /// </summary>
    public static TheoryData<string, string> PorterPairs => new()
    {
        // step 1a
        { "caresses", "caress" },
        { "ponies", "poni" },
        { "ties", "ti" },
        { "caress", "caress" },
        { "cats", "cat" },

        // step 1b
        { "feed", "feed" },
        { "agreed", "agree" },
        { "plastered", "plaster" },
        { "bled", "bled" },
        { "motoring", "motor" },
        { "sing", "sing" },
        { "conflated", "conflat" },
        { "troubled", "trouble" },
        { "sized", "size" },
        { "hopping", "hop" },
        { "tanned", "tan" },
        { "falling", "fall" },
        { "hissing", "hiss" },
        { "fizzed", "fizz" },
        { "failing", "fail" },
        { "filing", "file" },

        // step 1c
        { "happy", "happi" },
        { "sky", "sky" },

        // step 2
        { "relational", "relat" },
        { "conditional", "condit" },
        { "rational", "ration" },
        { "valenci", "valenc" },
        { "hesitanci", "hesit" },
        { "digitizer", "digit" },
        { "conformabli", "conform" },
        { "radicalli", "radic" },
        { "differentli", "differ" },
        { "vileli", "vile" },
        { "analogousli", "analog" },
        { "vietnamization", "vietnam" },
        { "predication", "predic" },
        { "operator", "oper" },
        { "feudalism", "feudal" },
        { "decisiveness", "decis" },
        { "hopefulness", "hope" },
        { "callousness", "callous" },
        { "formaliti", "formal" },
        { "sensitiviti", "sensit" },
        { "sensibiliti", "sensibl" },

        // step 3
        { "triplicate", "triplic" },
        { "formative", "form" },
        { "formalize", "formal" },
        { "electriciti", "electr" },
        { "electrical", "electr" },
        { "hopeful", "hope" },
        { "goodness", "good" },

        // step 4
        { "revival", "reviv" },
        { "allowance", "allow" },
        { "inference", "infer" },
        { "airliner", "airlin" },
        { "gyroscopic", "gyroscop" },
        { "adjustable", "adjust" },
        { "defensible", "defens" },
        { "irritant", "irrit" },
        { "replacement", "replac" },
        { "adjustment", "adjust" },
        { "dependent", "depend" },
        { "adoption", "adopt" },
        { "homologou", "homolog" },
        { "communism", "commun" },
        { "activate", "activ" },
        { "angulariti", "angular" },
        { "homologous", "homolog" },
        { "effective", "effect" },
        { "bowdlerize", "bowdler" },

        // step 5
        { "probate", "probat" },
        { "rate", "rate" },
        { "cease", "cease" },
        { "controll", "control" },
        { "roll", "roll" },
    };

    [Theory]
    [MemberData(nameof(PorterPairs))]
    public void Stem_MatchesThePorterReference(string word, string expected)
    {
        Assert.Equal(expected, EnglishWordStemmer.Stem(word));
    }

    [Theory]
    [MemberData(nameof(PorterPairs))]
    public void Stem_ReachesAFixedPointWithinTwoRounds(string word, string expected)
    {
        // Porter is NOT idempotent: step 1a strips a final "s" unconditionally, so a stem such as
        // "decis" (from "decisiveness") becomes "deci" when stemmed again. What must hold is that the
        // second round is the last one — otherwise a re-index and the existing index could keep drifting.
        var once = EnglishWordStemmer.Stem(word);
        var twice = EnglishWordStemmer.Stem(once);
        var thrice = EnglishWordStemmer.Stem(twice);

        Assert.Equal(expected, once);
        Assert.Equal(twice, thrice);
    }

    [Fact]
    public void StemmerId_IsPinned()
    {
        // A stemmer change is a new analyzer contract; the id is that contract's name.
        Assert.Equal("porter-en@1", EnglishWordStemmer.StemmerId);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("a")]
    [InlineData("naive")]
    [InlineData("get-by-id")]
    public void IsWord_WithoutAsciiDigit_IsTrue(string token)
    {
        Assert.True(EnglishWordStemmer.IsWord(token));
    }

    [Theory]
    [InlineData("abc1")]
    [InlineData("1")]
    [InlineData("a0b")]
    [InlineData("md5")]
    public void IsWord_WithAsciiDigit_IsFalse(string token)
    {
        Assert.False(EnglishWordStemmer.IsWord(token));
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("hash9e")]
    [InlineData("v2")]
    public void StemWordOnly_LeavesDigitBearingTokensUntouched(string token)
    {
        // An opaque token that merely ends in a stemmable suffix must not change: "hash9e" keeps its e,
        // "abc123" keeps every character.
        Assert.Equal(token, EnglishWordStemmer.StemWordOnly(token));
    }

    [Fact]
    public void StemWordOnly_StemsAPlainWord()
    {
        Assert.Equal("hop", EnglishWordStemmer.StemWordOnly("hopping"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("is")]
    [InlineData("be")]
    public void Stem_WithTwoCharactersOrFewer_IsUnchanged(string word)
    {
        Assert.Equal(word, EnglishWordStemmer.Stem(word));
    }

    [Fact]
    public void Stem_DoesNotFoldCase()
    {
        // Casing is the caller's job; the rules still apply, so Running stems but keeps its capital.
        Assert.Equal("Run", EnglishWordStemmer.Stem("Running"));
    }

    [Fact]
    public void Stem_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EnglishWordStemmer.Stem(null!));
    }

    [Fact]
    public void StemWordOnly_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EnglishWordStemmer.StemWordOnly(null!));
    }

    [Fact]
    public void IsWord_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EnglishWordStemmer.IsWord(null!));
    }
}
