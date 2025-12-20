// <copyright file="ProjectionExecutor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Optimizations;

using SharpCoreDB.Services;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Projection executor for Span-based columnar query results.
/// Eliminates Dictionary materialization for queries like SELECT salary FROM employees.
/// 
/// Target performance (100k record query):
/// - Memory allocations: 9MB (9000+ objects) → &lt;100KB (&lt;10 objects)
/// - GC pressure: 10+ collections → 0-1 collection
/// - Scan time: 14.5ms (with Dictionary materialization) → &lt;2ms (pure array access)
/// - Speedup vs SQLite: 7x slower → 7x FASTER
/// </summary>
public static class ProjectionExecutor
{
    /// <summary>
    /// Projects rows to a single typed column using streaming.
    /// Example: SELECT salary FROM employees → Span&lt;decimal&gt;
    /// </summary>
    /// <typeparam name="T">The target column type.</typeparam>
    /// <param name="rows">The source rows.</param>
    /// <param name="columnName">The column to project.</param>
    /// <param name="columnType">The expected DataType.</param>
    /// <returns>Span of projected values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static T[] ProjectToColumn<T>(
        IEnumerable<StructRow> rows,
        string columnName,
        DataType columnType) where T : notnull
    {
        var rowList = rows as List<StructRow> ?? rows.ToList();
        var results = new T[rowList.Count];

        for (int i = 0; i < rowList.Count; i++)
        {
            var value = rowList[i].GetValue(columnName);
            results[i] = value switch
            {
                T typed => typed,
                null => default(T)!,
                _ => (T)Convert.ChangeType(value, typeof(T))
            };
        }

        return results;
    }

    /// <summary>
    /// Projects rows to multiple columns returning StructRow array.
    /// Example: SELECT id, name, salary FROM employees
    /// </summary>
    /// <param name="rows">The source rows.</param>
    /// <param name="columnIndices">Indices of columns to include.</param>
    /// <returns>Projected rows with only selected columns.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static StructRow[] ProjectToColumns(
        IEnumerable<StructRow> rows,
        int[] columnIndices)
    {
        var rowList = rows as List<StructRow> ?? rows.ToList();
        var results = new StructRow[rowList.Count];

        if (rowList.Count == 0)
            return results;

        // Get schema from first row
        var firstRow = rowList[0];
        var projectedColumns = new string[columnIndices.Length];
        var projectedTypes = new DataType[columnIndices.Length];

        for (int i = 0; i < columnIndices.Length; i++)
        {
            projectedColumns[i] = firstRow.Columns[columnIndices[i]];
            projectedTypes[i] = firstRow.ColumnTypes[columnIndices[i]];
        }

        // Project all rows
        for (int rowIdx = 0; rowIdx < rowList.Count; rowIdx++)
        {
            var sourceRow = rowList[rowIdx];
            var projectedValues = new object?[columnIndices.Length];

            for (int colIdx = 0; colIdx < columnIndices.Length; colIdx++)
            {
                projectedValues[colIdx] = sourceRow.Values[columnIndices[colIdx]];
            }

            results[rowIdx] = new StructRow
            {
                Columns = projectedColumns,
                ColumnTypes = projectedTypes,
                Values = projectedValues
            };
        }

        return results;
    }

    /// <summary>
    /// Projects single column with inline aggregation (no intermediate collection).
    /// Example: SELECT SUM(salary) FROM employees
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static decimal AggregateSum(
        IEnumerable<StructRow> rows,
        string columnName)
    {
        decimal sum = 0;
        foreach (var row in rows)
        {
            var value = row.GetValue(columnName);
            if (value is not null)
            {
                sum += Convert.ToDecimal(value);
            }
        }
        return sum;
    }

    /// <summary>
    /// Projects with COUNT aggregation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long AggregateCount(
        IEnumerable<StructRow> rows,
        string? columnName = null)
    {
        if (columnName is null)
        {
            // COUNT(*)
            return rows.Count();
        }

        // COUNT(columnName) - count non-null
        return rows.Count(r => r.GetValue(columnName) is not null);
    }

    /// <summary>
    /// Projects with AVG aggregation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static decimal AggregateAvg(
        IEnumerable<StructRow> rows,
        string columnName)
    {
        decimal sum = 0;
        long count = 0;

        foreach (var row in rows)
        {
            var value = row.GetValue(columnName);
            if (value is not null)
            {
                sum += Convert.ToDecimal(value);
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }

    /// <summary>
    /// Projects with MIN aggregation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static decimal AggregateMin(
        IEnumerable<StructRow> rows,
        string columnName)
    {
        decimal? min = null;

        foreach (var row in rows)
        {
            var value = row.GetValue(columnName);
            if (value is not null)
            {
                var decVal = Convert.ToDecimal(value);
                if (min is null || decVal < min)
                    min = decVal;
            }
        }

        return min ?? 0;
    }

    /// <summary>
    /// Projects with MAX aggregation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static decimal AggregateMax(
        IEnumerable<StructRow> rows,
        string columnName)
    {
        decimal? max = null;

        foreach (var row in rows)
        {
            var value = row.GetValue(columnName);
            if (value is not null)
            {
                var decVal = Convert.ToDecimal(value);
                if (max is null || decVal > max)
                    max = decVal;
            }
        }

        return max ?? 0;
    }

    /// <summary>
    /// Filters rows using streaming WHERE clause evaluation.
    /// Avoids materializing non-matching rows.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static IEnumerable<StructRow> FilterWhere(
        IEnumerable<StructRow> rows,
        System.Func<StructRow, bool> predicate)
    {
        // Streaming: yields matches without buffering
        return rows.Where(predicate);
    }

    /// <summary>
    /// Orders rows while minimizing allocations.
    /// Uses comparison function instead of key extraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static StructRow[] OrderBy(
        IEnumerable<StructRow> rows,
        string columnName,
        bool ascending = true)
    {
        var rowArray = rows as StructRow[] ?? rows.ToArray();

        Array.Sort(rowArray, (a, b) =>
        {
            var aVal = a.GetValue(columnName);
            var bVal = b.GetValue(columnName);

            if (aVal is null && bVal is null) return 0;
            if (aVal is null) return ascending ? -1 : 1;
            if (bVal is null) return ascending ? 1 : -1;

            int cmp = Compare(aVal, bVal);
            return ascending ? cmp : -cmp;
        });

        return rowArray;
    }

    /// <summary>
    /// Applies LIMIT and OFFSET to row sequence.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static StructRow[] LimitOffset(
        IEnumerable<StructRow> rows,
        int? limit,
        int? offset)
    {
        var enumerable = rows;

        if (offset.HasValue && offset.Value > 0)
            enumerable = enumerable.Skip(offset.Value);

        if (limit.HasValue && limit.Value > 0)
            enumerable = enumerable.Take(limit.Value);

        return enumerable.ToArray();
    }

    /// <summary>
    /// Compares two values for sorting.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Compare(object? a, object? b)
    {
        return (a, b) switch
        {
            (int x, int y) => x.CompareTo(y),
            (long x, long y) => x.CompareTo(y),
            (double x, double y) => x.CompareTo(y),
            (decimal x, decimal y) => x.CompareTo(y),
            (string x, string y) => string.Compare(x, y, StringComparison.Ordinal),
            (DateTime x, DateTime y) => x.CompareTo(y),
            _ => Comparer<object>.Default.Compare(a, b)
        };
    }

    /// <summary>
    /// Converts StructRow array to Dictionary list for compatibility.
    /// Should only be used when Dictionary return type is required.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static List<Dictionary<string, object>> ToDictionaryList(StructRow[] rows)
    {
        var result = new List<Dictionary<string, object>>(rows.Length);
        foreach (var row in rows)
        {
            result.Add(row.ToDictionary());
        }
        return result;
    }

    /// <summary>
    /// Converts Dictionary list to StructRow array.
    /// Useful for input transformation from legacy APIs.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static StructRow[] FromDictionaryList(
        List<Dictionary<string, object>> dicts,
        string[] columns,
        DataType[] columnTypes)
    {
        var result = new StructRow[dicts.Count];
        for (int i = 0; i < dicts.Count; i++)
        {
            result[i] = StructRow.FromDictionary(dicts[i], columns, columnTypes);
        }
        return result;
    }
}
