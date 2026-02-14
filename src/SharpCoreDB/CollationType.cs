// <copyright file="CollationType.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

/// <summary>
/// Collation types for string comparison in SharpCoreDB.
/// Controls how TEXT values are compared, sorted, and indexed.
/// </summary>
/// <remarks>
/// Default is <see cref="Binary"/> (case-sensitive, byte-by-byte).
/// Use <see cref="NoCase"/> for case-insensitive ordinal comparisons.
/// Use <see cref="Locale"/> for culture-specific comparisons (Phase 9).
/// </remarks>
public enum CollationType
{
    /// <summary>Default binary comparison (case-sensitive, byte-by-byte).</summary>
    Binary = 0,

    /// <summary>Case-insensitive comparison using ordinal rules (OrdinalIgnoreCase).</summary>
    NoCase = 1,

    /// <summary>Like Binary but ignores trailing whitespace.</summary>
    RTrim = 2,

    /// <summary>Culture-aware case-insensitive using CurrentCulture.</summary>
    UnicodeCaseInsensitive = 3,

    /// <summary>
    /// Locale-specific collation using a named <see cref="System.Globalization.CultureInfo"/>.
    /// ✅ Phase 9: Requires a locale name (e.g., "tr_TR", "de_DE") stored via <see cref="CultureInfoCollation"/>.
    /// </summary>
    Locale = 4,
}
