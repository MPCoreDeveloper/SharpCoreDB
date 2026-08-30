// <copyright file="SqlInPredicate.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Shared IN / NOT IN predicate parsing and evaluation, used by every WHERE evaluation path
/// (single-file <see cref="SingleFileTable"/>, directory-mode <see cref="DataStructures.Table"/>
/// and the enhanced AST evaluator). Centralizing the logic here prevents the value-list
/// parsing from drifting between paths (GitHub issues #339 and #340).
/// </summary>
internal static class SqlInPredicate
{
    private static readonly Regex InConditionPattern = new(
        @"^(.+?)\s+(NOT\s+)?IN\s*\((.*)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    /// <summary>SQLite <c>IN (VALUES (...))</c> keyword stripped before list parsing.</summary>
    private const string ValuesKeyword = "VALUES";

    /// <summary>
    /// A parsed IN / NOT IN predicate. Supports a scalar column
    /// (<c>node_type IN ('a', 'b')</c>) and a tuple column
    /// (<c>(node_type, external_id) IN (VALUES ('a','x'), ('b','y'))</c>).
    /// </summary>
    /// <param name="Columns">The normalized column names (1 for scalar, N for a tuple).</param>
    /// <param name="Rows">The candidate value rows; each row has exactly <see cref="Columns"/> entries.</param>
    /// <param name="Negated">True when the clause is <c>NOT IN</c>.</param>
    public readonly record struct ParsedInPredicate(string[] Columns, List<string[]> Rows, bool Negated)
    {
        public bool IsScalar => Columns.Length == 1;
    }

    /// <summary>
    /// Parses a single-condition IN / NOT IN clause, including SQLite-style <c>IN (VALUES (...))</c>
    /// and tuple forms. Returns the normalized column names (alias-qualified and quoted references
    /// are stripped), whether the clause is negated, and the trimmed value rows.
    /// </summary>
    /// <param name="condition">The condition text, e.g. <c>node_type IN ('WorkItem', 'Person')</c>.</param>
    /// <param name="parsed">The parsed predicate when the condition is a well-formed IN/NOT IN clause.</param>
    /// <returns>True when the condition is a well-formed IN/NOT IN clause.</returns>
    public static bool TryParsePredicate(string condition, out ParsedInPredicate parsed)
    {
        parsed = default;

        var match = InConditionPattern.Match(condition);
        if (!match.Success)
        {
            return false;
        }

        var columnPart = match.Groups[1].Value.Trim();
        var listPart = match.Groups[3].Value;
        bool negated = match.Groups[2].Success;

        var columns = ParseColumns(columnPart);
        if (columns.Length == 0)
        {
            return false;
        }

        var rows = ParseValueRows(listPart);
        if (rows.Count == 0)
        {
            return false;
        }

        parsed = new ParsedInPredicate(columns, rows, negated);
        return true;
    }

    /// <summary>
    /// Legacy scalar-only wrapper kept for callers that only need a single-column IN list.
    /// </summary>
    public static bool TryParseCondition(string condition, out string column, out bool negated, out List<string> items)
    {
        column = string.Empty;
        negated = false;
        items = [];

        if (!TryParsePredicate(condition, out var parsed) || parsed.Columns.Length != 1)
        {
            return false;
        }

        column = parsed.Columns[0];
        negated = parsed.Negated;
        items = parsed.Rows.Select(r => r[0]).ToList();
        return true;
    }

    /// <summary>
    /// Evaluates a row against a parsed IN/NOT IN predicate. For a scalar predicate every column
    /// value must equal the single list item; for a tuple predicate every column value must equal
    /// the corresponding tuple slot. A row matches when any candidate row matches.
    /// </summary>
    /// <param name="row">The row to test.</param>
    /// <param name="parsed">The parsed predicate.</param>
    /// <returns>True when the row matches the predicate (before negation is applied).</returns>
    public static bool IsMatch(Dictionary<string, object> row, ParsedInPredicate parsed)
    {
        foreach (var values in parsed.Rows)
        {
            if (values.Length != parsed.Columns.Length)
            {
                continue;
            }

            bool allMatch = true;
            for (int i = 0; i < parsed.Columns.Length; i++)
            {
                if (!row.TryGetValue(parsed.Columns[i], out var v) || v is null or DBNull)
                {
                    allMatch = false;
                    break;
                }

                if (!string.Equals(v.ToString(), values[i], StringComparison.Ordinal))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Legacy scalar matcher: compares a single row value against an already-parsed list.
    /// </summary>
    public static bool IsMatch(object? rowValue, IEnumerable<string> items)
        => items.Contains(rowValue?.ToString() ?? string.Empty);

    /// <summary>
    /// Removes redundant outer parentheses from a logical condition so that
    /// <c>"(a = 1 OR b = 2)"</c> evaluates exactly like <c>"a = 1 OR b = 2"</c>.
    /// Only parentheses that enclose the WHOLE expression are stripped (e.g.
    /// <c>"(a = 1) OR (b = 2)"</c> is left intact), and parentheses inside string
    /// literals are ignored. Used by every WHERE evaluation path so parenthesized
    /// OR/AND predicates filter correctly (GitHub issue #348).
    /// </summary>
    public static string StripOuterParentheses(string condition)
    {
        var trimmed = condition.Trim();

        while (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')')
        {
            int depth = 0;
            bool inString = false;
            char quote = '\0';
            bool fullyWrapped = true;

            // Scan up to (but excluding) the final ')' — if the depth returns to 0 before
            // the end, the outer parens do not wrap the whole expression and must be kept.
            for (int i = 0; i < trimmed.Length - 1; i++)
            {
                char c = trimmed[i];

                if (inString)
                {
                    inString = c != quote; // closing quote exits the string literal
                    continue;
                }

                if (c is '\'' or '"')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        fullyWrapped = false;
                        break;
                    }
                }
            }

            // The scan excludes the final ')' (which balances the outer '('), so a fully
            // wrapped expression leaves exactly ONE unmatched '(' (depth == 1). If the depth
            // returns to 0 before the end, the outer parens do not wrap the whole expression
            // and must be kept.
            if (!fullyWrapped || depth != 1)
            {
                break;
            }

            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    /// <summary>
    /// Splits a condition on a logical keyword (AND / OR) that appears at the top level only —
    /// i.e. not inside parentheses or string literals. This keeps <c>IN ('a', 'b')</c> and
    /// <c>(a = 1 OR b = 2)</c> intact while still splitting <c>col = 1 OR col = 2</c>.
    /// </summary>
    public static List<string> SplitTopLevelLogical(string text, string keyword)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inString = false;
        char quote = '\0';
        int start = 0;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (inString)
            {
                inString = c != quote; // closing quote exits the string literal
                i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                inString = true;
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (IsLogicalKeywordAt(text, i, keyword, depth))
            {
                parts.Add(text[start..i].Trim());
                i += 1 + keyword.Length;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                start = i;
                continue;
            }

            i++;
        }

        parts.Add(text[start..].Trim());
        return parts;
    }

    /// <summary>
    /// True when <paramref name="keyword"/> (OR / AND) starts right after a top-level space at
    /// <paramref name="index"/> and is followed by whitespace, e.g. <c>"col = 1 OR col = 2"</c>.
    /// </summary>
    private static bool IsLogicalKeywordAt(string text, int index, string keyword, int depth)
    {
        if (depth != 0 || text[index] is not (' ' or '\t'))
        {
            return false;
        }

        var after = index + 1 + keyword.Length;
        return after < text.Length
            && text.AsSpan(index + 1, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(text[after]);
    }

    /// <summary>
    /// Evaluates a raw IN value list (e.g. <c>('a', 'b')</c>, <c>(1,2,3)</c> or
    /// <c>(VALUES ('a'), ('b'))</c>) against a single row value. Commas inside parentheses
    /// (tuple rows) do not split the list, and a leading <c>VALUES</c> keyword is ignored.
    /// </summary>
    /// <param name="rowValue">The row value to test.</param>
    /// <param name="listValue">The raw value list text.</param>
    /// <returns>True when the row value is contained in the list.</returns>
    public static bool ValueInList(string? rowValue, string? listValue)
    {
        if (listValue is null)
        {
            return false;
        }

        var list = listValue.Trim();
        if (list.StartsWith('(') && list.EndsWith(')'))
        {
            list = list[1..^1];
        }

        if (list.StartsWith(ValuesKeyword, StringComparison.OrdinalIgnoreCase))
        {
            list = list[ValuesKeyword.Length..].Trim();
        }

        return SplitTopLevel(list)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Select(StripSingleValue)
            .Any(item => string.Equals(item, rowValue, StringComparison.Ordinal));
    }

    /// <summary>
    /// Strips the wrapping parentheses (for single-value items such as <c>('a')</c>) and the
    /// surrounding quotes from one list item. Tuple items (<c>('a','b')</c>) are left as-is so
    /// they can never accidentally match a scalar column value.
    /// </summary>
    private static string StripSingleValue(string item)
    {
        if (item.StartsWith('(') && item.EndsWith(')') && !item[1..^1].Contains(','))
        {
            item = item[1..^1];
        }

        return item.Trim().Trim('\'', '"');
    }

    /// <summary>
    /// Parses the column part of an IN predicate: a bare column or a parenthesized tuple.
    /// </summary>
    private static string[] ParseColumns(string columnPart)
    {
        if (columnPart.StartsWith('(') && columnPart.EndsWith(')'))
        {
            return SplitTopLevel(columnPart[1..^1])
                .Select(NormalizeColumn)
                .Where(c => c.Length > 0)
                .ToArray();
        }

        var col = NormalizeColumn(columnPart);
        return col.Length > 0 ? [col] : [];
    }

    /// <summary>
    /// Parses the value-list part of an IN predicate (the content between the outer parentheses)
    /// into rows of values. Handles bare lists, SQLite <c>VALUES</c> rows and tuple rows.
    /// </summary>
    private static List<string[]> ParseValueRows(string listPart)
    {
        var list = listPart.Trim();

        // SQLite-style: IN (VALUES (v1), (v2)) — drop the keyword, keep the rows.
        if (list.StartsWith(ValuesKeyword, StringComparison.OrdinalIgnoreCase))
        {
            list = list[ValuesKeyword.Length..].Trim();
        }

        var rows = new List<string[]>();
        foreach (var item in SplitTopLevel(list))
        {
            var t = item.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (t.StartsWith('(') && t.EndsWith(')'))
            {
                // Tuple row: (v1, v2) — split the inner list on top-level commas.
                rows.Add(SplitTopLevel(t[1..^1])
                    .Select(v => v.Trim().Trim('\'', '"'))
                    .Where(v => v.Length > 0)
                    .ToArray());
            }
            else
            {
                rows.Add([t.Trim('\'', '"')]);
            }
        }

        return rows;
    }

    /// <summary>
    /// Splits a string on commas that are not inside parentheses or string literals. This is what
    /// lets <c>IN (VALUES ('a', 'x'), ('b', 'y'))</c> be split into two tuple rows instead of four
    /// scalar values.
    /// </summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inString = false;
        char quote = '\0';
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                inString = c != quote; // closing quote exits the string literal
                continue;
            }

            if (c is '\'' or '"')
            {
                inString = true;
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>
    /// Normalizes a column reference to the bare row-key form: strips alias qualifiers
    /// (e.g. <c>b.Url</c> to <c>Url</c>) and identifier quotes (<c>"</c>, <c>[</c>, <c>]</c>, <c>`</c>).
    /// </summary>
    private static string NormalizeColumn(string columnName)
    {
        var trimmed = columnName.Trim();
        var dotIndex = trimmed.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < trimmed.Length - 1)
        {
            trimmed = trimmed[(dotIndex + 1)..];
        }

        return trimmed.Trim('"', '[', ']', '`');
    }
}
