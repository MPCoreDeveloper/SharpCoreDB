// <copyright file="SqlLiteralFormatter.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Globalization;

/// <summary>
/// Culture-invariant SQL text for values that are rendered into a statement: the one definition shared by
/// <c>ParameterBinder.FormatParameter</c>, <c>CompiledQueryExecutor.FormatParameterValue</c>,
/// <c>SqlFunctions.Quote</c> and <c>Table.FormatValue</c>.
/// <para>
/// Up to 2.1.4 each copy formatted on its own with the current culture: th-TH wrote the year 2026 as 2569 (its
/// Buddhist calendar), a <c>DateTimeOffset</c> became <c>'1/1/2026 12:30:15 PM +10:00'</c>, three copies dropped a
/// <c>DateTime</c>'s milliseconds, sv-SE wrote a negative number with U+2212 instead of '-', and a comma-decimal
/// culture wrote <c>1,5</c>. Dates use SQLite's canonical time-string form, which its date functions read.
/// </para>
/// </summary>
internal static class SqlLiteralFormatter
{
    /// <summary>SQLite's canonical date-time text, with milliseconds.</summary>
    internal const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>The canonical date-time text followed by the offset, which SQLite time strings accept as <c>[+-]HH:MM</c>.</summary>
    internal const string DateTimeOffsetFormat = "yyyy-MM-dd HH:mm:ss.fffzzz";

    /// <summary>Quoted literal of a <see cref="DateTime"/>.</summary>
    internal static string Literal(DateTime value) =>
        $"'{value.ToString(DateTimeFormat, CultureInfo.InvariantCulture)}'";

    /// <summary>Quoted literal of a <see cref="DateTimeOffset"/>.</summary>
    internal static string Literal(DateTimeOffset value) =>
        $"'{value.ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture)}'";

    /// <summary>Quoted literal of a <see cref="DateOnly"/>.</summary>
    internal static string Literal(DateOnly value) =>
        $"'{value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'";

    /// <summary>Quoted literal of a <see cref="TimeOnly"/>.</summary>
    internal static string Literal(TimeOnly value) =>
        $"'{value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}'";

    /// <summary>Unquoted, culture-invariant text of any other value (numbers, Guids, enums, …).</summary>
    internal static string Invariant(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}
