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
/// and change-counter functions (CHANGES, TOTAL_CHANGES, LAST_INSERT_ROWID).
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
        var functionMatch = Regex.Match(
            expression,
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<args>.*)\)$",
            RegexOptions.Singleline);

        if (!functionMatch.Success)
        {
            return ParseLiteralScalarValue(expression);
        }

        return functionMatch.Groups["name"].Value.ToUpperInvariant() switch
        {
            "CHANGES" => _lastChanges,
            "TOTAL_CHANGES" => _totalChanges,
            "LAST_INSERT_ROWID" => _lastInsertRowId,
            "JULIANDAY" => EvaluateJulianDay(functionMatch.Groups["args"].Value),
            "UNIXEPOCH" => EvaluateUnixEpoch(functionMatch.Groups["args"].Value),
            "STRFTIME" => EvaluateStrftime(functionMatch.Groups["args"].Value),
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
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

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

    // ─── Date / time scalar functions ────────────────────────────────────────

    /// <summary>Parses the first quoted date argument from a function arg list.</summary>
    private static DateTime ParseDateArg(string args)
    {
        var parts = SplitTopLevelComma(args);
        if (parts.Count == 0) return DateTime.UtcNow;
        var raw = parts[0].Trim().Trim('\'', '"');
        if (raw.Equals("now", StringComparison.OrdinalIgnoreCase)) return DateTime.UtcNow;
        var dt = DateTime.Parse(raw, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        // SQLite treats dates without timezone as UTC
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return dt;
    }

    /// <summary>Applies SQLite date modifiers ('start of month', '+N days', etc.) to a DateTime.</summary>
    private static DateTime ApplyDateModifiers(DateTime dt, IReadOnlyList<string> modifiers)
    {
        foreach (var rawMod in modifiers)
        {
            var mod = rawMod.Trim().Trim('\'', '"').Trim();
            if (mod.Equals("start of month", StringComparison.OrdinalIgnoreCase))
                dt = new DateTime(dt.Year, dt.Month, 1, dt.Hour, dt.Minute, dt.Second, dt.Kind);
            else if (mod.Equals("start of year", StringComparison.OrdinalIgnoreCase))
                dt = new DateTime(dt.Year, 1, 1, dt.Hour, dt.Minute, dt.Second, dt.Kind);
            else if (mod.Equals("start of day", StringComparison.OrdinalIgnoreCase))
                dt = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Kind);
            else
            {
                var m = Regex.Match(mod, @"^([+-]?\d+)\s+(day|month|year|hour|minute|second)s?$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    dt = m.Groups[2].Value.ToUpperInvariant() switch
                    {
                        "DAY" => dt.AddDays(n),
                        "MONTH" => dt.AddMonths(n),
                        "YEAR" => dt.AddYears(n),
                        "HOUR" => dt.AddHours(n),
                        "MINUTE" => dt.AddMinutes(n),
                        "SECOND" => dt.AddSeconds(n),
                        _ => dt
                    };
                }
            }
        }
        return dt;
    }

    /// <summary>Evaluates JULIANDAY(date [, modifier ...]) → double.</summary>
    private static object EvaluateJulianDay(string args)
    {
        var parts = SplitTopLevelComma(args);
        var dt = ParseDateArg(args);
        if (parts.Count > 1) dt = ApplyDateModifiers(dt, parts.Skip(1).ToList());
        // Julian Day Number: days since noon January 1, 4713 BC
        var epoch = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc); // J2000.0 = 2451545.0
        const double j2000 = 2451545.0;
        return j2000 + (dt.ToUniversalTime() - epoch).TotalDays;
    }

    /// <summary>Evaluates UNIXEPOCH(date [, modifier ...]) → long (seconds since 1970-01-01).</summary>
    private static object EvaluateUnixEpoch(string args)
    {
        var parts = SplitTopLevelComma(args);
        var dt = ParseDateArg(args);
        if (parts.Count > 1) dt = ApplyDateModifiers(dt, parts.Skip(1).ToList());
        var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(dt.ToUniversalTime() - unixEpoch).TotalSeconds;
    }

    /// <summary>Evaluates strftime(format, date [, modifier ...]) → string.</summary>
    private static object? EvaluateStrftime(string args)
    {
        var parts = SplitTopLevelComma(args);
        if (parts.Count < 2) return null;
        var fmt = parts[0].Trim().Trim('\'', '"');
        var dt = ParseDateArg(parts[1]);
        if (parts.Count > 2) dt = ApplyDateModifiers(dt, parts.Skip(2).ToList());
        // Map SQLite strftime format specifiers to .NET format
        var result = fmt
            .Replace("%Y", dt.Year.ToString("D4", CultureInfo.InvariantCulture))
            .Replace("%m", dt.Month.ToString("D2", CultureInfo.InvariantCulture))
            .Replace("%d", dt.Day.ToString("D2", CultureInfo.InvariantCulture))
            .Replace("%H", dt.Hour.ToString("D2", CultureInfo.InvariantCulture))
            .Replace("%M", dt.Minute.ToString("D2", CultureInfo.InvariantCulture))
            .Replace("%S", dt.Second.ToString("D2", CultureInfo.InvariantCulture))
            .Replace("%j", dt.DayOfYear.ToString("D3", CultureInfo.InvariantCulture))
            .Replace("%w", ((int)dt.DayOfWeek).ToString(CultureInfo.InvariantCulture))
            .Replace("%f", $"{dt.Second:D2}.{dt.Millisecond:D3}")
            .Replace("%%", "%");
        return result;
    }
}
