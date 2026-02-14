// <copyright file="CollationExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

/// <summary>
/// Extension methods and helpers for <see cref="CollationType"/>.
/// ✅ COLLATE Phase 4: Index key normalization for collation-aware hash/BTree indexes.
/// </summary>
public static class CollationExtensions
{
    /// <summary>
    /// Normalizes an index key string based on the specified collation type.
    /// ✅ COLLATE Phase 4: Used by HashIndex and BTree to create canonical key representations.
    /// PERF: Hot path - minimize allocations. NoCase uses ToUpperInvariant() for stable hash codes.
    /// </summary>
    /// <param name="value">The original key value.</param>
    /// <param name="collation">The collation type.</param>
    /// <returns>The normalized key suitable for indexing.</returns>
    /// <remarks>
    /// Key normalization rules:
    /// - <see cref="CollationType.Binary"/>: No normalization (returns original value).
    /// - <see cref="CollationType.NoCase"/>: Converts to uppercase invariant (stable across cultures).
    /// - <see cref="CollationType.RTrim"/>: Trims trailing whitespace.
    /// - <see cref="CollationType.UnicodeCaseInsensitive"/>: Converts to uppercase current culture.
    /// </remarks>
    public static string NormalizeIndexKey(string value, CollationType collation)
    {
        ArgumentNullException.ThrowIfNull(value);

        return collation switch
        {
            CollationType.Binary => value, // No normalization
            CollationType.NoCase => value.ToUpperInvariant(), // Canonical uppercase form
            CollationType.RTrim => value.TrimEnd(), // Remove trailing spaces
            CollationType.UnicodeCaseInsensitive => value.ToUpper(), // Culture-aware uppercase
            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale => value.ToUpper(), // Culture-aware uppercase
            _ => value // Default to no normalization
        };
    }

    /// <summary>
    /// Normalizes an index key string using a specific locale.
    /// ✅ Phase 9: Used for locale-specific index key normalization.
    /// </summary>
    /// <param name="value">The original key value.</param>
    /// <param name="localeName">The locale name (e.g., "tr_TR").</param>
    /// <returns>The normalized key suitable for indexing.</returns>
    public static string NormalizeIndexKey(string value, string localeName)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(localeName);

        return CultureInfoCollation.Instance.NormalizeForComparison(value, localeName);
    }

    /// <summary>
    /// Determines if two strings are equal according to the specified collation.
    /// ✅ COLLATE Phase 4: Used by hash index equality comparers.
    /// </summary>
    /// <param name="left">The left string.</param>
    /// <param name="right">The right string.</param>
    /// <param name="collation">The collation type.</param>
    /// <returns>True if equal according to collation rules, false otherwise.</returns>
    public static bool AreEqual(string? left, string? right, CollationType collation)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        return collation switch
        {
            CollationType.Binary => left.Equals(right, StringComparison.Ordinal),
            CollationType.NoCase => left.Equals(right, StringComparison.OrdinalIgnoreCase),
            CollationType.RTrim => left.TrimEnd().Equals(right.TrimEnd(), StringComparison.Ordinal),
            CollationType.UnicodeCaseInsensitive => left.Equals(right, StringComparison.CurrentCultureIgnoreCase),
            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale => left.Equals(right, StringComparison.CurrentCultureIgnoreCase),
            _ => left.Equals(right, StringComparison.Ordinal)
        };
    }

    /// <summary>
    /// Gets a hash code for a string based on the specified collation.
    /// ✅ COLLATE Phase 4: Ensures consistent hash codes for collation-aware hash indexes.
    /// IMPORTANT: Must be consistent with <see cref="AreEqual"/> - equal strings must have equal hash codes.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <param name="collation">The collation type.</param>
    /// <returns>The hash code.</returns>
    public static int GetHashCode(string? value, CollationType collation)
    {
        if (value is null) return 0;

        return collation switch
        {
            CollationType.Binary => value.GetHashCode(StringComparison.Ordinal),
            CollationType.NoCase => value.GetHashCode(StringComparison.OrdinalIgnoreCase),
            CollationType.RTrim => value.TrimEnd().GetHashCode(StringComparison.Ordinal),
            CollationType.UnicodeCaseInsensitive => value.GetHashCode(StringComparison.CurrentCultureIgnoreCase),
            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale => value.GetHashCode(StringComparison.CurrentCultureIgnoreCase),
            _ => value.GetHashCode(StringComparison.Ordinal)
        };
    }
}
