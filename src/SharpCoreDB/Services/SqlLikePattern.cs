// <copyright file="SqlLikePattern.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// The single definition of SQL <c>LIKE</c> pattern matching: <c>%</c> matches any sequence, <c>_</c> matches one
/// character, and an <c>ESCAPE</c> character makes the following wildcard literal. The rules are SQLite's: the
/// comparison is case-insensitive for ASCII, and a wildcard also matches a line break.
/// <para>
/// SharpCoreDB had five independent copies of this logic (the WHERE evaluator, the JOIN evaluator, the fast
/// predicate compiler, the collation comparator and the AST evaluator), none of which implemented
/// <c>ESCAPE</c> — so <c>name LIKE 'prefix~_%' ESCAPE '~'</c>, which is exactly what linq2db's SQLite dialect
/// emits for <c>string.StartsWith</c>, matched nothing.
/// </para>
/// </summary>
internal static class SqlLikePattern
{
    /// <summary>
    /// Matches <paramref name="value"/> against a LIKE <paramref name="pattern"/>.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <param name="pattern">The pattern, without surrounding quotes.</param>
    /// <param name="escape">The <c>ESCAPE</c> character, when the statement supplies one.</param>
    /// <returns><c>true</c> when the value matches.</returns>
    internal static bool IsMatch(string value, string pattern, char? escape = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pattern);

        var regex = new StringBuilder(pattern.Length * 2 + 2);
        regex.Append('^');

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (escape is { } escapeChar && c == escapeChar && i + 1 < pattern.Length)
            {
                regex.Append(Regex.Escape(pattern[++i].ToString()));
                continue;
            }

            regex.Append(c switch
            {
                '%' => ".*",
                '_' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }

        regex.Append('$');

        return Regex.IsMatch(
            value,
            regex.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
    }
}
