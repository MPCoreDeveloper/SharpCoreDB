// <copyright file="SqlLiteralScanner.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// The single definition of how SharpCoreDB reads a SQL string literal — used by every scanner on the
/// statement path (VALUES and SET splitters, matching-parenthesis scan, parameter extraction, the batch
/// INSERT parsers and the wire-protocol parameter translation).
/// <para>
/// The rules are SQL's, and therefore SQLite's: inside <c>'…'</c> a doubled quote (<c>''</c>) is one quote and
/// does <b>not</b> close the literal, and a backslash is ordinary data. SharpCoreDB used to treat a backslash
/// as an escape and did not implement doubling at all, while
/// <see cref="ParameterBinder.FormatParameter"/> rendered strings with SQL doubling — so a bound value that
/// contained an apostrophe or ended in a backslash could not round-trip (known issue 7: the value was split at
/// its own comma and shifted into the next column, or the statement failed with "Mismatched parentheses").
/// Formatter and scanner now share one convention, so "any string comes back byte-exact" holds.
/// </para>
/// <para>
/// A backslash can never be an escape here: it is a legal last character of a value (a Windows path), and a
/// scanner that lets it suppress the closing quote cannot represent that value in a statement at all.
/// </para>
/// </summary>
internal static class SqlLiteralScanner
{
    /// <summary>
    /// Finds the closing quote of the literal that begins at <paramref name="start"/> (which must be the quote
    /// itself), honouring quote doubling. Returns the closing quote index, or <c>-1</c> when the literal is
    /// unterminated. <paramref name="hasDoubledQuote"/> reports whether the literal contains a doubled quote, so
    /// callers that slice the content know they must collapse it.
    /// </summary>
    /// <param name="text">The statement text.</param>
    /// <param name="start">Index of the opening quote.</param>
    /// <param name="hasDoubledQuote">Set when the literal contains <c>''</c> (or the doubled form of the quote in use).</param>
    /// <returns>The closing quote index, or <c>-1</c>.</returns>
    internal static int FindClosingQuote(ReadOnlySpan<char> text, int start, out bool hasDoubledQuote)
    {
        hasDoubledQuote = false;
        char quote = text[start];

        for (int i = start + 1; i < text.Length; i++)
        {
            if (text[i] != quote)
            {
                continue;
            }

            // A doubled quote is one quote of data: it does not close the literal.
            if (i + 1 < text.Length && text[i + 1] == quote)
            {
                hasDoubledQuote = true;
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    /// <summary>String overload of <see cref="FindClosingQuote(ReadOnlySpan{char}, int, out bool)"/>.</summary>
    internal static int FindClosingQuote(string text, int start, out bool hasDoubledQuote)
        => FindClosingQuote(text.AsSpan(), start, out hasDoubledQuote);

    /// <summary>
    /// Returns the index just past the closing quote of the literal that begins at
    /// <paramref name="start"/>, or <c>-1</c> when it is unterminated. Callers that only need to know where a
    /// literal ends (splitters, parenthesis matching, parameter extraction) use this.
    /// </summary>
    internal static int SkipLiteral(ReadOnlySpan<char> text, int start)
    {
        int closing = FindClosingQuote(text, start, out _);
        return closing < 0 ? -1 : closing + 1;
    }

    /// <summary>
    /// Splits a condition on whitespace that is <b>not</b> inside a quoted literal, a parenthesized group or a
    /// bracketed literal, returning the tokens with their text unaltered.
    /// <para>
    /// This is what a <c>WHERE</c> evaluator needs: splitting on spaces alone turns
    /// <c>name = 'it''s fine, really'</c> into three tokens of which the value is <c>'it''s</c>, so the row can
    /// never match (and a bracketed vector literal <c>[0.5, 0.25]</c> is split at its comma's space).
    /// </para>
    /// </summary>
    /// <param name="text">The condition text.</param>
    /// <returns>The top-level tokens, in order.</returns>
    internal static List<string> SplitTopLevelWhitespace(string text)
    {
        var tokens = new List<string>();
        int depth = 0;
        int bracketDepth = 0;
        int start = -1;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c is '\'' or '"')
            {
                if (start < 0)
                {
                    start = i;
                }

                int closing = FindClosingQuote(text, i, out _);
                if (closing < 0)
                {
                    i = text.Length - 1;   // unterminated literal: take the rest as one token
                    continue;
                }

                i = closing;
                continue;
            }

            if (char.IsWhiteSpace(c) && depth == 0 && bracketDepth == 0)
            {
                if (start >= 0)
                {
                    tokens.Add(text[start..i]);
                    start = -1;
                }

                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (c == '[')
            {
                bracketDepth++;
            }
            else if (c == ']' && bracketDepth > 0)
            {
                bracketDepth--;
            }

            if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            tokens.Add(text[start..]);
        }

        return tokens;
    }

    /// <summary>
    /// Removes exactly one pair of matching surrounding quotes and collapses the doubled quote inside it
    /// (<c>'it''s'</c> → <c>it's</c>). Text without a matching pair is returned unchanged, so a value that is
    /// not a literal is never altered. This replaces the earlier <c>Trim('\'')</c> / repeated-strip forms,
    /// which removed every leading and trailing quote and left the doubling in place.
    /// </summary>
    /// <param name="value">A literal (with quotes) or a bare value.</param>
    /// <returns>The unquoted value.</returns>
    internal static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        char quote = value[0];
        if (quote != '\'' && quote != '"')
        {
            return value;
        }

        if (value[^1] != quote)
        {
            return value;
        }

        var inner = value[1..^1];
        return quote == '\''
            ? inner.Replace("''", "'", StringComparison.Ordinal)
            : inner.Replace("\"\"", "\"", StringComparison.Ordinal);
    }
}
