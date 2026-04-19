// <copyright file="FunctionalSqlTranslator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Functional;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Translates SharpCoreDB Functional SQL extensions into standard SQL
/// and provides metadata for <see cref="Option{T}"/> wrapping behavior.
/// <para>
/// Functional SQL extends standard SQL with keywords that integrate
/// directly with the <c>Option&lt;T&gt;</c> / <c>Fin&lt;T&gt;</c> type system:
/// </para>
/// <list type="bullet">
///   <item><c>OPTIONALLY FROM</c> — wraps each result row in <c>Option&lt;T&gt;</c></item>
///   <item><c>IS SOME</c> — filters rows where the column has a meaningful (non-null, non-empty) value</item>
///   <item><c>IS NONE</c> — filters rows where the column is null or empty</item>
///   <item><c>UNWRAP column AS alias</c> — extracts a column value with a fallback default in the projection</item>
///   <item><c>MATCH SOME column</c> / <c>MATCH NONE column</c> — explicit pattern match in WHERE</item>
/// </list>
/// <example>
/// <code>
/// SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS SOME
/// SELECT Id, Name, UNWRAP Email AS SafeEmail OPTIONALLY FROM Users
/// SELECT Id, Name FROM Users WHERE ManagerId IS NONE
/// </code>
/// </example>
/// </summary>
public sealed partial class FunctionalSqlTranslator
{
    /// <summary>
    /// Result of translating a Functional SQL statement.
    /// </summary>
    /// <param name="StandardSql">The translated standard SQL.</param>
    /// <param name="IsOptional">Whether results should be wrapped in <c>Option&lt;T&gt;</c>.</param>
    /// <param name="SomeColumns">Columns that must be non-null/non-empty (IS SOME filters).</param>
    /// <param name="NoneColumns">Columns that must be null/empty (IS NONE filters).</param>
    /// <param name="UnwrapMappings">UNWRAP column-to-alias mappings with default values.</param>
    public sealed record TranslationResult(
        string StandardSql,
        bool IsOptional,
        IReadOnlyList<string> SomeColumns,
        IReadOnlyList<string> NoneColumns,
        IReadOnlyList<UnwrapMapping> UnwrapMappings);

    /// <summary>
    /// Represents an UNWRAP column mapping.
    /// </summary>
    /// <param name="Column">The source column name.</param>
    /// <param name="Alias">The output alias.</param>
    /// <param name="DefaultValue">Optional default value literal.</param>
    public sealed record UnwrapMapping(string Column, string Alias, string? DefaultValue = null);

    // Patterns for functional SQL keywords (case-insensitive)
    [GeneratedRegex(@"\bOPTIONALLY\s+FROM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OptionallyFromPattern();

    [GeneratedRegex(@"\b(\w+)\s+IS\s+SOME\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IsSomePattern();

    [GeneratedRegex(@"\b(\w+)\s+IS\s+NONE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IsNonePattern();

    [GeneratedRegex(@"\bMATCH\s+SOME\s+(\w+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MatchSomePattern();

    [GeneratedRegex(@"\bMATCH\s+NONE\s+(\w+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MatchNonePattern();

    [GeneratedRegex(@"\bUNWRAP\s+(\w+)\s+AS\s+(\w+)(?:\s+DEFAULT\s+'([^']*)')?(?=\s|,|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UnwrapPattern();

    /// <summary>
    /// Determines whether the given SQL contains any Functional SQL extensions.
    /// </summary>
    /// <param name="sql">The SQL to check.</param>
    /// <returns><c>true</c> if functional keywords are present.</returns>
    public static bool IsFunctionalSql(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        return OptionallyFromPattern().IsMatch(sql)
            || IsSomePattern().IsMatch(sql)
            || IsNonePattern().IsMatch(sql)
            || MatchSomePattern().IsMatch(sql)
            || MatchNonePattern().IsMatch(sql)
            || UnwrapPattern().IsMatch(sql);
    }

    /// <summary>
    /// Translates a Functional SQL statement into standard SQL with metadata
    /// describing the <c>Option&lt;T&gt;</c> wrapping behavior.
    /// Functional predicates (<c>IS SOME</c>, <c>IS NONE</c>, <c>MATCH SOME/NONE</c>)
    /// are converted to neutral SQL expressions and enforced during functional
    /// post-filtering to keep behavior consistent across parser dialect variations.
    /// </summary>
    /// <param name="functionalSql">The Functional SQL statement.</param>
    /// <returns>A <see cref="TranslationResult"/> with standard SQL and metadata.</returns>
    public TranslationResult Translate(string functionalSql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionalSql);

        var sql = functionalSql.Trim();
        var isOptional = false;
        List<string> someColumns = [];
        List<string> noneColumns = [];
        List<UnwrapMapping> unwrapMappings = [];

        // 1. Detect and remove OPTIONALLY FROM → FROM
        if (OptionallyFromPattern().IsMatch(sql))
        {
            isOptional = true;
            sql = OptionallyFromPattern().Replace(sql, "FROM");
        }

        // 2. Extract UNWRAP ... AS ... DEFAULT '...' from SELECT projection
        foreach (Match match in UnwrapPattern().Matches(sql))
        {
            var column = match.Groups[1].Value;
            var alias = match.Groups[2].Value;
            var defaultValue = match.Groups[3].Success ? match.Groups[3].Value : null;
            unwrapMappings.Add(new UnwrapMapping(column, alias, defaultValue));
        }

        // Replace UNWRAP col AS alias DEFAULT 'x' → col AS alias in the SQL
        sql = UnwrapPattern().Replace(sql, "$1 AS $2");

        // 3. Extract IS SOME / IS NONE from WHERE clause
        foreach (Match match in IsSomePattern().Matches(sql))
        {
            someColumns.Add(match.Groups[1].Value);
        }

        foreach (Match match in IsNonePattern().Matches(sql))
        {
            noneColumns.Add(match.Groups[1].Value);
        }

        foreach (Match match in MatchSomePattern().Matches(sql))
        {
            someColumns.Add(match.Groups[1].Value);
        }

        foreach (Match match in MatchNonePattern().Matches(sql))
        {
            noneColumns.Add(match.Groups[1].Value);
        }

        // Remove functional predicates from SQL WHERE; they are enforced in FunctionalDb post-filtering.
        sql = RemoveFunctionalWherePredicates(sql);

        return new TranslationResult(
            sql.Trim(),
            isOptional,
            someColumns,
            noneColumns,
            unwrapMappings);
    }

    private static string RemoveFunctionalWherePredicates(string sql)
    {
        var cleaned = IsSomePattern().Replace(sql, string.Empty);
        cleaned = IsNonePattern().Replace(cleaned, string.Empty);
        cleaned = MatchSomePattern().Replace(cleaned, string.Empty);
        cleaned = MatchNonePattern().Replace(cleaned, string.Empty);

        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"\bWHERE\s+(AND|OR)\b", "WHERE", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(AND|OR)\s+(AND|OR)\b", "$2", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bWHERE\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\b(AND|OR)\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();

        return cleaned;
    }
}
