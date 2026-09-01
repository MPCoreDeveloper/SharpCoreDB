// <copyright file="SimpleSelectPlan.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using System.Globalization;

/// <summary>
/// Pre-parsed descriptor for the most common SELECT shape:
/// <c>SELECT [*|col] FROM t [WHERE col = @param|'literal'] [ORDER BY col [ASC|DESC]] [LIMIT n [OFFSET m]]</c>.
/// Enables the v2 zero-reparse fast path: repeated executions reuse this descriptor and skip
/// parameter binding, tokenization, and SQL re-parsing entirely.
/// Detection is deliberately conservative — any query shape that does not match exactly
/// yields <see langword="null"/> and execution falls back to the full parser for full
/// backwards compatibility.
/// </summary>
internal sealed class SimpleSelectPlan
{
    /// <summary>Gets the main table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the WHERE column name (equality predicate), or null for a full scan.</summary>
    public required string? WhereColumn { get; init; }

    /// <summary>Gets the WHERE parameter token (e.g. "@name"), or null when a literal is used.</summary>
    public string? WhereParameter { get; init; }

    /// <summary>Gets the raw WHERE literal SQL token (e.g. "'User0'"), or null when a parameter is used.</summary>
    public string? WhereLiteral { get; init; }

    /// <summary>Gets whether the query is SELECT *.</summary>
    public bool IsSelectAll { get; init; }

    /// <summary>Gets the ORDER BY column, or null.</summary>
    public string? OrderByColumn { get; init; }

    /// <summary>Gets whether the ORDER BY direction is ascending.</summary>
    public bool OrderByAscending { get; init; } = true;

    /// <summary>Gets the LIMIT value, or null.</summary>
    public int? Limit { get; init; }

    /// <summary>Gets the OFFSET value, or null.</summary>
    public int? Offset { get; init; }

    /// <summary>
    /// Attempts to detect a simple point-lookup SELECT from whitespace-tokenized SQL parts.
    /// Returns null when the query shape is anything more complex than the supported pattern.
    /// </summary>
    /// <param name="parts">The whitespace-split SQL tokens (as produced by the plan cache).</param>
    /// <returns>A descriptor, or null when the query is not a supported simple shape.</returns>
    public static SimpleSelectPlan? TryCreate(string[] parts)
    {
        if (parts.Length < 4 || !parts[0].Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            return null;

        // Reject complex shapes outright: subqueries, joins, set operations, grouping, DISTINCT.
        if (!TryRejectComplexParts(parts))
            return null;

        if (!TryFindFromIndex(parts, out int fromIdx))
            return null;

        // SELECT clause: only "*" or exactly one bare column name.
        bool isSelectAll = parts[1].Equals("*", StringComparison.OrdinalIgnoreCase);
        if (!isSelectAll && fromIdx - 1 != 1)
            return null;

        if (fromIdx + 1 >= parts.Length)
            return null;

        string tableName = parts[fromIdx + 1];
        if (!IsSimpleIdentifier(tableName))
            return null;

        // Scan the trailing clauses: WHERE col = value, ORDER BY col [ASC|DESC], LIMIT n [OFFSET m].
        int pos = fromIdx + 2;
        var state = new ClauseState();

        while (pos < parts.Length)
        {
            if (!TryParseNextClause(parts, ref pos, state))
            {
                // Unknown trailing token — fall back to the full parser.
                return null;
            }
        }

        // A point lookup requires a WHERE predicate; without one this is a full scan
        // (supported by the StructRow path, which scans all rows).
        if (state.WhereColumn is null)
        {
            return new SimpleSelectPlan
            {
                TableName = tableName,
                WhereColumn = null,
                IsSelectAll = isSelectAll,
                OrderByColumn = state.OrderByColumn,
                OrderByAscending = state.OrderAscending,
                Limit = state.Limit,
                Offset = state.Offset
            };
        }

        return new SimpleSelectPlan
        {
            TableName = tableName,
            WhereColumn = state.WhereColumn,
            WhereParameter = state.WhereIsParameter ? state.WhereValue : null,
            WhereLiteral = state.WhereIsParameter ? null : state.WhereValue,
            IsSelectAll = isSelectAll,
            OrderByColumn = state.OrderByColumn,
            OrderByAscending = state.OrderAscending,
            Limit = state.Limit,
            Offset = state.Offset
        };
    }

    /// <summary>Mutable state collected while scanning the trailing SELECT clauses.</summary>
    private sealed class ClauseState
    {
        public string? WhereColumn;
        public string? WhereValue;
        public bool WhereIsParameter;
        public string? OrderByColumn;
        public bool OrderAscending = true;
        public int? Limit;
        public int? Offset;
    }

    private static bool TryRejectComplexParts(string[] parts)
    {
        foreach (var part in parts)
        {
            if (part.Length == 0)
                return false;

            if (part.IndexOfAny(['(', ')', ',']) >= 0)
                return false;

            if (part.Equals("JOIN", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("RIGHT", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("INNER", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("FULL", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("CROSS", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("UNION", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("EXCEPT", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("INTERSECT", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("DISTINCT", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("HAVING", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindFromIndex(string[] parts, out int fromIdx)
    {
        fromIdx = -1;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                fromIdx = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseNextClause(string[] parts, ref int pos, ClauseState state)
    {
        if (parts[pos].Equals("WHERE", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseWhereClause(parts, ref pos, state);
        }

        if (parts[pos].Equals("ORDER", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseOrderByClause(parts, ref pos, state);
        }

        if (parts[pos].Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseLimitClause(parts, ref pos, state);
        }

        if (parts[pos].Equals("OFFSET", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseOffsetClause(parts, ref pos, state);
        }

        return false;
    }

    private static bool TryParseWhereClause(string[] parts, ref int pos, ClauseState state)
    {
        if (state.WhereColumn is not null)
            return false; // Duplicate WHERE — unsupported.

        if (pos + 3 >= parts.Length)
            return false;

        string col = parts[pos + 1];
        if (!IsSimpleIdentifier(col) || !parts[pos + 2].Equals("=", StringComparison.Ordinal))
            return false;

        string value = parts[pos + 3];
        if (value.Length == 0 || value.IndexOfAny(['(', ')', ',']) >= 0)
            return false;

        // Positional '?' placeholders require the parameter binder (legacy path).
        if (value == "?")
            return false;

        state.WhereColumn = col;
        state.WhereIsParameter = value[0] == '@' || value[0] == ':';
        state.WhereValue = value;
        pos += 4;
        return true;
    }

    private static bool TryParseOrderByClause(string[] parts, ref int pos, ClauseState state)
    {
        if (state.WhereColumn is null || state.OrderByColumn is not null)
            return false; // ORDER BY before WHERE or duplicated.

        if (pos + 2 >= parts.Length || !parts[pos + 1].Equals("BY", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!IsSimpleIdentifier(parts[pos + 2]))
            return false;

        state.OrderByColumn = parts[pos + 2];
        pos += 3;

        if (pos < parts.Length && parts[pos].Equals("DESC", StringComparison.OrdinalIgnoreCase))
        {
            state.OrderAscending = false;
            pos += 1;
        }
        else if (pos < parts.Length && parts[pos].Equals("ASC", StringComparison.OrdinalIgnoreCase))
        {
            pos += 1;
        }

        return true;
    }

    private static bool TryParseLimitClause(string[] parts, ref int pos, ClauseState state)
    {
        if (state.Limit is not null || pos + 1 >= parts.Length ||
            !int.TryParse(parts[pos + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int limitValue) || limitValue < 0)
        {
            return false;
        }

        state.Limit = limitValue;
        pos += 2;
        return true;
    }

    private static bool TryParseOffsetClause(string[] parts, ref int pos, ClauseState state)
    {
        if (state.Offset is not null || pos + 1 >= parts.Length ||
            !int.TryParse(parts[pos + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int offsetValue) || offsetValue < 0)
        {
            return false;
        }

        state.Offset = offsetValue;
        pos += 2;
        return true;
    }

    /// <summary>
    /// Validates that a token is a bare SQL identifier (letters, digits, underscore).
    /// </summary>
    private static bool IsSimpleIdentifier(string token)
    {
        if (token.Length == 0)
            return false;

        if (!char.IsLetter(token[0]) && token[0] != '_')
            return false;

        for (int i = 1; i < token.Length; i++)
        {
            char c = token[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }
}
