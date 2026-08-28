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
        if (parts.Length < 4)
            return null;

        if (!parts[0].Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            return null;

        // Reject complex shapes outright: subqueries, joins, set operations, grouping, DISTINCT.
        // Any token containing '(', ')', or ',' is rejected (catches COUNT(*), col lists, etc.).
        foreach (var part in parts)
        {
            if (part.Length == 0)
                return null;

            if (part.IndexOfAny(['(', ')', ',']) >= 0)
                return null;

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
                return null;
            }
        }

        // Locate the FROM clause (must come after SELECT).
        int fromIdx = -1;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                fromIdx = i;
                break;
            }
        }

        if (fromIdx < 0)
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
        string? whereColumn = null;
        string? whereValue = null;
        bool whereIsParameter = false;
        string? orderByColumn = null;
        bool orderAscending = true;
        int? limit = null;
        int? offset = null;

        while (pos < parts.Length)
        {
            if (parts[pos].Equals("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                if (whereColumn is not null)
                    return null; // Duplicate WHERE — unsupported.

                if (pos + 3 >= parts.Length)
                    return null;

                string col = parts[pos + 1];
                if (!IsSimpleIdentifier(col) || !parts[pos + 2].Equals("=", StringComparison.Ordinal))
                    return null;

                string value = parts[pos + 3];
                if (value.Length == 0 || value.IndexOfAny(['(', ')', ',']) >= 0)
                    return null;

                // Positional '?' placeholders require the parameter binder (legacy path).
                if (value == "?")
                    return null;

                whereColumn = col;
                if (value[0] == '@' || value[0] == ':')
                {
                    whereIsParameter = true;
                    whereValue = value;
                }
                else
                {
                    whereValue = value;
                }

                pos += 4;
            }
            else if (parts[pos].Equals("ORDER", StringComparison.OrdinalIgnoreCase))
            {
                if (whereColumn is null || orderByColumn is not null)
                    return null; // ORDER BY before WHERE or duplicated.

                if (pos + 2 >= parts.Length || !parts[pos + 1].Equals("BY", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (!IsSimpleIdentifier(parts[pos + 2]))
                    return null;

                orderByColumn = parts[pos + 2];
                pos += 3;

                if (pos < parts.Length && parts[pos].Equals("DESC", StringComparison.OrdinalIgnoreCase))
                {
                    orderAscending = false;
                    pos += 1;
                }
                else if (pos < parts.Length && parts[pos].Equals("ASC", StringComparison.OrdinalIgnoreCase))
                {
                    pos += 1;
                }
            }
            else if (parts[pos].Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                if (limit is not null || pos + 1 >= parts.Length ||
                    !int.TryParse(parts[pos + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int limitValue) || limitValue < 0)
                {
                    return null;
                }

                limit = limitValue;
                pos += 2;
            }
            else if (parts[pos].Equals("OFFSET", StringComparison.OrdinalIgnoreCase))
            {
                if (offset is not null || pos + 1 >= parts.Length ||
                    !int.TryParse(parts[pos + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int offsetValue) || offsetValue < 0)
                {
                    return null;
                }

                offset = offsetValue;
                pos += 2;
            }
            else
            {
                // Unknown trailing token — fall back to the full parser.
                return null;
            }
        }

        // A point lookup requires a WHERE predicate; without one this is a full scan
        // (supported by the StructRow path, which scans all rows).
        if (whereColumn is null)
        {
            return new SimpleSelectPlan
            {
                TableName = tableName,
                WhereColumn = null,
                IsSelectAll = isSelectAll,
                OrderByColumn = orderByColumn,
                OrderByAscending = orderAscending,
                Limit = limit,
                Offset = offset
            };
        }

        return new SimpleSelectPlan
        {
            TableName = tableName,
            WhereColumn = whereColumn,
            WhereParameter = whereIsParameter ? whereValue : null,
            WhereLiteral = whereIsParameter ? null : whereValue,
            IsSelectAll = isSelectAll,
            OrderByColumn = orderByColumn,
            OrderByAscending = orderAscending,
            Limit = limit,
            Offset = offset
        };
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

