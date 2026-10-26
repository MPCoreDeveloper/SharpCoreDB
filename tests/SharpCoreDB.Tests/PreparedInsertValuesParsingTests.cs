// <copyright file="PreparedInsertValuesParsingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Globalization;
using Xunit;

/// <summary>
/// Pins the value splitter behind the prepared-insert hot path
/// (<see cref="Database.PreparedInsertStatement.ParseValues"/> and
/// <see cref="Database.PreparedInsertStatement.ParseValuesToArray"/>).
/// <para>
/// The two parsers used to carry their own copy of the same splitting loop — one returning a row dictionary, one a
/// column-ordered array — so a fix could land in one and silently miss the other. They now share a single
/// <c>ref struct</c> enumerator, and every case here asserts through <see cref="ReadBoth"/> that both parsers
/// produce the same values for the same clause. Re-forking the splitter fails these tests.
/// </para>
/// <para>
/// The rules pinned are the ones <see cref="InsertValuesParsingTests"/> pins for the SQL path: a quoted literal is
/// skipped as a whole (<c>''</c> is one quote of data, a backslash is data and can be the last character), a comma
/// inside quotes or inside a bracketed VECTOR literal does not split, a parenthesised list stays together, and
/// every value is trimmed.
/// </para>
/// </summary>
public sealed class PreparedInsertValuesParsingTests
{
    private static Database.PreparedInsertStatement PrepareTwoColumns()
        => new("t", ["id", "name"], [DataType.Integer, DataType.String]);

    /// <summary>
    /// Parses the same clause with both parsers, asserts they agree, and returns the array's values.
    /// </summary>
    private static object[] ReadBoth(Database.PreparedInsertStatement prepared, string clause)
    {
        var row = prepared.ParseValues(clause);
        var values = prepared.ParseValuesToArray(clause);

        Assert.Equal(prepared.Columns.Count, values.Length);
        for (int i = 0; i < prepared.Columns.Count; i++)
        {
            Assert.Equal(row[prepared.Columns[i]], values[i]);
        }

        return values;
    }

    // Whitespace is trimmed around a value, never inside its quotes: '  padded  ' is a value of four leading and
    // four trailing spaces, exactly as SQL reads it.
    [Theory]
    [InlineData("7, 'a,b'", 7, "a,b")]
    [InlineData("8, 'it''s, ok'", 8, "it's, ok")]
    [InlineData("9, 'C:\\tmp\\'", 9, "C:\\tmp\\")]
    [InlineData("10, '[0.5, 0.25]'", 10, "[0.5, 0.25]")]
    [InlineData("11, (1,2)", 11, "(1,2)")]
    [InlineData("12,   '  padded  '  ", 12, "  padded  ")]
    public void BothParsers_SplitValuesIdentically(string clause, int expectedId, string expectedName)
    {
        var prepared = PrepareTwoColumns();

        var values = ReadBoth(prepared, clause);

        Assert.Equal(expectedId, Convert.ToInt32(values[0], CultureInfo.InvariantCulture));
        Assert.Equal(expectedName, values[1]);
    }

    [Fact]
    public void BothParsers_KeepAnUnquotedNumberAsItself()
    {
        var prepared = PrepareTwoColumns();

        var values = ReadBoth(prepared, " 42 , NULL ");

        Assert.Equal(42, Convert.ToInt32(values[0], CultureInfo.InvariantCulture));
        Assert.Equal(DBNull.Value, values[1]);
    }

    [Theory]
    [InlineData("'only a name'")]
    [InlineData("1, 'a', 3")]
    [InlineData("1,")]
    [InlineData("")]
    public void BothParsers_RejectAClauseThatDoesNotFillTheColumns(string clause)
    {
        var prepared = PrepareTwoColumns();

        Assert.Throws<InvalidOperationException>(() => prepared.ParseValues(clause));
        Assert.Throws<InvalidOperationException>(() => prepared.ParseValuesToArray(clause));
    }
}
