// <copyright file="SqlParser.DML.ScalarFunctions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// SqlParser partial — scalar SELECT support: literal expressions, aliases,
/// and change-counter functions (CHANGES, TOTAL_CHANGES, LAST_INSERT_ROWID). Scalar and date/time functions are
/// evaluated by <see cref="SqlScalarExpression"/>, the evaluator rows use too.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Executes a SELECT with no FROM clause, evaluating each expression as a literal
    /// or scalar function call.  Supports AS aliases and CHANGES() / TOTAL_CHANGES() /
    /// LAST_INSERT_ROWID().
    /// </summary>
    private List<Dictionary<string, object>> ExecuteSelectLiteralQuery(string selectClause)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var expression in SplitTopLevelComma(selectClause))
        {
            var trimmed = expression.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var alias = ExtractAlias(trimmed, out var exprText);
            var outputKey = alias ?? trimmed;
            row[outputKey] = EvaluateLiteralSelectExpression(exprText) ?? DBNull.Value;
        }

        return [row];
    }

    /// <summary>
    /// Evaluates a single literal SELECT expression: scalar functions, strings, numbers, booleans.
    /// </summary>
    private object? EvaluateLiteralSelectExpression(string expression)
    {
        // Scalar functions, CAST and operators go through the shared evaluator, the same one a row uses, so
        // date('…'), time('…'), datetime('…') and upper('abc') answer as SQLite does. Plain literals keep the typed
        // parse below.
        if (SqlScalarExpression.TryParse(expression, out var computed) && SqlScalarExpression.IsComputed(computed))
        {
            return SqlScalarExpression.Evaluate(computed, row: null);
        }

        var functionMatch = Regex.Match(
            expression,
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<args>.*)\)$",
            RegexOptions.Singleline, TimeSpan.FromSeconds(1));

        if (!functionMatch.Success)
        {
            return ParseLiteralScalarValue(expression);
        }

        return functionMatch.Groups["name"].Value.ToUpperInvariant() switch
        {
            "CHANGES" => _lastChanges,
            "TOTAL_CHANGES" => _totalChanges,
            "LAST_INSERT_ROWID" => _lastInsertRowId,
            _ => ParseLiteralScalarValue(expression),
        };
    }

    /// <summary>
    /// Parses an AS alias from a SELECT expression.
    /// Returns the alias name, or <c>null</c> if absent.
    /// </summary>
    private static string? ExtractAlias(string expression, out string expressionWithoutAlias)
    {
        var aliasMatch = Regex.Match(
            expression,
            @"^(?<expr>.+?)\s+AS\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

        if (aliasMatch.Success)
        {
            expressionWithoutAlias = aliasMatch.Groups["expr"].Value.Trim();
            return aliasMatch.Groups["alias"].Value.Trim();
        }

        expressionWithoutAlias = expression;
        return null;
    }

    /// <summary>
    /// Splits a comma-separated expression list, respecting quoted strings and parentheses.
    /// </summary>
    private static List<string> SplitTopLevelComma(string text)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return parts;
        }

        var sb = new StringBuilder();
        int depth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        foreach (var ch in text)
        {
            if (ch == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                sb.Append(ch);
                continue;
            }

            if (ch == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                sb.Append(ch);
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')' && depth > 0)
                {
                    depth--;
                }
                else if (ch == ',' && depth == 0)
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
        {
            parts.Add(sb.ToString());
        }

        return parts;
    }

    /// <summary>
    /// Parses a raw literal token into its native CLR type.
    /// </summary>
    private static object? ParseLiteralScalarValue(string literal)
    {
        if (literal.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (literal.Length >= 2 &&
            ((literal[0] == '\'' && literal[^1] == '\'') ||
             (literal[0] == '"' && literal[^1] == '"')))
        {
            return literal[1..^1];
        }

        if (int.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (decimal.TryParse(literal, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue;
        }

        if (double.TryParse(literal, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        if (bool.TryParse(literal, out var boolValue))
        {
            return boolValue;
        }

        return literal;
    }
}
