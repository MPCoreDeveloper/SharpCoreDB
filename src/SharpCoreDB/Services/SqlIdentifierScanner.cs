// <copyright file="SqlIdentifierScanner.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;

/// <summary>
/// The single definition of how a statement's object name (and the keywords in front of it) is read — used by
/// the statement executor's INSERT path and by the INSERT parsers of the batch path.
/// <para>
/// SQL does not care where the whitespace in a statement is, and SQLite accepts a line break or a tab wherever
/// a space is legal. SharpCoreDB's INSERT parser did care: it assumed exactly one space after
/// <c>INSERT INTO</c> and read the table name as "everything up to the next space". A statement laid out over
/// several lines — which is what linq2db's SQLite dialect, DbUp, Flyway and most pretty-printers emit — was
/// therefore rejected with a nonsensical <c>Table notes]\n(\n\t[id],… does not exist</c> (the table name ran to
/// the first space <em>inside the string literal</em>), and <c>INSERT\nINTO t …</c> threw
/// <c>ArgumentOutOfRangeException</c> from a <c>-1</c> slice.
/// </para>
/// <para>
/// An identifier is read as one token: a quoted name is read to its closing quote — <c>[…]</c>, <c>"…"</c> or
/// <c>`…`</c>, with a doubled closing quote standing for one character of data — and a bare name ends at
/// whitespace or at one of the punctuation characters that can legally follow it (<c>(</c>, <c>)</c>,
/// <c>,</c>, <c>;</c>).
/// </para>
/// </summary>
internal static class SqlIdentifierScanner
{
    /// <summary>
    /// Skips SQL whitespace (space, tab, CR, LF and the other Unicode whitespace characters) from
    /// <paramref name="index"/> and returns the first index that is not whitespace.
    /// </summary>
    /// <param name="text">The statement text.</param>
    /// <param name="index">The index to start at.</param>
    /// <returns>The index of the next non-whitespace character, or <paramref name="text"/>.Length.</returns>
    internal static int SkipWhitespace(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Matches <paramref name="keyword"/> at the first non-whitespace character at or after
    /// <paramref name="index"/>, case-insensitively, and requires the keyword to end at a word boundary.
    /// </summary>
    /// <param name="text">The statement text.</param>
    /// <param name="index">The index to start at (whitespace is skipped).</param>
    /// <param name="keyword">The keyword to match.</param>
    /// <param name="next">The index just past the keyword, or <c>0</c> when it does not match.</param>
    /// <returns><c>true</c> when the keyword was matched.</returns>
    internal static bool TrySkipKeyword(ReadOnlySpan<char> text, int index, string keyword, out int next)
    {
        next = 0;

        int start = SkipWhitespace(text, index);
        if (start + keyword.Length > text.Length)
        {
            return false;
        }

        if (!text.Slice(start, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int after = start + keyword.Length;
        if (after < text.Length && !char.IsWhiteSpace(text[after]))
        {
            return false;
        }

        next = after;
        return true;
    }

    /// <summary>
    /// Reads the identifier token that starts at the first non-whitespace character at or after
    /// <paramref name="index"/>. The returned bounds include any surrounding quotes.
    /// </summary>
    /// <param name="text">The statement text.</param>
    /// <param name="index">The index to start at (whitespace is skipped).</param>
    /// <param name="start">The index of the token's first character.</param>
    /// <param name="end">The index just past the token's last character.</param>
    /// <returns><c>true</c> when a non-empty identifier was read.</returns>
    internal static bool TryReadIdentifier(ReadOnlySpan<char> text, int index, out int start, out int end)
    {
        start = SkipWhitespace(text, index);
        end = 0;

        if (start >= text.Length)
        {
            start = 0;
            return false;
        }

        char open = text[start];
        if (open is '"' or '[' or '`')
        {
            char close = open == '[' ? ']' : open;
            for (int i = start + 1; i < text.Length; i++)
            {
                if (text[i] != close)
                {
                    continue;
                }

                // A doubled closing quote is one character of data: it does not close the identifier.
                if (i + 1 < text.Length && text[i + 1] == close)
                {
                    i++;
                    continue;
                }

                end = i + 1;
                return true;
            }

            start = 0;
            return false;   // unterminated quoted identifier
        }

        int cursor = start;
        while (cursor < text.Length)
        {
            char c = text[cursor];
            if (char.IsWhiteSpace(c) || c is '(' or ')' or ',' or ';')
            {
                break;
            }

            cursor++;
        }

        if (cursor == start)
        {
            start = 0;
            return false;
        }

        end = cursor;
        return true;
    }

    /// <summary>
    /// Returns the text of an identifier with exactly one pair of matching quotes removed and a doubled closing
    /// quote collapsed (<c>[my table]</c> → <c>my table</c>). A bare name is returned unchanged.
    /// </summary>
    /// <param name="identifier">The identifier token, quotes included.</param>
    /// <returns>The name the catalog is keyed by.</returns>
    internal static string IdentifierText(ReadOnlySpan<char> identifier)
    {
        if (identifier.Length < 2)
        {
            return identifier.ToString();
        }

        char open = identifier[0];
        char close = open == '[' ? ']' : open;
        if (open is not ('"' or '[' or '`') || identifier[^1] != close)
        {
            return identifier.ToString();
        }

        var inner = identifier[1..^1].ToString();
        return inner.IndexOf(close) < 0
            ? inner
            : inner.Replace(new string(close, 2), close.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds the target of an INSERT statement anywhere in <paramref name="statement"/> and returns the bounds of
    /// its identifier token. Whitespace between the keywords may be any kind, and SQLite's optional conflict
    /// clause (<c>INSERT OR REPLACE INTO …</c>) is skipped when present.
    /// </summary>
    /// <param name="statement">The statement text.</param>
    /// <param name="start">The index of the identifier's first character.</param>
    /// <param name="end">The index just past the identifier's last character.</param>
    /// <returns><c>true</c> when an INSERT target was found.</returns>
    internal static bool TryFindInsertTarget(ReadOnlySpan<char> statement, out int start, out int end)
    {
        start = 0;
        end = 0;

        int search = 0;
        while (search < statement.Length)
        {
            int insertAt = statement[search..].IndexOf("INSERT", StringComparison.OrdinalIgnoreCase);
            if (insertAt < 0)
            {
                return false;
            }

            insertAt += search;

            // Word boundary in front, so a name such as "reINSERT" is not mistaken for the verb.
            bool atWordStart = insertAt == 0 || !char.IsLetterOrDigit(statement[insertAt - 1]);
            if (atWordStart && TrySkipKeyword(statement, insertAt, "INSERT", out int afterInsert))
            {
                int cursor = SkipWhitespace(statement, afterInsert);
                if (TrySkipKeyword(statement, cursor, "OR", out int afterOr))
                {
                    // INSERT OR REPLACE|IGNORE|ABORT|FAIL|ROLLBACK INTO …
                    int clauseEnd = SkipWhitespace(statement, afterOr);
                    while (clauseEnd < statement.Length && char.IsLetter(statement[clauseEnd]))
                    {
                        clauseEnd++;
                    }

                    cursor = clauseEnd;
                }

                if (TrySkipKeyword(statement, cursor, "INTO", out int afterInto) &&
                    TryReadIdentifier(statement, afterInto, out start, out end))
                {
                    return true;
                }
            }

            search = insertAt + "INSERT".Length;
        }

        return false;
    }
}
