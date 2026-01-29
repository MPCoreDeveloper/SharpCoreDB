// <copyright file="RangeQueryOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;

/// <summary>
/// ✅ Phase 4: Range Query Optimizer
/// Detects range queries and uses B-tree indexes for O(log n + k) performance
/// instead of linear O(N) scans.
/// 
/// Performance:
/// - Linear scan: O(N) - full table scan
/// - B-tree range: O(log N + k) - seek to start, scan range
/// - Expected improvement: 10-100x faster for selective ranges
/// </summary>
public sealed class RangeQueryOptimizer
{
    private readonly IndexManager _indexManager;

    public RangeQueryOptimizer(IndexManager indexManager)
    {
        _indexManager = indexManager ?? throw new ArgumentNullException(nameof(indexManager));
    }

    /// <summary>
    /// Detects if a WHERE clause is a range query (BETWEEN, >, <, >=, <=).
    /// </summary>
    /// <param name="whereExpression">The WHERE clause expression.</param>
    /// <returns>True if this is a range query; false otherwise.</returns>
    public bool IsRangeQuery(string? whereExpression)
    {
        if (string.IsNullOrWhiteSpace(whereExpression))
            return false;

        // Detect range query patterns (case-insensitive)
        var upper = whereExpression.ToUpperInvariant();
        return upper.Contains("BETWEEN") ||
               upper.Contains(" > ") ||
               upper.Contains(" < ") ||
               upper.Contains(">=") ||
               upper.Contains("<=");
    }

    /// <summary>
    /// Extracts range bounds from a BETWEEN clause.
    /// Example: "age BETWEEN 18 AND 65" → ("age", "18", "65")
    /// </summary>
    public bool TryExtractBetweenBounds(
        string whereExpression,
        out string columnName,
        out string startBound,
        out string endBound)
    {
        columnName = "";
        startBound = "";
        endBound = "";

        if (string.IsNullOrWhiteSpace(whereExpression))
            return false;

        // Pattern: "column BETWEEN 'start' AND 'end'"
        var trimmed = whereExpression.Trim();
        var betweenIdx = trimmed.IndexOf("BETWEEN", StringComparison.OrdinalIgnoreCase);
        if (betweenIdx == -1)
            return false;

        // Extract column name (everything before BETWEEN)
        columnName = trimmed[..betweenIdx].Trim();
        if (string.IsNullOrWhiteSpace(columnName))
            return false;

        // Extract bounds (everything after BETWEEN)
        var boundsStr = trimmed[( betweenIdx + 7)..]; // Skip "BETWEEN"
        var andIdx = boundsStr.IndexOf("AND", StringComparison.OrdinalIgnoreCase);
        if (andIdx == -1)
            return false;

        startBound = boundsStr[..andIdx].Trim().Trim('\'');
        endBound = boundsStr[(andIdx + 3)..].Trim().Trim('\'');

        return !string.IsNullOrWhiteSpace(startBound) && !string.IsNullOrWhiteSpace(endBound);
    }

    /// <summary>
    /// Extracts bounds from a comparison query.
    /// Example: "age > 18" → ("age", ">", "18")
    /// </summary>
    public bool TryExtractComparisonBounds(
        string whereExpression,
        out string columnName,
        out string @operator,
        out string bound)
    {
        columnName = "";
        @operator = "";
        bound = "";

        if (string.IsNullOrWhiteSpace(whereExpression))
            return false;

        var trimmed = whereExpression.Trim();

        // Try operators in order of length (>= before >)
        var operators = new[] { ">=", "<=", ">", "<", "=" };
        foreach (var op in operators)
        {
            var idx = trimmed.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0)
            {
                columnName = trimmed[..idx].Trim();
                @operator = op;
                bound = trimmed[(idx + op.Length)..].Trim().Trim('\'');

                return !string.IsNullOrWhiteSpace(columnName) && !string.IsNullOrWhiteSpace(bound);
            }
        }

        return false;
    }

    /// <summary>
    /// Determines if a B-tree index should be used for this range query.
    /// </summary>
    public bool ShouldUseBTreeIndex(string tableName, string columnName)
    {
        // In a real implementation, you would:
        // 1. Check if column is indexed
        // 2. Check if index type is B-tree
        // 3. Check selectivity/statistics to ensure it's worth using the index
        
        // For now, we'll assume that if an index exists for the column, use it
        // The IndexManager will handle creating/retrieving the appropriate index type
        return true;
    }

    /// <summary>
    /// Optimizes a range query by using B-tree index instead of linear scan.
    /// Returns result row positions for the matching range.
    /// </summary>
    /// <typeparam name="TKey">The type of the indexed column.</typeparam>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The indexed column name.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>Enumerable of row positions matching the range query.</returns>
    public IEnumerable<long> OptimizeRangeQuery<TKey>(
        string tableName,
        string columnName,
        TKey start,
        TKey end)
        where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    {
        // Get or create B-tree index for this column
        var index = _indexManager.GetOrCreateIndex<TKey>(
            tableName,
            columnName,
            IndexType.BTree);

        // Use B-tree's O(log n + k) range scan
        return index.FindRange(start, end);
    }
}

/// <summary>
/// Query hint to guide optimizer on index usage.
/// </summary>
public class RangeQueryHint
{
    public string? ColumnName { get; set; }
    public string? TableName { get; set; }
    public bool UseIndex { get; set; } = true;
    public IndexType PreferredIndexType { get; set; } = IndexType.BTree;
}
