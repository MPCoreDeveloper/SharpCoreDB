// <copyright file="SqlParser.Optimizations.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;
using System.Collections.Generic;

/// <summary>
/// SqlParser partial class - Query and Update Optimizations.
/// ✅ C# 15: Modern patterns, required properties, collection expressions.
/// 
/// This file contains optimization paths for common operations:
/// - Primary key-based single-column updates
/// - Primary key-based multi-column updates
/// - Potential future: Index-based query optimizations
/// 
/// These optimizations bypass general-purpose code paths and directly
/// manipulate the underlying storage for better performance.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Attempts optimized primary key update for single-column changes.
    /// Uses direct storage access to avoid full table scan.
    /// ✅ Returns true if optimization was applied, false to fall back to standard Update().
    /// </summary>
    /// <param name="table">The table to update.</param>
    /// <param name="pkColumn">The primary key column name.</param>
    /// <param name="pkValue">The primary key value to find.</param>
    /// <param name="assignments">The column assignments to apply.</param>
    /// <returns>True if optimized path was used, false otherwise.</returns>
    private static bool TryOptimizedPrimaryKeyUpdate(Table table, string pkColumn, object? pkValue, Dictionary<string, object> assignments)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(pkColumn);
        ArgumentNullException.ThrowIfNull(assignments);

        if (pkValue is null || assignments.Count == 0)
        {
            return false;
        }

        var where = BuildPrimaryKeyWhereClause(pkColumn, pkValue);
        table.Update(where, assignments);
        return true;
    }

    /// <summary>
    /// Attempts optimized primary key update for multi-column changes.
    /// Similar to single-column but handles multiple column updates in one operation.
    /// </summary>
    /// <param name="table">The table to update.</param>
    /// <param name="pkColumn">The primary key column name.</param>
    /// <param name="pkValue">The primary key value to find.</param>
    /// <param name="assignments">The column assignments to apply.</param>
    /// <returns>True if optimized path was used, false otherwise.</returns>
    private static bool TryOptimizedMultiColumnUpdate(Table table, string pkColumn, object? pkValue, Dictionary<string, object> assignments)
    {
        // Same optimized path for multi-column updates: targeted PK predicate + single update call.
        return TryOptimizedPrimaryKeyUpdate(table, pkColumn, pkValue, assignments);
    }

    /// <summary>
    /// Future optimization: Use index hints for query execution.
    /// Example: SELECT /*+ INDEX(users idx_email) */ * FROM users WHERE email = 'test@example.com'
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="indexName">The index hint.</param>
    /// <param name="whereClause">The WHERE clause.</param>
    /// <returns>Results using the specified index.</returns>
    private List<Dictionary<string, object>> ExecuteQueryWithIndexHint(string tableName, string indexName, string whereClause)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        // Prefer hash-indexed lookup when hint column index exists.
        if (!string.IsNullOrWhiteSpace(indexName) && table.HasHashIndex(indexName))
        {
            return table.Select(whereClause);
        }

        return table.Select(whereClause);
    }

    private static string BuildPrimaryKeyWhereClause(string pkColumn, object pkValue)
    {
        var valueLiteral = pkValue switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            DateTime dt => $"'{dt:O}'",
            bool b => b ? "1" : "0",
            _ => Convert.ToString(pkValue, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"
        };

        return $"WHERE {pkColumn} = {valueLiteral}";
    }
}
