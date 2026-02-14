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
                column = parts[0].Trim().Trim('"', '[', ']', '`');
                value = parts[1].Trim().Trim('\'', '"');
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Tries to parse a range WHERE clause for B-tree optimization.
    /// Supports: "age > 30", "salary BETWEEN 50000 AND 100000", "age >= 25", etc.
    /// </summary>
    private static bool TryParseRangeWhereClause(
        string where,
        out string column,
        out string rangeStart,
        out string rangeEnd)
    {
        column = string.Empty;
        rangeStart = string.Empty;
        rangeEnd = string.Empty;

        if (string.IsNullOrWhiteSpace(where))
            return false;

        where = where.Trim();

        // Handle: column BETWEEN x AND y
        if (where.Contains("BETWEEN", StringComparison.OrdinalIgnoreCase))
        {
            var parts = where.Split(new[] { "BETWEEN", "AND" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 3)
            {
                column = parts[0];
                rangeStart = parts[1].Trim('\'', '"');
                rangeEnd = parts[2].Trim('\'', '"');
                return true;
            }
        }

        // Handle: column >= value
        if (where.Contains(">="))
        {
            var parts = where.Split(new[] { ">=" }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                rangeStart = parts[1].Trim().Trim('\'', '"');
                rangeEnd = "9999999999"; // Large number for upper bound
                return true;
            }
        }

        // Handle: column > value
        if (where.Contains('>') && !where.Contains('='))
        {
            var parts = where.Split('>');

            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                rangeStart = parts[1].Trim().Trim('\'', '"');
                rangeEnd = "9999999999";
                return true;
            }
        }

        // Handle: column <= value
        if (where.Contains("<="))
        {
            var parts = where.Split(new[] { "<=" }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                rangeStart = "0"; // Minimum value
                rangeEnd = parts[1].Trim().Trim('\'', '"');
                return true;
            }
        }

        // Handle: column < value
        if (where.Contains('<') && !where.Contains('='))
        {
            var parts = where.Split('<');
            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                rangeStart = "0";
                rangeEnd = parts[1].Trim().Trim('\'', '"');
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a value string to the appropriate type for B-tree lookup.
    /// </summary>
    private static object? ParseValueForBTreeLookup(string value, DataType type)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return type switch
            {
                // ✅ FIX: Use long for integer parsing to handle large range bounds like "9999999999"
                // Then convert to int if within range, otherwise keep as long
                DataType.Integer => ParseIntegerSafe(value),
                DataType.Long => long.Parse(value),
                DataType.Real => double.Parse(value),
                DataType.Decimal => decimal.Parse(value),
                DataType.String => value,
                DataType.DateTime => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                _ => value
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// ✅ NEW: Safely parses integer values, handling overflow by using long if needed.
    /// This allows range queries like "age > 30" with upper bound "9999999999".
    /// </summary>
    private static object ParseIntegerSafe(string value)
    {
        // Try int first (most common case)
        if (int.TryParse(value, out int intValue))
        {
            return intValue;
        }

        // If too large for int, use long (for range bounds like "9999999999")
        if (long.TryParse(value, out long longValue))
        {
            // ✅ FIX: For integer columns, clamp large values to int.MaxValue
            // This is safe for range upper bounds: "age < 9999999999" = "age < int.MaxValue"
            if (longValue > int.MaxValue)
            {
                return int.MaxValue;
            }
            if (longValue < int.MinValue)
            {
                return int.MinValue;
            }
            return (int)longValue;  // Safe cast: within int range
        }

        // Fallback: return int max value for upper bounds
        return int.MaxValue;
    }

    /// <summary>
    /// Deduplicates a list of rows by primary key.
    /// For UPDATE operations that may create multiple versions, only keeps the latest version per PK.
    /// </summary>
    internal List<Dictionary<string, object>> DeduplicateByPrimaryKey(List<Dictionary<string, object>> results)
    {
        if (this.PrimaryKeyIndex < 0 || results.Count == 0)
            return results;

        var pkCol = this.Columns[this.PrimaryKeyIndex];
        var seen = new HashSet<string>();
        var deduplicated = new List<Dictionary<string, object>>();

        foreach (var row in results)
        {
            if (row.TryGetValue(pkCol, out var pkVal) && pkVal != null)
            {
                var pkStr = pkVal.ToString() ?? string.Empty;
                if (seen.Add(pkStr))
                {
                    deduplicated.Add(row);
                }
            }
            else
            {
                deduplicated.Add(row);
            }
        }

        return deduplicated;
    }

    /// <summary>
    /// Deserializes a byte array into a row dictionary.
    /// Uses ReadTypedValueFromSpan to parse column values.
    /// ✅ OPTIMIZED: Uses SIMD batch deserialization for numeric columns.
    /// </summary>
    private Dictionary<string, object>? DeserializeRow(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        // ✅ OPTIMIZED: Use SIMD batch deserialization when beneficial
        return DeserializeRowWithSimd(data.AsSpan());
    }

    /// <summary>
    /// Applies ORDER BY to a list of rows.
    /// </summary>
    private static List<Dictionary<string, object>> ApplyOrdering(
        List<Dictionary<string, object>> results,
        string? orderBy,
        bool asc)
    {
        if (string.IsNullOrEmpty(orderBy) || results.Count == 0)
            return results;

        try
        {
            // Sort using LINQ with custom comparer (cast to IComparer<object?> for nullability)
            var sorted = asc
                ? results.OrderBy(r => r.TryGetValue(orderBy, out var val) ? val : null, (IComparer<object?>)Comparer<object>.Default)
                : results.OrderByDescending(r => r.TryGetValue(orderBy, out var val) ? val : null, (IComparer<object?>)Comparer<object>.Default);

            return sorted.ToList();
        }
        catch
        {
            // If sorting fails, return unsorted
            return results;
        }
    }
}
