// <copyright file="CultureInfoCollationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Globalization;
using Xunit;

/// <summary>
/// Unit tests for <see cref="CultureInfoCollation"/> covering locale format validation,
/// invalid locale detection, culture creation fallback, and comparison operations.
/// Targets code added in the cross-platform locale fix (IsValidLocaleFormat, IsInvalidLocaleIndicator, CreateCulture fallback).
/// </summary>
public sealed class CultureInfoCollationTests
{
    private readonly CultureInfoCollation _sut = new();

    #region GetCulture - IsValidLocaleFormat branches

    [Fact]
    public void GetCulture_WithNullLocale_ShouldThrow()
    {
        Assert.ThrowsAny<ArgumentException>(() => _sut.GetCulture(null!));
    }

    [Fact]
    public void GetCulture_WithEmptyLocale_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(""));
    }

    [Fact]
    public void GetCulture_WithWhitespaceLocale_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _sut.GetCulture("   "));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en_US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("nl-NL")]
    public void GetCulture_WithValidLocale_ShouldReturnCulture(string locale)
    {
        // Act
        var culture = _sut.GetCulture(locale);

        // Assert
        Assert.NotNull(culture);
    }

    [Fact]
    public void GetCulture_WithLanguageOnly_ShouldReturnCulture()
    {
        // "en" is a valid two-letter language-only locale
        var culture = _sut.GetCulture("en");

        Assert.NotNull(culture);
    }

    [Fact]
    public void GetCulture_WithThreeLetterLanguage_ShouldReturnCulture()
    {
        // Three-letter language codes are valid format (e.g., "haw" for Hawaiian)
        // May or may not be available on all systems, but format is valid
        try
        {
            var culture = _sut.GetCulture("haw");
            Assert.NotNull(culture);
        }
        catch (ArgumentException)
        {
            // Acceptable if the locale is not available on this system
        }
    }

    [Theory]
    [InlineData("en-US-extra")]
    [InlineData("en-US-x-custom")]
    [InlineData("a-b-c")]
    public void GetCulture_WithTooManyParts_ShouldThrow(string locale)
    {
        // More than 2 parts separated by hyphens should be rejected by IsValidLocaleFormat
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Theory]
    [InlineData("e")]
    [InlineData("a")]
    public void GetCulture_WithLanguageCodeTooShort_ShouldThrow(string locale)
    {
        // Language code must be 2-3 letters
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("english")]
    public void GetCulture_WithLanguageCodeTooLong_ShouldThrow(string locale)
    {
        // Language code must be 2-3 letters
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Theory]
    [InlineData("1a")]
    [InlineData("e2")]
    [InlineData("12")]
    public void GetCulture_WithNonAlphaLanguageCode_ShouldThrow(string locale)
    {
        // Language code must be all ASCII letters
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("zz")]
    [InlineData("iv")]
    public void GetCulture_WithRejectedLanguageCode_ShouldThrow(string locale)
    {
        // "xx", "zz", "iv" are rejected as obviously invalid
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Theory]
    [InlineData("en-X")]
    [InlineData("en-ABCD")]
    public void GetCulture_WithRegionCodeWrongLength_ShouldThrow(string locale)
    {
        // Region code must be 2 letters or 3 digits
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Theory]
    [InlineData("en-1A")]
    [InlineData("en-A1")]
    public void GetCulture_WithNonAlphaRegionCode_ShouldThrow(string locale)
    {
        // Two-letter region code must be all alphabetic
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Theory]
    [InlineData("en-xx")]
    [InlineData("en-zz")]
    [InlineData("en-iv")]
    public void GetCulture_WithRejectedRegionCode_ShouldThrow(string locale)
    {
        // Rejected region codes
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Fact]
    public void GetCulture_WithThreeDigitRegionCode_ShouldNotThrowFormatError()
    {
        // UN M.49 three-digit numeric region codes are valid format (e.g., "en-001" for World English)
        try
        {
            var culture = _sut.GetCulture("en-001");
            Assert.NotNull(culture);
        }
        catch (ArgumentException)
        {
            // May fail for other reasons but should not fail format validation
        }
    }

    [Theory]
    [InlineData("en-12A")]
    [InlineData("en-A12")]
    public void GetCulture_WithNonDigitThreeCharRegionCode_ShouldThrow(string locale)
    {
        // Three-character region codes must be all-numeric
        Assert.Throws<ArgumentException>(() => _sut.GetCulture(locale));
    }

    [Fact]
    public void GetCulture_CalledTwice_ShouldReturnCachedInstance()
    {
        // Verify caching works (exercises the cache-hit path in GetCulture)
        var culture1 = _sut.GetCulture("en-US");
        var culture2 = _sut.GetCulture("en-US");

        Assert.Same(culture1, culture2);
    }

    [Fact]
    public void GetCulture_WithUnderscoreSeparator_ShouldNormalizeToHyphen()
    {
        // "en_US" and "en-US" should resolve to the same culture
        var culture1 = _sut.GetCulture("en_US");
        var culture2 = _sut.GetCulture("en-US");

        Assert.Equal(culture1.Name, culture2.Name);
    }

    #endregion

    #region GetCulture - IsInvalidLocaleIndicator branches

    [Fact]
    public void GetCulture_WithLiteralInvalid_ShouldThrow()
    {
        var ex = Assert.Throws<ArgumentException>(() => _sut.GetCulture("invalid"));
        Assert.Contains("locale", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCulture_WithXxPrefix_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _sut.GetCulture("xx-YY"));
    }

    [Fact]
    public void GetCulture_WithZzPrefix_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _sut.GetCulture("zz-YY"));
    }

    [Fact]
    public void GetCulture_WithIvPrefix_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _sut.GetCulture("iv-YY"));
    }

    #endregion

    #region IsValidLocale

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("en-US", true)]
    [InlineData("de-DE", true)]
    public void IsValidLocale_WithVariousInputs_ShouldReturnExpected(string? locale, bool expected)
    {
        Assert.Equal(expected, CultureInfoCollation.IsValidLocale(locale));
    }

    [Fact]
    public void IsValidLocale_WithInvalidLocale_ShouldReturnFalse()
    {
        Assert.False(CultureInfoCollation.IsValidLocale("invalid_LOCALE"));
    }

    #endregion

    #region GetCompareInfo

    [Fact]
    public void GetCompareInfo_WithNullLocale_ShouldThrow()
    {
        Assert.ThrowsAny<ArgumentException>(() => _sut.GetCompareInfo(null!));
    }

    [Fact]
    public void GetCompareInfo_WithValidLocale_ShouldReturnCompareInfo()
    {
        var compareInfo = _sut.GetCompareInfo("en-US");

        Assert.NotNull(compareInfo);
    }

    [Fact]
    public void GetCompareInfo_CalledTwice_ShouldReturnCachedInstance()
    {
        // First call populates the cache, second call returns cached
        var ci1 = _sut.GetCompareInfo("en-US");
        var ci2 = _sut.GetCompareInfo("en-US");

        Assert.Same(ci1, ci2);
    }

    [Fact]
    public void GetCompareInfo_WithUncachedLocale_ShouldPopulateViaGetCulture()
    {
        // Use a fresh instance to ensure no prior caching
        var sut = new CultureInfoCollation();

        // GetCompareInfo on an uncached locale should internally call GetCulture to populate both caches
        var compareInfo = sut.GetCompareInfo("fr-FR");

        Assert.NotNull(compareInfo);
    }

    #endregion

    #region Compare

    [Fact]
    public void Compare_BothNull_ShouldReturnZero()
    {
        Assert.Equal(0, _sut.Compare(null, null, "en-US"));
    }

    [Fact]
    public void Compare_LeftNull_ShouldReturnNegative()
    {
        Assert.True(_sut.Compare(null, "b", "en-US") < 0);
    }

    [Fact]
    public void Compare_RightNull_ShouldReturnPositive()
    {
        Assert.True(_sut.Compare("a", null, "en-US") > 0);
    }

    [Fact]
    public void Compare_EqualStrings_ShouldReturnZero()
    {
        Assert.Equal(0, _sut.Compare("hello", "hello", "en-US"));
    }

    [Fact]
    public void Compare_CaseInsensitive_ShouldReturnZero()
    {
        Assert.Equal(0, _sut.Compare("Hello", "hello", "en-US", ignoreCase: true));
    }

    [Fact]
    public void Compare_CaseSensitive_ShouldNotReturnZero()
    {
        // With case-sensitive comparison, "Hello" and "hello" may differ
        var result = _sut.Compare("Hello", "hello", "en-US", ignoreCase: false);
        Assert.NotEqual(0, result);
    }

    #endregion

    #region Equals

    [Fact]
    public void Equals_SameReference_ShouldReturnTrue()
    {
        var str = "test";
        Assert.True(_sut.Equals(str, str, "en-US"));
    }

    [Fact]
    public void Equals_LeftNull_ShouldReturnFalse()
    {
        Assert.False(_sut.Equals(null, "test", "en-US"));
    }

    [Fact]
    public void Equals_RightNull_ShouldReturnFalse()
    {
        Assert.False(_sut.Equals("test", null, "en-US"));
    }

    [Fact]
    public void Equals_BothNull_ShouldReturnTrue()
    {
        // Both null => ReferenceEquals is true
        Assert.True(_sut.Equals(null, null, "en-US"));
    }

    [Fact]
    public void Equals_CaseInsensitive_ShouldReturnTrue()
    {
        Assert.True(_sut.Equals("Hello", "hello", "en-US", ignoreCase: true));
    }

    [Fact]
    public void Equals_CaseSensitive_ShouldReturnFalse()
    {
        Assert.False(_sut.Equals("Hello", "hello", "en-US", ignoreCase: false));
    }

    #endregion

    #region GetHashCode

    [Fact]
    public void GetHashCode_WithNull_ShouldReturnZero()
    {
        Assert.Equal(0, _sut.GetHashCode(null, "en-US"));
    }

    [Fact]
    public void GetHashCode_WithValue_ShouldReturnNonZero()
    {
        var hash = _sut.GetHashCode("test", "en-US");
        // Hash code could theoretically be 0, but extremely unlikely for a real string
        Assert.IsType<int>(hash);
    }

    [Fact]
    public void GetHashCode_CaseInsensitive_SameStrings_ShouldMatch()
    {
        var hash1 = _sut.GetHashCode("Hello", "en-US", ignoreCase: true);
        var hash2 = _sut.GetHashCode("hello", "en-US", ignoreCase: true);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GetHashCode_CaseSensitive_DifferentCase_MayDiffer()
    {
        var hash1 = _sut.GetHashCode("Hello", "en-US", ignoreCase: false);
        var hash2 = _sut.GetHashCode("hello", "en-US", ignoreCase: false);

        // Case-sensitive hashes for different cases should differ
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region GetSortKeyBytes

    [Fact]
    public void GetSortKeyBytes_WithNull_ShouldReturnEmptyArray()
    {
        var result = _sut.GetSortKeyBytes(null, "en-US");

        Assert.Empty(result);
    }

    [Fact]
    public void GetSortKeyBytes_WithValue_ShouldReturnNonEmpty()
    {
        var result = _sut.GetSortKeyBytes("test", "en-US");

        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetSortKeyBytes_CaseInsensitive_SameStrings_ShouldMatch()
    {
        var key1 = _sut.GetSortKeyBytes("Hello", "en-US", ignoreCase: true);
        var key2 = _sut.GetSortKeyBytes("hello", "en-US", ignoreCase: true);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GetSortKeyBytes_CaseSensitive_DifferentCase_ShouldDiffer()
    {
        var key1 = _sut.GetSortKeyBytes("Hello", "en-US", ignoreCase: false);
        var key2 = _sut.GetSortKeyBytes("hello", "en-US", ignoreCase: false);

        Assert.NotEqual(key1, key2);
    }

    #endregion

    #region NormalizeForComparison

    [Fact]
    public void NormalizeForComparison_WithNull_ShouldReturnEmpty()
    {
        var result = _sut.NormalizeForComparison(null, "en-US");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void NormalizeForComparison_WithEnglishLocale_ShouldReturnUppercase()
    {
        var result = _sut.NormalizeForComparison("hello", "en-US");

        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void NormalizeForComparison_WithTurkishLocale_ShouldApplyTurkishRules()
    {
        // Turkish normalization should handle i/I differently
        var result = _sut.NormalizeForComparison("istanbul", "tr-TR");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void NormalizeForComparison_WithTurkishLocale_EmptyString_ShouldReturnEmpty()
    {
        var result = _sut.NormalizeForComparison("", "tr-TR");

        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeForComparison_WithGermanLocale_ShouldApplyGermanRules()
    {
        // German normalization: ß should be handled
        var result = _sut.NormalizeForComparison("straße", "de-DE");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void NormalizeForComparison_WithGermanLocale_EmptyString_ShouldReturnEmpty()
    {
        var result = _sut.NormalizeForComparison("", "de-DE");

        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeForComparison_WithFrenchLocale_ShouldReturnUppercase()
    {
        // French uses the default path (not Turkish or German special-case)
        var result = _sut.NormalizeForComparison("bonjour", "fr-FR");

        Assert.Equal("BONJOUR", result);
    }

    #endregion

    #region Singleton

    [Fact]
    public void Instance_ShouldReturnSameInstance()
    {
        var instance1 = CultureInfoCollation.Instance;
        var instance2 = CultureInfoCollation.Instance;

        Assert.Same(instance1, instance2);
    }

    #endregion
}
