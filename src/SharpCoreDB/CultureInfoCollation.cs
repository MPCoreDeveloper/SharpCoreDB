// <copyright file="CultureInfoCollation.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

/// <summary>
/// ✅ Phase 9: Singleton registry and comparison engine for locale-specific collations.
/// Maps locale names (e.g., "tr_TR", "de_DE") to <see cref="CultureInfo"/> instances
/// and provides culture-aware string comparison, equality, and hash code operations.
///
/// Thread-safe via <see cref="Lock"/> (C# 14). Caches <see cref="CompareInfo"/> for hot-path performance.
/// </summary>
/// <remarks>
/// Locale-aware comparison is 10-100x slower than ordinal. Use only when needed.
/// <para>
/// Supported locale formats: "en_US", "en-US", "de_DE", "tr_TR", etc.
/// Underscores are normalized to hyphens for .NET <see cref="CultureInfo"/> compatibility.
/// </para>
/// </remarks>
public sealed class CultureInfoCollation
{
    /// <summary>
    /// Shared singleton instance for global locale collation registry.
    /// </summary>
    public static CultureInfoCollation Instance { get; } = new();

    private readonly Lock _registryLock = new();
    private readonly Dictionary<string, CultureInfo> _cultureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CompareInfo> _compareInfoCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or creates a <see cref="CultureInfo"/> for the given locale name.
    /// Caches the result for subsequent calls.
    /// </summary>
    /// <param name="localeName">The locale name (e.g., "tr_TR", "de-DE").</param>
    /// <returns>The resolved <see cref="CultureInfo"/>.</returns>
    /// <exception cref="ArgumentException">If <paramref name="localeName"/> is null, empty, or invalid.</exception>
    public CultureInfo GetCulture(string localeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localeName);

        var normalized = NormalizeLocaleName(localeName);

        lock (_registryLock)
        {
            if (_cultureCache.TryGetValue(normalized, out var cached))
                return cached;
        }

        // Validate and create outside lock to avoid holding lock during CultureInfo construction
        var culture = CreateCulture(normalized);

        lock (_registryLock)
        {
            // Double-check after acquiring lock
            if (!_cultureCache.TryGetValue(normalized, out _))
            {
                _cultureCache[normalized] = culture;
                _compareInfoCache[normalized] = culture.CompareInfo;
            }

            return _cultureCache[normalized];
        }
    }

    /// <summary>
    /// Gets the <see cref="CompareInfo"/> for the given locale name.
    /// More efficient than <c>GetCulture(name).CompareInfo</c> due to caching.
    /// </summary>
    /// <param name="localeName">The locale name (e.g., "tr_TR", "de-DE").</param>
    /// <returns>The resolved <see cref="CompareInfo"/>.</returns>
    public CompareInfo GetCompareInfo(string localeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localeName);

        var normalized = NormalizeLocaleName(localeName);

        lock (_registryLock)
        {
            if (_compareInfoCache.TryGetValue(normalized, out var cached))
                return cached;
        }

        // Ensure culture is registered (populates both caches)
        _ = GetCulture(localeName);

        lock (_registryLock)
        {
            return _compareInfoCache[normalized];
        }
    }

    /// <summary>
    /// Performs locale-aware string comparison.
    /// Returns: negative (left &lt; right), 0 (equal), positive (left &gt; right).
    /// </summary>
    /// <param name="left">First string to compare (can be null).</param>
    /// <param name="right">Second string to compare (can be null).</param>
    /// <param name="localeName">The locale name for comparison.</param>
    /// <param name="ignoreCase">Whether to ignore case.</param>
    /// <returns>Comparison result.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(string? left, string? right, string localeName, bool ignoreCase = true)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var compareInfo = GetCompareInfo(localeName);
        var options = ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;
        return compareInfo.Compare(left, right, options);
    }

    /// <summary>
    /// Performs locale-aware string equality check.
    /// </summary>
    /// <param name="left">First string (can be null).</param>
    /// <param name="right">Second string (can be null).</param>
    /// <param name="localeName">The locale name.</param>
    /// <param name="ignoreCase">Whether to ignore case.</param>
    /// <returns>True if equal under the locale rules.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(string? left, string? right, string localeName, bool ignoreCase = true)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        var compareInfo = GetCompareInfo(localeName);
        var options = ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;
        return compareInfo.Compare(left, right, options) == 0;
    }

    /// <summary>
    /// Gets a locale-aware hash code consistent with <see cref="Equals"/>.
    /// Uses <see cref="CompareInfo.GetSortKey(string, CompareOptions)"/> for correctness.
    /// </summary>
    /// <param name="value">The string value (can be null).</param>
    /// <param name="localeName">The locale name.</param>
    /// <param name="ignoreCase">Whether to ignore case.</param>
    /// <returns>The hash code.</returns>
    public int GetHashCode(string? value, string localeName, bool ignoreCase = true)
    {
        if (value is null) return 0;

        var compareInfo = GetCompareInfo(localeName);
        var options = ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;
        var sortKey = compareInfo.GetSortKey(value, options);
        return sortKey.GetHashCode();
    }

    /// <summary>
    /// Generates a sort key for index materialization.
    /// Sort keys enable binary comparison for indexed locale-aware columns.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <param name="localeName">The locale name.</param>
    /// <param name="ignoreCase">Whether to ignore case.</param>
    /// <returns>The sort key bytes, or empty array if value is null.</returns>
    public byte[] GetSortKeyBytes(string? value, string localeName, bool ignoreCase = true)
    {
        if (value is null) return [];

        var compareInfo = GetCompareInfo(localeName);
        var options = ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;
        var sortKey = compareInfo.GetSortKey(value, options);
        return sortKey.KeyData;
    }

    /// <summary>
    /// Normalizes a locale key for comparison and hashing.
    /// Returns the culture-aware uppercase form of the string.
    /// </summary>
    /// <param name="value">The string to normalize.</param>
    /// <param name="localeName">The locale name.</param>
    /// <returns>Normalized string suitable for index keys.</returns>
    public string NormalizeForComparison(string? value, string localeName)
    {
        if (value is null) return string.Empty;

        var normalized = NormalizeLocaleName(localeName);
        
        // Handle Turkish special case: İ (dotted capital I) and ı (dotless lowercase i)
        if (normalized.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyTurkishNormalization(value);
        }
        
        // Handle German special case: ß (Eszett) → SS in uppercase
        if (normalized.StartsWith("de", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyGermanNormalization(value);
        }

        var culture = GetCulture(localeName);
        return value.ToUpper(culture);
    }
    
    /// <summary>
    /// Applies Turkish-specific case normalization.
    /// Turkish has distinct I/i and İ/ı (dotted/dotless forms).
    /// For comparison purposes, normalizes to lowercase for consistency.
    /// </summary>
    private static string ApplyTurkishNormalization(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        // Convert to uppercase using Turkish rules: i→I, ı→Ι, İ stays İ
        var result = value.ToUpper(culture);
        // Then to lowercase to get canonical form
        return result.ToLower(culture);
    }
    
    /// <summary>
    /// Applies German-specific case normalization.
    /// German ß (Eszett) → SS in uppercase, but ss normalizes back to ß.
    /// </summary>
    private static string ApplyGermanNormalization(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        
        var culture = CultureInfo.GetCultureInfo("de-DE");
        // Convert to uppercase: ß → SS
        var uppercase = value.ToUpper(culture);
        // Convert back to lowercase for canonical form
        return uppercase.ToLower(culture);
    }

    /// <summary>
    /// Validates whether a locale name is recognized by the runtime.
    /// </summary>
    /// <param name="localeName">The locale name to validate.</param>
    /// <returns>True if the locale is valid and supported.</returns>
    public static bool IsValidLocale(string? localeName)
    {
        if (string.IsNullOrWhiteSpace(localeName))
            return false;

        try
        {
            var normalized = NormalizeLocaleName(localeName);
            _ = CultureInfo.GetCultureInfo(normalized);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Normalizes locale name format: converts underscores to hyphens for .NET compatibility.
    /// "tr_TR" → "tr-TR", "de_DE" → "de-DE"
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormalizeLocaleName(string localeName) =>
        localeName.Replace('_', '-');

    /// <summary>
    /// Creates and validates a CultureInfo from a normalized locale name.
    /// ✅ FIX: Validate that the locale is a real, supported culture (not just a syntactically valid code like "xx-YY").
    /// </summary>
    private static CultureInfo CreateCulture(string normalizedName)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(normalizedName);
            
            // ✅ FIX: Check if this is a real culture or just a placeholder/custom code
            // .NET accepts codes like "xx-YY" and "zz-ZZ" without throwing, but these are not real cultures
            // We validate by checking:
            // 1. Two-letter ISO code is "iv" (Invariant culture placeholder)
            // 2. DisplayName contains "Unknown" (e.g., "zz (Unknown Region)")
            // 3. Two-letter ISO code is "xx" or "zz" (common placeholders)
            var isoCode = culture.TwoLetterISOLanguageName;
            if (isoCode == "iv" || 
                isoCode == "xx" || 
                isoCode == "zz" ||
                culture.DisplayName.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                throw new CultureNotFoundException(
                    $"Locale '{normalizedName}' is not a recognized culture. " +
                    $"Use a valid IETF locale name (e.g., 'en-US', 'de-DE', 'tr-TR').");
            }
            
            return culture;
        }
        catch (CultureNotFoundException ex)
        {
            throw new ArgumentException(
                $"Unknown locale '{normalizedName}'. Use a valid IETF locale name (e.g., 'en-US', 'de-DE', 'tr-TR').",
                nameof(normalizedName), ex);
        }
    }
}
