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

        // ✅ OPTIMIZED: Use pre-compiled filter expression instead of re-parsing WHERE clause
        // Get all rows from the table
        var allRows = table.Select();
        var results = allRows;

        // Apply WHERE filter using compiled expression tree (zero parsing!)
        if (plan.HasWhereClause && plan.WhereFilter is not null)
        {
            results = allRows.Where(plan.WhereFilter).ToList();
        }

        // Apply ORDER BY
        if (!string.IsNullOrEmpty(plan.OrderByColumn))
        {
            results = plan.OrderByAscending
                ? results.OrderBy(row => row.TryGetValue(plan.OrderByColumn, out var val) ? val : null).ToList()
                : results.OrderByDescending(row => row.TryGetValue(plan.OrderByColumn, out var val) ? val : null).ToList();
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

        // Apply projection if needed
        if (plan.HasProjection && plan.ProjectionFunc is not null && results.Count > 0)
        {
            results = results.Select(plan.ProjectionFunc).ToList();
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
}
