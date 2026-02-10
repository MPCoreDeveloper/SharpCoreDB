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
/// Phase 6 will add locale-aware collations via <see cref="UnicodeCaseInsensitive"/>.
/// </remarks>
public enum CollationType
{
    /// <summary>Default binary comparison (case-sensitive, byte-by-byte).</summary>
    Binary = 0,

    /// <summary>Case-insensitive comparison using ordinal rules (OrdinalIgnoreCase).</summary>
    NoCase = 1,

    /// <summary>Like Binary but ignores trailing whitespace.</summary>
    RTrim = 2,

    /// <summary>Culture-aware case-insensitive (future: locale-specific, Phase 6).</summary>
    UnicodeCaseInsensitive = 3,
}
