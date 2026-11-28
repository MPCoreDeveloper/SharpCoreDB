// <copyright file="SqlParser.SetExpressions.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// SqlParser partial — reading an UPDATE's SET values: a literal, or an expression computed per row.
/// <para>
/// Up to 2.1.4 every SET value was parsed as a literal of the column's type: <c>SET n = n + 5</c> failed with "Invalid
/// value 'n + 5'", and <c>SET label = name</c> on a TEXT column stored the four letters <c>name</c>. A value is now an
/// expression when it reads a column of the table or calls a function — as in SQLite. Text that is only constant
/// arithmetic keeps the literal reading it always had (an unquoted <c>2024-01-15</c> stays a date for a DATETIME
/// column), and is evaluated only when the literal reading fails (<c>SET n = 2 * 3</c>).
/// </para>
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="valueText"/> is an expression that reads a column of the table or calls
    /// a function, with its parsed form. A plain literal (a quoted string, a number, NULL, a parameter, a vector or blob
    /// literal) is recognised without parsing and allocates nothing.
    /// </summary>
    internal static bool IsComputedSetValue(string valueText, IReadOnlyList<string> columns, out SqlScalarExpression.Node node)
    {
        node = null!;
        if (IsPlainSetLiteral(valueText) || !SqlScalarExpression.TryParse(valueText.Trim(), out var parsed))
        {
            return false;
        }

        // Option 2 of issue #525: evaluate anything that parses as an expression — constant arithmetic (2 * 3), an
        // unquoted date (2024-01-15), a function call, CAST, a comparison — as long as every column it names is a
        // column of the table. A bare word that names no column (SET status = active) and an identifier the evaluator
        // cannot resolve keep the lenient literal reading, which is where SQLite itself would raise "no such column".
        // A bare literal (a quoted string, a number, NULL, …) is read by ParseSetLiteral.
        if ((!SqlScalarExpression.IsComputed(parsed) && !SqlScalarExpression.IsColumn(parsed)) ||
            !SqlScalarExpression.AllColumnRefsAreKnown(parsed, name => ContainsColumn(columns, name)))
        {
            return false;
        }

        node = parsed;
        return true;
    }

    /// <summary>
    /// Parses a SET value that is not a per-row expression: the literal parse, or — when that fails and the text is a
    /// constant expression such as <c>2 * 3</c> — its value.
    /// </summary>
    internal static object? ParseSetLiteral(string valueText, DataType type)
    {
        try
        {
            return ParseValue(valueText, type);
        }
        catch (InvalidOperationException) when (!IsPlainSetLiteral(valueText) &&
                                                SqlScalarExpression.TryParse(valueText.Trim(), out var constant))
        {
            return ConvertComputedSetValue(SqlScalarExpression.Evaluate(constant, null), type);
        }
    }

    /// <summary>Builds the per-row function for a computed SET value of a column of type <paramref name="type"/>.</summary>
    internal static Func<Dictionary<string, object>, object?> CompileSetExpression(SqlScalarExpression.Node node, DataType type) =>
        row => ConvertComputedSetValue(SqlScalarExpression.Evaluate(node, row), type);

    /// <summary>Converts a computed value to the column's storage type (SQL NULL stays <c>null</c>).</summary>
    internal static object? ConvertComputedSetValue(object? value, DataType type)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        try
        {
            return type switch
            {
                DataType.String => value as string ?? SqlScalarExpression.ToText(value),
                DataType.Integer => value switch
                {
                    int i => i,
                    long l => checked((int)l),
                    double d when double.IsInteger(d) => checked((int)d),
                    bool b => b ? 1 : 0,
                    _ => ParseValue(SqlScalarExpression.ToText(value), type),
                },
                DataType.Long => value switch
                {
                    int i => (long)i,
                    long l => l,
                    double d when double.IsInteger(d) => checked((long)d),
                    bool b => b ? 1L : 0L,
                    _ => ParseValue(SqlScalarExpression.ToText(value), type),
                },
                DataType.Real => value is string
                    ? ParseValue(SqlScalarExpression.ToText(value), type)
                    : Convert.ToDouble(value, CultureInfo.InvariantCulture),
                DataType.Decimal => value is string
                    ? ParseValue(SqlScalarExpression.ToText(value), type)
                    : Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                DataType.Boolean => value switch
                {
                    bool b => b,
                    string => ParseValue(SqlScalarExpression.ToText(value), type),
                    _ => Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0,
                },
                DataType.Blob => value as byte[] ?? Encoding.UTF8.GetBytes(SqlScalarExpression.ToText(value)),
                DataType.DateTime when value is DateTime => value,
                _ => ParseValue(SqlScalarExpression.ToText(value), type),
            };
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"The computed value {SqlScalarExpression.ToText(value)} does not fit a {type} column");
        }
    }

    /// <summary>
    /// Returns <c>true</c> for a SET value that is certainly a literal: one quoted string, a number, NULL/TRUE/FALSE, a
    /// parameter, a vector (<c>[…]</c>) or blob (<c>X'…'</c>) literal. Allocates nothing.
    /// </summary>
    private static bool IsPlainSetLiteral(string valueText) => IsPlainSetLiteral(valueText.AsSpan());

    /// <summary>The span form of <see cref="IsPlainSetLiteral(string)"/> — the batch INSERT path reads a span.</summary>
    internal static bool IsPlainSetLiteral(ReadOnlySpan<char> span)
    {
        span = span.Trim();
        if (span.IsEmpty)
        {
            return true;
        }

        char first = span[0];
        if (first == '\'')
        {
            for (int i = 1; i < span.Length; i++)
            {
                if (span[i] != '\'')
                {
                    continue;
                }

                if (i + 1 < span.Length && span[i + 1] == '\'')
                {
                    i++; // a doubled quote is one quote of data
                    continue;
                }

                return i == span.Length - 1;
            }

            return true; // unterminated: the literal parser reports it
        }

        if (first is '[' or '?' or '@' or ':' or '$' ||
            ((first is 'X' or 'x') && span.Length > 1 && span[1] == '\''))
        {
            return true;
        }

        return span.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
               double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// Reads one INSERT … VALUES / non-per-row UPDATE SET element as SQLite does under option 2 of issue #525: a plain
    /// literal is left to the caller's own reader; a date/time column keeps an unquoted date as a date (the deliberate
    /// exception — SharpCoreDB is typed, so it cannot store the integer SQLite's arithmetic would produce); and any
    /// other expression that parses and names no unresolvable identifier is evaluated and converted to
    /// <paramref name="type"/>. Returns <c>false</c> when the text is not such an expression, so the caller keeps its
    /// existing literal (lenient) reading.
    /// </summary>
    internal static bool TryEvaluateValuesExpression(ReadOnlySpan<char> value, DataType type, out object? result)
    {
        result = null;
        if (IsPlainSetLiteral(value))
        {
            return false;
        }

        string text = value.Trim().ToString();

        // #525 exception: an unquoted date in a date/time column stays a date. A DATETIME column cannot hold the
        // integer 2008 that SQLite's `2024-01-15` arithmetic would store, so the date reading is kept.
        if (type == DataType.DateTime && TryParseDateTimeLiteral(text, out result))
        {
            return true;
        }

        if (!SqlScalarExpression.TryParse(text, out var node) ||
            !SqlScalarExpression.IsComputed(node) ||
            !SqlScalarExpression.AllColumnRefsAreKnown(node, static _ => false))
        {
            return false;
        }

        result = ConvertComputedSetValue(SqlScalarExpression.Evaluate(node, null), type);
        return true;
    }

    /// <summary>Reads one INSERT … VALUES element: an evaluated expression when it is one, else the literal.</summary>
    internal static object? ParseInsertValue(string valueText, DataType type) =>
        TryEvaluateValuesExpression(valueText.AsSpan(), type, out var result) ? result : ParseValue(valueText, type);

    /// <summary>Parses <paramref name="valueText"/> as a date/time literal; <c>false</c> when it is not one.</summary>
    private static bool TryParseDateTimeLiteral(string valueText, out object? value)
    {
        try
        {
            value = ParseValue(valueText, DataType.DateTime);
            return true;
        }
        catch (InvalidOperationException)
        {
            value = null;
            return false;
        }
    }

    private static bool ContainsColumn(IReadOnlyList<string> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
