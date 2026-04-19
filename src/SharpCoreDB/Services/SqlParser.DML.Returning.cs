// <copyright file="SqlParser.DML.Returning.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// SqlParser partial — RETURNING clause helpers for INSERT, UPDATE, DELETE.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Strips a trailing RETURNING clause from a DML statement and returns the
    /// requested column names. Returns <c>null</c> when no RETURNING clause exists.
    /// </summary>
    /// <param name="sql">The original SQL string.</param>
    /// <param name="sqlWithoutReturning">The SQL with the RETURNING clause removed.</param>
    /// <returns>Ordered list of column names (or <c>*</c>) to project, or <c>null</c>.</returns>
    private static List<string>? TryExtractReturningColumns(string sql, out string sqlWithoutReturning)
    {
        var returningIndex = sql.IndexOf(" RETURNING ", StringComparison.OrdinalIgnoreCase);
        if (returningIndex < 0)
        {
            sqlWithoutReturning = sql;
            return null;
        }

        sqlWithoutReturning = sql[..returningIndex].TrimEnd().TrimEnd(';');
        var returningPart = sql[(returningIndex + " RETURNING ".Length)..].Trim().TrimEnd(';');
        return [.. returningPart.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim())];
    }

    /// <summary>
    /// Projects a set of rows through a RETURNING column list.
    /// Passing <c>*</c> as the only column returns a shallow copy of every row.
    /// </summary>
    private static List<Dictionary<string, object>> ProjectReturningRows(
        List<Dictionary<string, object>> rows,
        List<string> returningColumns)
    {
        if (returningColumns.Count == 1 && returningColumns[0] == "*")
        {
            return [.. rows.Select(r => new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase))];
        }

        var result = new List<Dictionary<string, object>>(rows.Count);
        foreach (var row in rows)
        {
            var projected = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in returningColumns)
            {
                if (TryGetValueIgnoreCase(row, column, out var value))
                {
                    projected[column] = value ?? DBNull.Value;
                }
            }

            result.Add(projected);
        }

        return result;
    }

    /// <summary>
    /// Case-insensitive dictionary value lookup.
    /// </summary>
    private static bool TryGetValueIgnoreCase(
        Dictionary<string, object> row,
        string key,
        out object? value)
    {
        if (row.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var kv in row)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
