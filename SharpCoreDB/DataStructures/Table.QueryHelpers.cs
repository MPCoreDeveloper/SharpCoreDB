// <copyright file="Table.QueryHelpers.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Query helper methods for Table - WHERE clause parsing, row deserialization, result ordering.
/// These are core methods used by SELECT operations across all storage modes.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Tries to parse a simple WHERE clause of the form "column = value".
    /// </summary>
    /// <param name="where">The WHERE clause string.</param>
    /// <param name="column">Output: the column name.</param>
    /// <param name="value">Output: the value.</param>
    /// <returns>True if successfully parsed, false otherwise.</returns>
    private static bool TryParseSimpleWhereClause(string where, out string column, out object value)
    {
        column = string.Empty;
        value = string.Empty;
        
        if (string.IsNullOrWhiteSpace(where))
            return false;
        
        // Handle: column = value
        if (where.Contains('='))
        {
            var parts = where.Split('=', 2);
            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                value = parts[1].Trim().Trim('\'', '"');
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Deserializes binary row data into a dictionary.
    /// Uses the table's column schema to parse the data.
    /// </summary>
    /// <param name="data">The binary row data.</param>
    /// <returns>Deserialized row dictionary, or null if deserialization fails.</returns>
    private Dictionary<string, object>? DeserializeRow(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;
        
        var row = new Dictionary<string, object>();
        int offset = 0;
        var dataSpan = data.AsSpan();
        
        try
        {
            for (int i = 0; i < Columns.Count; i++)
            {
                if (offset >= dataSpan.Length)
                    return null;

                var value = ReadTypedValueFromSpan(dataSpan.Slice(offset), ColumnTypes[i], out int bytesRead);
                row[Columns[i]] = value;
                offset += bytesRead;
            }

            return row;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies ORDER BY clause to query results.
    /// </summary>
    /// <param name="results">The query results to order.</param>
    /// <param name="orderBy">The column to order by (optional).</param>
    /// <param name="asc">Whether to order ascending (default true).</param>
    /// <returns>Ordered results.</returns>
    private static List<Dictionary<string, object>> ApplyOrdering(
        List<Dictionary<string, object>> results, 
        string? orderBy, 
        bool asc)
    {
        if (string.IsNullOrWhiteSpace(orderBy) || results.Count == 0)
            return results;
        
        // Check if orderBy column exists in first row
        if (!results[0].ContainsKey(orderBy))
            return results;
        
        try
        {
            if (asc)
            {
                return results.OrderBy(r => r.TryGetValue(orderBy, out var val) ? val : null).ToList();
            }
            else
            {
                return results.OrderByDescending(r => r.TryGetValue(orderBy, out var val) ? val : null).ToList();
            }
        }
        catch
        {
            // If ordering fails (e.g., incompatible types), return unsorted
            return results;
        }
    }
}
