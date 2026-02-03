// <copyright file="IndexHints.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Indexing;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Index hints for query optimization.
/// C# 14: Record types, modern patterns, enum support.
/// 
/// ✅ SCDB Phase 9: Index Enhancements
/// 
/// Purpose:
/// - Allow explicit index selection in queries
/// - Force/prefer/avoid specific indexes
/// - Override query optimizer decisions
/// - Support SQL-style hint syntax
/// </summary>
public sealed record IndexHint
{
    /// <summary>Table name the hint applies to.</summary>
    public required string TableName { get; init; }

    /// <summary>Index name to use/avoid.</summary>
    public required string IndexName { get; init; }

    /// <summary>Hint type (Force/Prefer/Avoid).</summary>
    public required IndexHintType HintType { get; init; }

    /// <summary>
    /// Parses a hint string in SQL format.
    /// Example: "/*+ INDEX(users idx_email) */"
    /// </summary>
    public static IndexHint? Parse(string hintString)
    {
        if (string.IsNullOrWhiteSpace(hintString))
            return null;

        // Remove comment markers
        var cleaned = hintString.Trim()
            .Replace("/*+", "")
            .Replace("*/", "")
            .Trim();

        // Parse INDEX(table index_name) or NO_INDEX(table index_name)
        if (cleaned.StartsWith("INDEX(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIndexHint(cleaned[6..^1], IndexHintType.Force);
        }

        if (cleaned.StartsWith("NO_INDEX(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIndexHint(cleaned[9..^1], IndexHintType.Avoid);
        }

        if (cleaned.StartsWith("USE_INDEX(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseIndexHint(cleaned[10..^1], IndexHintType.Prefer);
        }

        return null;
    }

    private static IndexHint? ParseIndexHint(string args, IndexHintType hintType)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        return new IndexHint
        {
            TableName = parts[0],
            IndexName = parts[1],
            HintType = hintType
        };
    }

    /// <summary>
    /// Validates the hint against available indexes.
    /// </summary>
    public bool Validate(IEnumerable<string> availableIndexes)
    {
        return availableIndexes.Contains(IndexName);
    }
}

/// <summary>
/// Type of index hint.
/// </summary>
public enum IndexHintType
{
    /// <summary>Force use of this index.</summary>
    Force,

    /// <summary>Prefer this index if possible.</summary>
    Prefer,

    /// <summary>Avoid using this index.</summary>
    Avoid
}

/// <summary>
/// Collection of index hints for a query.
/// </summary>
public sealed class IndexHintCollection
{
    private readonly List<IndexHint> _hints = [];

    /// <summary>Adds a hint.</summary>
    public void Add(IndexHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        _hints.Add(hint);
    }

    /// <summary>Gets hints for a specific table.</summary>
    public IEnumerable<IndexHint> GetHintsForTable(string tableName)
    {
        return _hints.Where(h => h.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gets all forced indexes for a table.</summary>
    public IEnumerable<string> GetForcedIndexes(string tableName)
    {
        return GetHintsForTable(tableName)
            .Where(h => h.HintType == IndexHintType.Force)
            .Select(h => h.IndexName);
    }

    /// <summary>Gets all avoided indexes for a table.</summary>
    public IEnumerable<string> GetAvoidedIndexes(string tableName)
    {
        return GetHintsForTable(tableName)
            .Where(h => h.HintType == IndexHintType.Avoid)
            .Select(h => h.IndexName);
    }

    /// <summary>Checks if an index should be used.</summary>
    public bool ShouldUseIndex(string tableName, string indexName)
    {
        var hints = GetHintsForTable(tableName).ToList();

        // Check for explicit avoid
        if (hints.Any(h => h.HintType == IndexHintType.Avoid && h.IndexName == indexName))
            return false;

        // Check for forced indexes (if any forced, only those can be used)
        var forcedIndexes = hints.Where(h => h.HintType == IndexHintType.Force).ToList();
        if (forcedIndexes.Count > 0)
            return forcedIndexes.Any(h => h.IndexName == indexName);

        return true;
    }

    /// <summary>Gets the preferred index order for a table.</summary>
    public IEnumerable<string> GetPreferredOrder(string tableName, IEnumerable<string> availableIndexes)
    {
        var hints = GetHintsForTable(tableName).ToList();
        var forced = hints.Where(h => h.HintType == IndexHintType.Force).Select(h => h.IndexName).ToList();
        var preferred = hints.Where(h => h.HintType == IndexHintType.Prefer).Select(h => h.IndexName).ToList();
        var avoided = hints.Where(h => h.HintType == IndexHintType.Avoid).Select(h => h.IndexName).ToHashSet();

        // Return in order: forced, preferred, others (excluding avoided)
        foreach (var idx in forced)
            yield return idx;

        foreach (var idx in preferred.Where(i => !forced.Contains(i)))
            yield return idx;

        foreach (var idx in availableIndexes.Where(i => !forced.Contains(i) && !preferred.Contains(i) && !avoided.Contains(i)))
            yield return idx;
    }

    /// <summary>Clears all hints.</summary>
    public void Clear() => _hints.Clear();

    /// <summary>Gets all hints.</summary>
    public IReadOnlyList<IndexHint> All => _hints.AsReadOnly();
}
