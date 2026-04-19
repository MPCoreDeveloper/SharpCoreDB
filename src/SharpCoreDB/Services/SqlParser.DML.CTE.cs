// <copyright file="SqlParser.DML.CTE.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// SqlParser partial — WITH [RECURSIVE] CTE execution.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Executes a WITH [RECURSIVE] … SELECT statement.
    /// Supports a single CTE with optional RECURSIVE self-reference via UNION ALL.
    /// </summary>
    private List<Dictionary<string, object>> ExecuteWithCte(string sql)
    {
        // Strip WITH / WITH RECURSIVE prefix
        var afterWith = Regex.Match(sql, @"^WITH\s+(?:RECURSIVE\s+)?(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!afterWith.Success)
            throw new InvalidOperationException($"Invalid WITH syntax: {sql}");

        var body = afterWith.Groups[1].Value.Trim();

        // Parse: cteName(cols) AS ( … ) SELECT …
        var cteNameMatch = Regex.Match(body,
            @"^(\w+)\s*\(([^)]*)\)\s+AS\s*\((.+?)\)\s+(SELECT\s+.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!cteNameMatch.Success)
            throw new InvalidOperationException($"Unsupported WITH syntax (only single CTE supported): {body}");

        var cteName = cteNameMatch.Groups[1].Value.Trim();
        var colNames = cteNameMatch.Groups[2].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cteBody = cteNameMatch.Groups[3].Value.Trim();
        var outerSelect = cteNameMatch.Groups[4].Value.Trim();

        // Detect UNION ALL split (for recursive CTEs)
        var unionAllIdx = IndexOfUnionAll(cteBody);
        List<Dictionary<string, object>> cteRows;

        if (unionAllIdx < 0)
        {
            // Non-recursive: evaluate seed query
            cteRows = EvaluateCteQuery(cteBody, cteName, [], colNames);
        }
        else
        {
            // Recursive: seed + recursive step
            var seedSql = cteBody[..unionAllIdx].Trim();
            var recursiveSql = cteBody[(unionAllIdx + "UNION ALL".Length)..].Trim();

            var seedRows = EvaluateCteQuery(seedSql, cteName, [], colNames);
            cteRows = [.. seedRows];

            List<Dictionary<string, object>> frontierRows = [.. seedRows];
            for (int iteration = 0; iteration < 10_000 && frontierRows.Count > 0; iteration++)
            {
                var newRows = EvaluateCteQuery(recursiveSql, cteName, frontierRows, colNames);
                if (newRows.Count == 0)
                {
                    break;
                }

                cteRows.AddRange(newRows);
                frontierRows = newRows;
            }
        }

        // Execute outer SELECT substituting the CTE virtual table
        return ApplyCteSelect(outerSelect, cteName, cteRows, colNames);
    }

    /// <summary>
    /// Finds the position of UNION ALL at the top level (not inside nested parens) in a CTE body.
    /// </summary>
    private static int IndexOfUnionAll(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length - 8; i++)
        {
            if (text[i] == '(') { depth++; continue; }
            if (text[i] == ')') { depth--; continue; }
            if (depth == 0 && text[i..].StartsWith("UNION ALL", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Evaluates a single CTE SELECT (seed or recursive step) given the current CTE rows.
    /// </summary>
    private List<Dictionary<string, object>> EvaluateCteQuery(
        string querySql,
        string cteName,
        List<Dictionary<string, object>> currentRows,
        string[] colNames)
    {
        // Resolve literal SELECT (no FROM)
        var trimmed = querySql.Trim();
        if (!trimmed.Contains(" FROM ", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("SELECT " + cteName, StringComparison.OrdinalIgnoreCase))
        {
            // Seed: SELECT <literal_expr1>, <literal_expr2>, …
            var selectClause = Regex.Replace(trimmed, @"^SELECT\s+", string.Empty, RegexOptions.IgnoreCase).Trim();
            return EvaluateLiteralCteRow(selectClause, colNames);
        }

        // Recursive step: SELECT expr FROM cteName WHERE …
        var fromMatch = Regex.Match(trimmed,
            @"^SELECT\s+(?<sel>.+?)\s+FROM\s+" + Regex.Escape(cteName) + @"(?:\s+WHERE\s+(?<where>.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!fromMatch.Success)
            throw new InvalidOperationException($"Unsupported recursive CTE step: {trimmed}");

        var selectExprStr = fromMatch.Groups["sel"].Value.Trim();
        var whereStr = fromMatch.Groups["where"].Success ? fromMatch.Groups["where"].Value.Trim() : null;

        var selectExprs = selectExprStr.Split(',', StringSplitOptions.TrimEntries);
        var results = new List<Dictionary<string, object>>();

        foreach (var row in currentRows)
        {
            // Apply WHERE (e.g. "n < 5")
            if (whereStr is not null && !EvaluateCteWhere(row, whereStr))
                continue;

            var newRow = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < colNames.Length && i < selectExprs.Length; i++)
            {
                newRow[colNames[i]] = EvaluateCteExpression(selectExprs[i].Trim(), row);
            }
            results.Add(newRow);
        }
        return results;
    }

    /// <summary>Evaluates a literal SELECT row for a seed CTE (no FROM clause).</summary>
    private static List<Dictionary<string, object>> EvaluateLiteralCteRow(string selectClause, string[] colNames)
    {
        var exprs = selectClause.Split(',', StringSplitOptions.TrimEntries);
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < colNames.Length && i < exprs.Length; i++)
        {
            row[colNames[i]] = ParseCteScalar(exprs[i].Trim());
        }
        return [row];
    }

    /// <summary>Evaluates a CTE WHERE clause against a row (simple comparisons only).</summary>
    private static bool EvaluateCteWhere(Dictionary<string, object> row, string where)
    {
        // Support simple: col op literal  (e.g.  n < 5)
        var m = Regex.Match(where.Trim(), @"^(\w+)\s*(<=|>=|<>|!=|<|>|=)\s*(.+)$");
        if (!m.Success) return true;
        var col = m.Groups[1].Value;
        var op = m.Groups[2].Value;
        var rhs = ParseCteScalar(m.Groups[3].Value.Trim());
        if (!row.TryGetValue(col, out var lhs)) return false;
        return CompareCteValues(lhs, op, rhs);
    }

    private static bool CompareCteValues(object lhs, string op, object rhs)
    {
        try
        {
            var l = Convert.ToDouble(lhs, CultureInfo.InvariantCulture);
            var r = Convert.ToDouble(rhs, CultureInfo.InvariantCulture);
            return op switch
            {
                "=" => l == r,
                "!=" or "<>" => l != r,
                "<" => l < r,
                "<=" => l <= r,
                ">" => l > r,
                ">=" => l >= r,
                _ => false
            };
        }
        catch
        {
            return string.Compare(lhs?.ToString(), rhs?.ToString(), StringComparison.Ordinal) == 0;
        }
    }

    /// <summary>Evaluates a column expression in the context of a CTE row (supports col+N, col-N).</summary>
    private static object EvaluateCteExpression(string expr, Dictionary<string, object> row)
    {
        // Simple column reference
        if (row.TryGetValue(expr, out var direct)) return direct;

        // Arithmetic: colName +/- literal  (e.g. n+1)
        var arith = Regex.Match(expr, @"^(\w+)\s*([+\-\*\/])\s*(.+)$");
        if (arith.Success)
        {
            var colName = arith.Groups[1].Value;
            var oper = arith.Groups[2].Value;
            var rhsStr = arith.Groups[3].Value.Trim();
            if (row.TryGetValue(colName, out var colVal))
            {
                try
                {
                    var l = Convert.ToDouble(colVal, CultureInfo.InvariantCulture);
                    var r = Convert.ToDouble(rhsStr, CultureInfo.InvariantCulture);
                    double result = oper switch
                    {
                        "+" => l + r,
                        "-" => l - r,
                        "*" => l * r,
                        "/" => l / r,
                        _ => l
                    };
                    // Preserve integer type if both operands were integers
                    if (colVal is int or long && double.IsInteger(result) && result >= long.MinValue && result <= long.MaxValue)
                        return (long)result;
                    return result;
                }
                catch { /* fall through */ }
            }
        }

        return ParseCteScalar(expr);
    }

    /// <summary>Parses a scalar literal (number or string).</summary>
    private static object ParseCteScalar(string s)
    {
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return s.Trim('\'', '"');
    }

    /// <summary>
    /// Applies the outer SELECT against the CTE virtual rows.
    /// Supports: SELECT cols FROM cteName [WHERE …] [LIMIT n]
    /// </summary>
    private static List<Dictionary<string, object>> ApplyCteSelect(
        string outerSelect,
        string cteName,
        List<Dictionary<string, object>> cteRows,
        string[] colNames)
    {
        var m = Regex.Match(outerSelect,
            @"^SELECT\s+(?<sel>.+?)\s+FROM\s+" + Regex.Escape(cteName)
            + @"(?:\s+WHERE\s+(?<where>.+?))?(?:\s+LIMIT\s+(?<limit>\d+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            throw new InvalidOperationException($"Unsupported outer CTE SELECT: {outerSelect}");

        var sel = m.Groups["sel"].Value.Trim();
        var whereStr = m.Groups["where"].Success ? m.Groups["where"].Value.Trim() : null;
        var limitStr = m.Groups["limit"].Success ? m.Groups["limit"].Value : null;

        IEnumerable<Dictionary<string, object>> filtered = cteRows;
        if (whereStr is not null)
            filtered = filtered.Where(r => EvaluateCteWhere(r, whereStr));

        if (limitStr is not null && int.TryParse(limitStr, out var lim))
            filtered = filtered.Take(lim);

        // Project columns
        var selectCols = sel.Equals("*", StringComparison.Ordinal)
            ? colNames.ToList()
            : sel.Split(',', StringSplitOptions.TrimEntries).ToList();

        return filtered.Select(row =>
        {
            var projected = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in selectCols)
            {
                if (row.TryGetValue(col, out var v))
                    projected[col] = v;
            }
            return projected;
        }).ToList();
    }
}
