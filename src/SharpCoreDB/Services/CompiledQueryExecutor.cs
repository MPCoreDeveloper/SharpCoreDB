// <copyright file="CompiledQueryExecutor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Executes compiled query plans with zero parsing overhead.
/// Expected performance: 5-10x faster than re-parsing for repeated queries.
/// Target: 1000 identical SELECTs in less than 8ms total.
/// </summary>
public class CompiledQueryExecutor
{
    private readonly Dictionary<string, ITable> tables;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompiledQueryExecutor"/> class.
    /// </summary>
    /// <param name="tables">The database tables.</param>
    public CompiledQueryExecutor(Dictionary<string, ITable> tables)
    {
        this.tables = tables;
    }

    /// <summary>
    /// Executes a compiled query plan with parameters.
    /// Zero parsing overhead - uses pre-compiled expression trees.
    /// </summary>
    /// <param name="plan">The compiled query plan.</param>
    /// <param name="parameters">The query parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> Execute(
        CompiledQueryPlan plan,
        Dictionary<string, object?>? parameters = null)
    {
        // Get the table
        if (!tables.TryGetValue(plan.TableName, out var table))
        {
            throw new InvalidOperationException($"Table {plan.TableName} does not exist");
        }

        // ✅ OPTIMIZED (Task 2.1): Single-pass filtering + ordering instead of LINQ chaining
        // Eliminates multiple .ToList() allocations
        var allRows = table.Select();
        var filtered = new List<Dictionary<string, object>>(allRows.Count);

        // Apply WHERE filter (single pass, no intermediate list)
        if (plan.HasWhereClause && plan.WhereFilter is not null)
        {
            foreach (var row in allRows)
            {
                if (plan.WhereFilter(row))
                {
                    filtered.Add(row);
                }
            }
        }
        else
        {
            // No filter - copy all rows
            filtered.AddRange(allRows);
        }

        // Apply ORDER BY (in-place sort, no intermediate allocation)
        if (!string.IsNullOrEmpty(plan.OrderByColumn))
        {
            filtered.Sort((a, b) =>
            {
                // Safe dictionary lookup with null handling
                var aVal = a.TryGetValue(plan.OrderByColumn, out var av) ? av : null;
                var bVal = b.TryGetValue(plan.OrderByColumn, out var bv) ? bv : null;

                // Compare values
                return CompareValues(aVal, bVal);
            });

            // Reverse if descending
            if (!plan.OrderByAscending)
            {
                filtered.Reverse();
            }
        }

        // Apply OFFSET + LIMIT (combined to avoid second allocation)
        var offset = plan.Offset ?? 0;
        var limit = plan.Limit ?? int.MaxValue;

        List<Dictionary<string, object>> results;
        if (offset > 0 || limit < int.MaxValue)
        {
            // Only allocate if OFFSET/LIMIT applied
            results = new List<Dictionary<string, object>>(Math.Min(filtered.Count - offset, limit));
            var end = Math.Min(offset + limit, filtered.Count);

            for (int i = offset; i < end; i++)
            {
                results.Add(filtered[i]);
            }
        }
        else
        {
            results = filtered;
        }

        // Apply projection if needed (final transformation)
        if (plan.HasProjection && plan.ProjectionFunc is not null && results.Count > 0)
        {
            var projected = new List<Dictionary<string, object>>(results.Count);
            foreach (var row in results)
            {
                projected.Add(plan.ProjectionFunc(row));
            }
            results = projected;
        }

        return results;
    }

    /// <summary>
    /// Executes a compiled query plan with parameterized WHERE clause.
    /// Binds parameters before execution for optimal performance.
    /// </summary>
    /// <param name="plan">The compiled query plan.</param>
    /// <param name="parameters">The query parameters to bind.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteParameterized(
        CompiledQueryPlan plan,
        Dictionary<string, object?> parameters)
    {
        // Get the table
        if (!tables.TryGetValue(plan.TableName, out var table))
        {
            throw new InvalidOperationException($"Table {plan.TableName} does not exist");
        }

        // Extract WHERE clause and bind parameters
        var whereClause = ExtractWhereClause(plan.Sql);
        if (!string.IsNullOrEmpty(whereClause))
        {
            // Simple parameter substitution
            whereClause = BindParametersInWhereClause(whereClause, parameters);
        }

        // Use table's built-in Select
        var results = string.IsNullOrEmpty(whereClause)
            ? table.Select()
            : table.Select(whereClause, plan.OrderByColumn, plan.OrderByAscending);

        // Apply projection
        if (plan.HasProjection && plan.ProjectionFunc is not null && results.Count > 0)
        {
            results = results.Select(plan.ProjectionFunc).ToList();
        }

        // Apply OFFSET
        if (plan.Offset.HasValue && plan.Offset.Value > 0)
        {
            results = results.Skip(plan.Offset.Value).ToList();
        }

        // Apply LIMIT
        if (plan.Limit.HasValue && plan.Limit.Value > 0)
        {
            results = results.Take(plan.Limit.Value).ToList();
        }

        return results;
    }

    /// <summary>
    /// Binds parameters in a WHERE clause.
    /// </summary>
    private static string BindParametersInWhereClause(string whereClause, Dictionary<string, object?> parameters)
    {
        var result = whereClause;
        
        foreach (var param in parameters)
        {
            var paramName = param.Key.StartsWith('@') ? param.Key : $"@{param.Key}";
            var value = FormatParameterValue(param.Value);
            result = result.Replace(paramName, value);
        }
        
        return result;
    }

    /// <summary>
    /// Formats a parameter value for SQL substitution.
    /// </summary>
    private static string FormatParameterValue(object? value)
    {
        if (value == null) return "NULL";
        
        return value switch
        {
            string s => $"'{s.Replace("'", "''")}'",
            int i => i.ToString(),
            long l => l.ToString(),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            _ => $"'{value.ToString()?.Replace("'", "''") ?? string.Empty}'"
        };
    }

    /// <summary>
    /// Extracts the WHERE clause from a SQL statement.
    /// </summary>
    private static string ExtractWhereClause(string sql)
    {
        var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIndex < 0) return string.Empty;

        var endIndex = sql.Length;
        var orderByIndex = sql.IndexOf("ORDER BY", whereIndex, StringComparison.OrdinalIgnoreCase);
        var limitIndex = sql.IndexOf("LIMIT", whereIndex, StringComparison.OrdinalIgnoreCase);

        if (orderByIndex > 0) endIndex = Math.Min(endIndex, orderByIndex);
        if (limitIndex > 0) endIndex = Math.Min(endIndex, limitIndex);

        return sql.Substring(whereIndex + 6, endIndex - whereIndex - 6).Trim();
    }

    /// <summary>
    /// ✅ Compares two values safely (handles nulls, different types).
    /// </summary>
    private static int CompareValues(object? a, object? b)
    {
        // Null handling
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        // Try direct comparison first
        if (a is IComparable comp)
        {
            try
            {
                return comp.CompareTo(b);
            }
            catch
            {
                // Fall through to string comparison
            }
        }

        // Fallback to string comparison
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }
}
