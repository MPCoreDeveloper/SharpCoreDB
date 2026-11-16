// <copyright file="SqlOrderBy.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SharpCoreDB.Interfaces;

/// <summary>
/// The ORDER BY list of the single-table SELECT path, read and applied as SQLite does: every key with its own
/// direction, a key may be any scalar expression, a result-column alias or a 1-based result-column ordinal, NULLs sort
/// first when ascending (last when descending) unless <c>NULLS FIRST|LAST</c> says otherwise, and text compares with
/// the key's <c>COLLATE</c>, else the column's declared collation, else BINARY. The sort is stable.
/// <para>
/// Up to 2.1.4 the path read only the token after ORDER BY: a second key was ignored (<c>ORDER BY grp, name</c> came
/// back unsorted — linq2db's shape for <c>OrderBy(…).ThenBy(…)</c>), an expression, alias or qualified key
/// (<c>[x].[name]</c>) matched no row value, and text was compared with the machine's culture.
/// </para>
/// </summary>
internal static class SqlOrderBy
{
    /// <summary>One ORDER BY key.</summary>
    internal sealed class Term(SqlScalarExpression.Node key, bool descending, bool nullsFirst, string? collation, string? columnName)
    {
        public SqlScalarExpression.Node Key { get; } = key;
        public bool Descending { get; } = descending;
        public bool NullsFirst { get; } = nullsFirst;
        public string? Collation { get; } = collation;
        public string? ColumnName { get; } = columnName;
    }

    /// <summary>
    /// Reads an ORDER BY list. Returns <c>null</c> when a key is not something this evaluator can compute — for
    /// example <c>vec_distance_cosine(…)</c>, which keeps its vector-index route — so the caller keeps its own handling.
    /// </summary>
    /// <param name="orderClause">The text after ORDER BY, up to LIMIT.</param>
    /// <param name="selectClause">The SELECT list, for aliases and ordinals.</param>
    internal static IReadOnlyList<Term>? TryParse(string orderClause, string selectClause)
    {
        if (string.IsNullOrWhiteSpace(orderClause))
        {
            return null;
        }

        var selectItems = ReadSelectItems(selectClause);
        var terms = new List<Term>();
        foreach (var item in SqlInPredicate.SplitTopLevelComma(orderClause))
        {
            var tokens = SqlLiteralScanner.SplitTopLevelWhitespace(item.Trim());
            int end = tokens.Count;
            bool? nullsFirst = null;
            bool descending = false;
            string? collation = null;

            if (end >= 2 && Is(tokens[end - 2], "NULLS") && (Is(tokens[end - 1], "FIRST") || Is(tokens[end - 1], "LAST")))
            {
                nullsFirst = Is(tokens[end - 1], "FIRST");
                end -= 2;
            }

            if (end >= 1 && (Is(tokens[end - 1], "ASC") || Is(tokens[end - 1], "DESC")))
            {
                descending = Is(tokens[end - 1], "DESC");
                end--;
            }

            if (end >= 2 && Is(tokens[end - 2], "COLLATE"))
            {
                collation = SqlIdentifierScanner.IdentifierText(tokens[end - 1]).ToUpperInvariant();
                end -= 2;
            }

            if (end == 0)
            {
                return null;
            }

            var key = ResolveKey(string.Join(" ", tokens.Take(end)), selectItems);
            if (key is null)
            {
                return null;
            }

            // SQLite: NULL is the smallest value, so it leads an ascending sort and trails a descending one.
            terms.Add(new Term(key.Value.Node, descending, nullsFirst ?? !descending, collation, key.Value.ColumnName));
        }

        return terms;
    }

    /// <summary>Sorts rows by the terms (stable), before any OFFSET/LIMIT is applied.</summary>
    /// <param name="rows">The rows to sort.</param>
    /// <param name="terms">The ORDER BY terms.</param>
    /// <param name="table">The table, for declared column collations.</param>
    internal static List<Dictionary<string, object>> Sort(
        List<Dictionary<string, object>> rows,
        IReadOnlyList<Term> terms,
        ITable table)
    {
        if (rows.Count < 2)
        {
            return rows;
        }

        var textComparers = terms.Select(term => TextComparerFor(term, table)).ToArray();
        var keyed = new (object?[] Keys, Dictionary<string, object> Row)[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            var keys = new object?[terms.Count];
            for (int t = 0; t < terms.Count; t++)
            {
                keys[t] = SqlScalarExpression.Evaluate(terms[t].Key, rows[r]);
            }

            keyed[r] = (keys, rows[r]);
        }

        var comparer = Comparer<object?[]>.Create((a, b) =>
        {
            for (int t = 0; t < terms.Count; t++)
            {
                int c = CompareKey(a[t], b[t], terms[t], textComparers[t]);
                if (c != 0)
                {
                    return c;
                }
            }

            return 0;
        });

        // Enumerable.OrderBy is a stable sort: rows with equal keys keep their scan order.
        return [.. keyed.OrderBy(k => k.Keys, comparer).Select(k => k.Row)];
    }

    private static int CompareKey(object? x, object? y, Term term, IComparer<string>? textComparer)
    {
        bool xNull = x is null or DBNull;
        bool yNull = y is null or DBNull;
        if (xNull || yNull)
        {
            if (xNull && yNull)
            {
                return 0;
            }

            // NULL placement is fixed by NULLS FIRST/LAST, not flipped by DESC.
            return xNull == term.NullsFirst ? -1 : 1;
        }

        int c;
        if (x is string xs && y is string ys)
        {
            c = textComparer?.Compare(xs, ys) ?? string.CompareOrdinal(xs, ys);
        }
        else if (!SqlScalarExpression.TryCompare(x, y, out c))
        {
            c = 0;
        }

        c = Math.Sign(c);
        return term.Descending ? -c : c;
    }

    /// <summary>The text comparer for a term: its COLLATE, else the column's declared collation, else BINARY (null).</summary>
    private static IComparer<string>? TextComparerFor(Term term, ITable table)
    {
        switch (term.Collation)
        {
            case "NOCASE":
                return StringComparer.OrdinalIgnoreCase;
            case "RTRIM":
                return RTrimComparer;
            case "BINARY":
                return null;
        }

        if (term.ColumnName is null)
        {
            return null;
        }

        int index = table.Columns.FindIndex(c => c.Equals(term.ColumnName, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= table.ColumnCollations.Count)
        {
            return null;
        }

        return table.ColumnCollations[index] switch
        {
            CollationType.NoCase => StringComparer.OrdinalIgnoreCase,
            CollationType.RTrim => RTrimComparer,
            CollationType.UnicodeCaseInsensitive => StringComparer.InvariantCultureIgnoreCase,
            CollationType.Locale when index < table.ColumnLocaleNames.Count &&
                                      !string.IsNullOrWhiteSpace(table.ColumnLocaleNames[index]) =>
                StringComparer.Create(CultureInfo.GetCultureInfo(table.ColumnLocaleNames[index]!), ignoreCase: false),
            _ => null,
        };
    }

    private static readonly IComparer<string> RTrimComparer =
        Comparer<string>.Create(static (a, b) => string.CompareOrdinal(a.TrimEnd(' '), b.TrimEnd(' ')));

    /// <summary>
    /// Resolves an ORDER BY key: a 1-based ordinal names a result column; an unqualified name that is a result-column
    /// alias names that column's expression (SQLite resolves aliases first); anything else is an expression.
    /// </summary>
    private static (SqlScalarExpression.Node Node, string? ColumnName)? ResolveKey(
        string text,
        List<(string Expression, string? Alias)> selectItems)
    {
        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal))
        {
            if (ordinal < 1 || ordinal > selectItems.Count)
            {
                return null;
            }

            var expression = selectItems[(int)ordinal - 1].Expression;
            return expression == "*" || expression.EndsWith(".*", StringComparison.Ordinal) ? null : ParseKey(expression);
        }

        if (SqlScalarExpression.TryParse(text, out var node) &&
            SqlScalarExpression.ColumnName(node) is { } name &&
            !SqlScalarExpression.IsQualified(node))
        {
            foreach (var (expression, alias) in selectItems)
            {
                if (alias is not null && alias.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return ParseKey(expression);
                }
            }
        }

        return ParseKey(text);
    }

    private static (SqlScalarExpression.Node Node, string? ColumnName)? ParseKey(string text) =>
        SqlScalarExpression.TryParse(text, out var node) ? (node, SqlScalarExpression.ColumnName(node)) : null;

    private static List<(string Expression, string? Alias)> ReadSelectItems(string selectClause)
    {
        var clause = selectClause.Trim();
        if (clause.StartsWith("DISTINCT", StringComparison.OrdinalIgnoreCase))
        {
            clause = clause["DISTINCT".Length..].Trim();
        }

        return [.. SqlInPredicate.SplitTopLevelComma(clause).Select(item => SqlParser.SplitSelectItemAlias(item.Trim()))];
    }

    private static bool Is(string token, string keyword) => token.Equals(keyword, StringComparison.OrdinalIgnoreCase);
}
