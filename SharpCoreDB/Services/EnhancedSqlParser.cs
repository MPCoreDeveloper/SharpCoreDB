// <copyright file="EnhancedSqlParser.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Enhanced SQL parser with support for complex queries, multiple dialects, and error recovery.
/// SPLIT INTO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - EnhancedSqlParser.cs: Core class definition, fields, and main Parse method
/// - EnhancedSqlParser.Select.cs: SELECT statement parsing and related helpers
/// - EnhancedSqlParser.DML.cs: INSERT, UPDATE, DELETE statement parsing
/// - EnhancedSqlParser.DDL.cs: CREATE TABLE and DDL statement parsing
/// - EnhancedSqlParser.Expressions.cs: Expression, literal, and operator parsing
/// </summary>
public partial class EnhancedSqlParser
{
    private readonly List<string> _errors = [];
    private string _sql = string.Empty;
    private int _position;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnhancedSqlParser"/> class.
    /// </summary>
    /// <param name="dialect">The SQL dialect to use.</param>
    public EnhancedSqlParser(ISqlDialect? dialect = null)
    {
    }

    /// <summary>
    /// Gets the list of parsing errors.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Gets whether any errors were encountered during parsing.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>
    /// Parses a SQL statement into an AST.
    /// </summary>
    /// <param name="sql">The SQL statement to parse.</param>
    /// <returns>The root AST node, or null if parsing failed critically.</returns>
    public SqlNode? Parse(string sql)
    {
        _errors.Clear();
        _sql = sql;
        _position = 0;

        try
        {
            var keyword = PeekKeyword();

            return keyword?.ToUpperInvariant() switch
            {
                "SELECT" => ParseSelect(),
                "INSERT" => ParseInsert(),
                "UPDATE" => ParseUpdate(),
                "DELETE" => ParseDelete(),
                "CREATE" => ParseCreate(),
                "ALTER" => ParseAlter(),
                _ => throw new InvalidOperationException($"Unsupported statement type: {keyword}")
            };
        }
        catch (Exception ex)
        {
            RecordError($"Critical parsing error: {ex.Message}");
            return null;
        }
    }

    // Common helper methods used across all partial classes
    
    private void RecordError(string message)
    {
        _errors.Add($"[Position {_position}] {message}");
    }

    private string? PeekKeyword()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(\w+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private void ConsumeKeyword()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(\w+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
        }
    }

    private bool MatchKeyword(string keyword)
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(" + keyword + @")\b", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return true;
        }
        return false;
    }

    private string? ConsumeIdentifier()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*([\w]+|""[^""]+""|\[[^\]]+\]|`[^`]+`)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            var identifier = match.Groups[1].Value;
            // Remove quotes - S6610 fix: use char overload
            if ((identifier.StartsWith('"') && identifier.EndsWith('"')) ||
                (identifier.StartsWith('[') && identifier.EndsWith(']')) ||
                (identifier.StartsWith('`') && identifier.EndsWith('`')))
            {
                identifier = identifier.Substring(1, identifier.Length - 2);
            }
            return identifier;
        }
        return null;
    }

    private bool MatchToken(string token)
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(" + Regex.Escape(token) + @")", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return true;
        }
        return false;
    }

    private int? ParseInteger()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return int.Parse(match.Groups[1].Value);
        }
        return null;
    }

    private static bool IsReservedKeyword(string keyword)
    {
        var reserved = new[] { "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "FULL", "INNER", "OUTER", "CROSS", "ON", "GROUP", "BY", "HAVING", "ORDER", "LIMIT", "OFFSET" };
        return reserved.Contains(keyword.ToUpperInvariant());
    }
}
