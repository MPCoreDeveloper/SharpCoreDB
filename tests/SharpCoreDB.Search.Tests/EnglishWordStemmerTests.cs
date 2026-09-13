// <copyright file="EnglishWordStemmerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Vectors are the classic Porter examples.
// </copyright>

using SharpCoreDB.Search.Lexical;
using Xunit;

namespace SharpCoreDB.Search.Tests;

/// <summary>English word-only stemming.</summary>
public class EnglishWordStemmerTests
{
    [Theory]
    [InlineData("caresses", "caress")]
    [InlineData("ponies", "poni")]
    [InlineData("ties", "ti")]
    [InlineData("caress", "caress")]
    [InlineData("cats", "cat")]
    [InlineData("hopping", "hop")]
    [InlineData("tanned", "tan")]
    [InlineData("falling", "fall")]
    [InlineData("hissing", "hiss")]
    [InlineData("fizzed", "fizz")]
    [InlineData("filing", "file")]
    [InlineData("happy", "happi")]
    [InlineData("relational", "relat")]
    [InlineData("conditional", "condit")]
    [InlineData("goodness", "good")]
    [InlineData("hopeful", "hope")]
    [InlineData("revival", "reviv")]
    public void Porter_vectors_match(string word, string expected)
        => Assert.Equal(expected, EnglishWordStemmer.Stem(word));

    [Fact]
    public void Short_words_are_left_alone()
    {
        Assert.Equal("is", EnglishWordStemmer.Stem("is"));
        Assert.Equal("be", EnglishWordStemmer.Stem("be"));
    }

    [Fact]
    public void A_token_carrying_a_digit_is_not_a_word_and_is_not_stemmed()
    {
        // The munarium rule: an opaque token that merely ends in a stemmable suffix must not change.
        Assert.False(EnglishWordStemmer.IsWord("ecf8427e"));
        Assert.Equal("ecf8427e", EnglishWordStemmer.StemWordOnly("ecf8427e"));
        Assert.Equal("US7654321B2", EnglishWordStemmer.StemWordOnly("US7654321B2"));
        Assert.Equal("1.2e-9", EnglishWordStemmer.StemWordOnly("1.2e-9"));
    }

    [Fact]
    public void Words_are_stemmed_through_the_word_only_entry_point()
    {
        Assert.True(EnglishWordStemmer.IsWord("caresses"));
        Assert.Equal("caress", EnglishWordStemmer.StemWordOnly("caresses"));
    }
}
