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
    /// Returns the current UTC timestamp ticks for sync change tracking.
    /// </summary>
    /// <returns>UTC ticks as a long.</returns>
    public static long SyncTimestamp() => DateTimeOffset.UtcNow.Ticks;

    /// <summary>
    /// Returns the current UTC timestamp (SQLite-compatible CURRENT_TIMESTAMP).
    /// </summary>
    /// <returns>Current DateTime in UTC.</returns>
    public static DateTime CurrentTimestamp() => DateTime.UtcNow;

    /// <summary>
    /// Returns the current UTC date (SQLite-compatible CURRENT_DATE).
    /// </summary>
    /// <returns>Current date in UTC.</returns>
    public static DateTime CurrentDate() => DateTime.UtcNow.Date;

    /// <summary>
    /// Returns the current UTC time of day (SQLite-compatible CURRENT_TIME).
    /// </summary>
    /// <returns>Current time of day in UTC.</returns>
    public static TimeSpan CurrentTime() => DateTime.UtcNow.TimeOfDay;

    /// <summary>
    /// Converts a hexadecimal string to a UTF-8 string.
    /// SQLite-compatible UNHEX behavior for textual payloads.
    /// </summary>
    /// <param name="hex">Hexadecimal string.</param>
    /// <returns>Decoded UTF-8 string.</returns>
    /// <exception cref="ArgumentException">Thrown when input is not valid even-length hex.</exception>
    public static string Unhex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        if ((hex.Length & 1) != 0)
        {
            throw new ArgumentException("UNHEX input must have an even number of characters.", nameof(hex));
        }

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            var pair = hex.AsSpan(i, 2);
            if (!byte.TryParse(pair, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var b))
            {
                throw new ArgumentException("UNHEX input contains non-hex characters.", nameof(hex));
            }

            bytes[i / 2] = b;
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Returns an SQL literal representation of a value (SQLite QUOTE semantics).
    /// </summary>
    /// <param name="value">The value to quote.</param>
    /// <returns>Quoted SQL literal text.</returns>
    public static string Quote(object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            string s => $"'{s.Replace("'", "''", StringComparison.Ordinal)}'",
            bool b => b ? "1" : "0",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss zzz}'",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                 ?? "NULL",
        };
    }

    /// <summary>
    /// Returns a string built from one or more Unicode code points.
    /// SQLite-compatible CHAR semantics.
    /// </summary>
    /// <param name="codePoints">Unicode code points.</param>
    /// <returns>Constructed string.</returns>
    /// <exception cref="ArgumentException">Thrown when a code point is outside valid Unicode range.</exception>
    public static string Char(params int[] codePoints)
    {
        ArgumentNullException.ThrowIfNull(codePoints);

        if (codePoints.Length == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(codePoints.Length);
        foreach (var cp in codePoints)
        {
            if (cp is < 0 or > 0x10FFFF)
            {
                throw new ArgumentException("CHAR input contains an invalid Unicode code point.", nameof(codePoints));
            }

            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the Unicode code point of the first character in a string.
    /// SQLite-compatible UNICODE semantics.
    /// </summary>
    /// <param name="value">Input string.</param>
    /// <returns>Unicode code point, or null for null/empty input.</returns>
    public static int? Unicode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return char.ConvertToUtf32(value, 0);
    }

    /// <summary>
    /// Evaluates a SQL function call.
    /// </summary>
    /// <param name="functionName">The function name.</param>
    /// <param name="arguments">The function arguments.</param>
    /// <param name="customProviders">Optional custom function providers for extensions (e.g., vector search).</param>
    /// <returns>The function result.</returns>
    public static object? EvaluateFunction(string functionName, List<object?> arguments, IReadOnlyList<ICustomFunctionProvider>? customProviders = null)
    {
        var upperName = functionName.ToUpperInvariant();

        return upperName switch
        {
            "NOW" => Now(),
            "CURRENT_TIMESTAMP" => CurrentTimestamp(),
            "CURRENT_DATE" => CurrentDate(),
            "CURRENT_TIME" => CurrentTime(),
            "SYNC_TIMESTAMP" => SyncTimestamp(),
            "DATE" => arguments.Count > 0 && arguments[0] is DateTime dt ? Date(dt) : null,
            "STRFTIME" => arguments.Count >= 2 && arguments[0] is DateTime dt2 && arguments[1] is string fmt
                ? StrFTime(dt2, fmt) : null,
            "DATEADD" => arguments.Count >= 3 && arguments[0] is DateTime dt3 && arguments[1] is int val && arguments[2] is string unit
                ? DateAdd(dt3, val, unit) : null,
            "UNHEX" => arguments.Count >= 1 && arguments[0] is not null ? Unhex(arguments[0]!.ToString() ?? string.Empty) : null,
            "QUOTE" => arguments.Count >= 1 ? Quote(arguments[0]) : "NULL",
            "CHAR" => Char([.. arguments.Select(ConvertToCharCodePoint)]),
            "UNICODE" => arguments.Count >= 1 ? Unicode(arguments[0]?.ToString()) : null,
            _ => EvaluateCustomFunction(upperName, arguments, customProviders),
        };
    }

    private static int ConvertToCharCodePoint(object? value)
    {
        return value switch
        {
            null or DBNull => throw new ArgumentException("CHAR does not accept NULL code points."),
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            short s => s,
            byte b => b,
            string s when int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) => i,
            _ => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Tries registered custom function providers before throwing NotSupportedException.
    /// </summary>
    private static object? EvaluateCustomFunction(string functionName, List<object?> arguments, IReadOnlyList<ICustomFunctionProvider>? providers)
    {
        if (providers is not null)
        {
            foreach (var provider in providers)
            {
                if (provider.CanHandle(functionName))
                {
                    return provider.Evaluate(functionName, arguments);
                }
            }
        }

        throw new NotSupportedException($"Function {functionName} is not supported");
    }
}
