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
/// parsing from drifting between paths (GitHub issue #339).
/// </summary>
internal static class SqlInPredicate
{
    private static readonly Regex InConditionPattern = new(
        @"^(.+?)\s+(NOT\s+)?IN\s*\((.*)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Parses a single-condition IN / NOT IN clause: <c>col IN ('a', 'b')</c> or
    /// <c>col NOT IN (1, 2)</c>. Returns the normalized column name (alias-qualified and
    /// quoted references are stripped), whether the clause is negated, and the trimmed items.
    /// </summary>
    /// <param name="condition">The condition text, e.g. <c>node_type IN ('WorkItem', 'Person')</c>.</param>
    /// <param name="column">The normalized column name.</param>
    /// <param name="negated">True when the clause is <c>NOT IN</c>.</param>
    /// <param name="items">The trimmed, quote-stripped list items.</param>
    /// <returns>True when the condition is a well-formed IN/NOT IN clause.</returns>
    public static bool TryParseCondition(string condition, out string column, out bool negated, out List<string> items)
    {
        column = string.Empty;
        negated = false;
        items = [];

        var match = InConditionPattern.Match(condition);
        if (!match.Success)
        {
            return false;
        }

        column = NormalizeColumn(match.Groups[1].Value);
        negated = match.Groups[2].Success;
        items = match.Groups[3].Value
            .Split(',')
            .Select(v => v.Trim().Trim('\'', '"'))
            .ToList();

        return true;
    }

    /// <summary>
    /// Evaluates a row value against an already-parsed IN list by comparing <see cref="object.ToString"/>.
    /// </summary>
    /// <param name="rowValue">The row value to test.</param>
    /// <param name="items">The parsed list items.</param>
    /// <returns>True when the row value is contained in the list.</returns>
    public static bool IsMatch(object? rowValue, IEnumerable<string> items)
        => items.Contains(rowValue?.ToString() ?? string.Empty);

    /// <summary>
    /// Evaluates a raw IN value list (e.g. <c>('a', 'b')</c> or <c>(1,2,3)</c>) against a row value.
    /// Strips surrounding parentheses (when present), splits on commas and trims quotes.
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

        var trimmed = listValue.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.Split(',').Select(v => v.Trim().Trim('\'', '"')).Contains(rowValue);
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
