// <copyright file="SqlFunctions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// Provides SQL functions for date/time and aggregation operations.
/// </summary>
public static class SqlFunctions
{
    /// <summary>
    /// Returns the current date and time.
    /// </summary>
    /// <returns>Current DateTime in UTC.</returns>
    public static DateTime Now() => DateTime.UtcNow;

    /// <summary>
    /// Returns the date portion of a DateTime.
    /// </summary>
    /// <param name="dateTime">The DateTime value.</param>
    /// <returns>The date portion.</returns>
    public static DateTime Date(DateTime dateTime) => dateTime.Date;

    /// <summary>
    /// Formats a DateTime according to a format string.
    /// </summary>
    /// <param name="dateTime">The DateTime value.</param>
    /// <param name="format">The format string (standard .NET format).</param>
    /// <returns>Formatted date string.</returns>
    public static string StrFTime(DateTime dateTime, string format)
    {
        return dateTime.ToString(format);
    }

    /// <summary>
    /// Adds a specified number of days, months, or years to a DateTime.
    /// </summary>
    /// <param name="dateTime">The base DateTime value.</param>
    /// <param name="value">The value to add.</param>
    /// <param name="unit">The unit: 'day', 'month', 'year', 'hour', 'minute', 'second'.</param>
    /// <returns>The modified DateTime.</returns>
    public static DateTime DateAdd(DateTime dateTime, int value, string unit)
    {
        return unit.ToLowerInvariant() switch
        {
            "day" or "days" => dateTime.AddDays(value),
            "month" or "months" => dateTime.AddMonths(value),
            "year" or "years" => dateTime.AddYears(value),
            "hour" or "hours" => dateTime.AddHours(value),
            "minute" or "minutes" => dateTime.AddMinutes(value),
            "second" or "seconds" => dateTime.AddSeconds(value),
            _ => throw new ArgumentException($"Unknown date unit: {unit}"),
        };
    }

    /// <summary>
    /// Calculates the sum of numeric values.
    /// </summary>
    /// <param name="values">The values to sum.</param>
    /// <returns>The sum.</returns>
    public static decimal Sum(IEnumerable<object> values)
    {
        decimal sum = 0;
        foreach (var val in values)
        {
            if (val != null && val != DBNull.Value)
            {
                sum += Convert.ToDecimal(val);
            }
        }

        return sum;
    }

    /// <summary>
    /// Calculates the average of numeric values.
    /// </summary>
    /// <param name="values">The values to average.</param>
    /// <returns>The average.</returns>
    public static decimal Avg(IEnumerable<object> values)
    {
        var list = values.Where(v => v != null && v != DBNull.Value).ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        decimal sum = 0;
        foreach (var val in list)
        {
            sum += Convert.ToDecimal(val);
        }

        return sum / list.Count;
    }

    /// <summary>
    /// Counts distinct values.
    /// </summary>
    /// <param name="values">The values to count.</param>
    /// <returns>The count of distinct values.</returns>
    public static int CountDistinct(IEnumerable<object> values)
    {
        var distinct = new HashSet<object>();
        foreach (var val in values)
        {
            if (val != null && val != DBNull.Value)
            {
                distinct.Add(val);
            }
        }

        return distinct.Count;
    }

    /// <summary>
    /// Concatenates values with a separator.
    /// </summary>
    /// <param name="values">The values to concatenate.</param>
    /// <param name="separator">The separator (default: comma).</param>
    /// <returns>The concatenated string.</returns>
    public static string GroupConcat(IEnumerable<object> values, string separator = ",")
    {
        var items = values
            .Where(v => v != null && v != DBNull.Value)
            .Select(v => v.ToString() ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s));
        return string.Join(separator, items);
    }

    /// <summary>
    /// Returns the ROWID of the most recent successful INSERT operation.
    /// Thread-safe: Each thread has its own last insert rowid.
    /// Compatible with SQLite's last_insert_rowid() function.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <returns>The ROWID of the last inserted row, or 0 if no inserts have occurred.</returns>
    public static long LastInsertRowId(IDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.GetLastInsertRowId();
    }

    /// <summary>
    /// Evaluates a SQL function call.
    /// </summary>
    /// <param name="functionName">The function name.</param>
    /// <param name="arguments">The function arguments.</param>
    /// <returns>The function result.</returns>
    public static object? EvaluateFunction(string functionName, List<object?> arguments)
    {
        return functionName.ToUpperInvariant() switch
        {
            "NOW" => Now(),
            "DATE" => arguments.Count > 0 && arguments[0] is DateTime dt ? Date(dt) : null,
            "STRFTIME" => arguments.Count >= 2 && arguments[0] is DateTime dt2 && arguments[1] is string fmt
                ? StrFTime(dt2, fmt) : null,
            "DATEADD" => arguments.Count >= 3 && arguments[0] is DateTime dt3 && arguments[1] is int val && arguments[2] is string unit
                ? DateAdd(dt3, val, unit) : null,
            _ => throw new NotSupportedException($"Function {functionName} is not supported"),
        };
    }
}
