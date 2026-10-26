// <copyright file="ParameterLiteralRoundTripTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// Known issue 7: a bound string parameter is rendered back into the statement text
/// (<c>ParameterBinder.FormatParameter</c> emits <c>'it''s'</c> for <c>it's</c>) and then read back by the
/// SQL literal scanner, so formatter and scanner have to agree about escaping.
/// <para>
/// They disagreed. The formatter doubled the single quote — the SQL and SQLite convention — while the VALUES
/// scanner treated a backslash as an escape and did not implement doubling. A parameter holding
/// <c>it's fine, really</c> therefore closed its literal at the doubled quote, split at the following comma and
/// pushed <c>really</c> into the next column; a value ending in a backslash or holding a backslash before a
/// quote threw <c>Mismatched parentheses in VALUES clause</c>.
/// </para>
/// <para>
/// These tests are the property that was missing: <b>any</b> string that goes in through a parameter comes back
/// byte-exact in its own column, and the neighbouring columns are untouched. They drive the public API only, so
/// they cover the whole path — binder, VALUES splitter, literal scanner and value conversion.
/// </para>
/// </summary>
public sealed class ParameterLiteralRoundTripTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private int _dbCounter;

    public ParameterLiteralRoundTripTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_ParameterRoundTrip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private IDatabase Open()
    {
        var dir = Path.Combine(_dirPath, $"db{_dbCounter++}");
        Directory.CreateDirectory(dir);
        var db = _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, head TEXT, val TEXT, tail TEXT)");
        return db;
    }

    private static Dictionary<string, object?> P(params (string Name, object? Value)[] pairs)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in pairs)
        {
            parameters[name] = value;
        }

        return parameters;
    }

    private static Dictionary<string, object> Read(IDatabase db, int id)
    {
        var rows = db.ExecuteQuery($"SELECT * FROM t WHERE id = {id}");
        Assert.Single(rows);
        return rows[0];
    }

    /// <summary>
    /// The strings that broke the old scanner: an apostrophe (doubled by the formatter), a backslash before a
    /// quote, a trailing backslash, commas and brackets inside the value, plus plain text that must survive
    /// unchanged. <c>NULL</c> as text and the empty string are included because both are values, not absences.
    /// </summary>
    public static TheoryData<string> TextValues => new()
    {
        "it's fine, really",
        "a stray \\' apostrophe, then a comma",
        "ends with a backslash \\",
        "the mask (see below, the reader) wins, and then it records iterations)",
        "quoted at both ends: 'x'",
        "doubled '' quote",
        "brackets [0.5, 0.25] and braces {x}",
        "line1\nline2\ttabbed",
        "unicode: ünïcødé ✓ 日本語",
        "NULL",
        "null",
        "; semicolon -- dash",
        "key=value&other=2",
        "",
        "'",
        "''",
        "\\",
    };

    // ── The defect: a text parameter in the middle of a VALUES tuple ────────────────────────────

    [Theory]
    [MemberData(nameof(TextValues))]
    public async Task ExecuteSQL_TextParameterBetweenLiterals_RoundTripsByteExact(string value)
    {
        await using var db = Open();

        // head and tail are literals, so a value split at its own comma can only be detected as corruption of
        // the neighbours — which is exactly how SharpAgent saw it (a value landing in `embedding`).
        db.ExecuteSQL(
            "INSERT INTO t VALUES (@id, 'head', @val, 'tail')",
            P(("id", 1), ("val", value)));

        var row = Read(db, 1);
        Assert.Equal("head", row["head"] as string);
        Assert.Equal(value, row["val"] as string);
        Assert.Equal("tail", row["tail"] as string);
    }

    [Theory]
    [MemberData(nameof(TextValues))]
    public async Task ExecuteSQL_TextParameterAsLastLiteral_RoundTripsByteExact(string value)
    {
        await using var db = Open();

        db.ExecuteSQL(
            "INSERT INTO t VALUES (@id, 'head', 'val', @val)",
            P(("id", 1), ("val", value)));

        var row = Read(db, 1);
        Assert.Equal("head", row["head"] as string);
        Assert.Equal("val", row["val"] as string);
        Assert.Equal(value, row["tail"] as string);
    }

    [Fact]
    public async Task ExecuteSQL_MultiRowInsertWithTextParameters_RoundTripsEveryRow()
    {
        await using var db = Open();

        db.ExecuteSQL(
            "INSERT INTO t VALUES (@a, 'head', @av, 'tail'), (@b, 'head', @bv, 'tail')",
            P(("a", 1), ("av", "it's the first, really"),
              ("b", 2), ("bv", "and the second ends with a backslash \\")));

        Assert.Equal("it's the first, really", Read(db, 1)["val"] as string);
        Assert.Equal("and the second ends with a backslash \\", Read(db, 2)["val"] as string);
    }

    [Fact]
    public async Task ExecuteSQL_PositionalTextParameter_RoundTripsByteExact()
    {
        await using var db = Open();

        db.ExecuteSQL(
            "INSERT INTO t VALUES (?, 'head', ?, 'tail')",
            P(("0", 1), ("1", "it's positional, and holds a comma")));

        Assert.Equal("it's positional, and holds a comma", Read(db, 1)["val"] as string);
    }

    // ── The same values through UPDATE (the SET splitter has its own scanner) ───────────────────

    [Theory]
    [MemberData(nameof(TextValues))]
    public async Task ExecuteSQL_TextParameter_CanBeFoundByEqualToTheSameValue(string value)
    {
        await using var db = Open();

        db.ExecuteSQL("INSERT INTO t VALUES (@id, 'head', @val, 'tail')", P(("id", 1), ("val", value)));

        var rows = db.ExecuteQuery("SELECT * FROM t WHERE val = @val", P(("val", value)));

        Assert.Single(rows);
        Assert.Equal(value, rows[0]["val"] as string);
    }

    [Theory]
    [MemberData(nameof(TextValues))]
    public async Task ExecuteSQL_TextParameterInUpdateSet_RoundTripsByteExact(string value)
    {
        await using var db = Open();

        db.ExecuteSQL("INSERT INTO t VALUES (1, 'head', 'original', 'tail')");
        db.ExecuteSQL(
            "UPDATE t SET val = @val WHERE id = @id",
            P(("val", value), ("id", 1)));

        var row = Read(db, 1);
        Assert.Equal(value, row["val"] as string);
        Assert.Equal("head", row["head"] as string);
        Assert.Equal("tail", row["tail"] as string);
    }

    // ── Literal SQL (not parameters) follows the same convention ────────────────────────────────

    [Fact]
    public async Task ExecuteSQL_LiteralWithDoubledQuote_StoresOneQuote()
    {
        await using var db = Open();

        // SQL/SQLite: '' inside a literal is one quote and does not close it.
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'head', 'it''s', 'tail')");

        Assert.Equal("it's", Read(db, 1)["val"] as string);
    }

    [Fact]
    public async Task ExecuteSQL_LiteralEndingInBackslash_StoresTheBackslash()
    {
        await using var db = Open();

        // SQLite: a backslash is data, so the closing quote still closes the literal.
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'head', 'C:\\temp\\', 'tail')");

        Assert.Equal("C:\\temp\\", Read(db, 1)["val"] as string);
        Assert.Equal("tail", Read(db, 1)["tail"] as string);
    }

    [Fact]
    public async Task ExecuteSQL_LiteralWithCommaAndDoubledQuote_KeepsTheTupleInOnePiece()
    {
        await using var db = Open();

        db.ExecuteSQL("INSERT INTO t VALUES (1, 'head', 'it''s fine, really', 'tail')");

        var row = Read(db, 1);
        Assert.Equal("it's fine, really", row["val"] as string);
        Assert.Equal("tail", row["tail"] as string);
    }

    // ── Safety net: a tuple with too many values fails loudly instead of shifting columns ───────

    [Fact]
    public async Task ExecuteSQL_InsertTupleWithMoreValuesThanColumns_ThrowsNamingTheTable()
    {
        await using var db = Open();

        var ex = Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("INSERT INTO t VALUES (1, 'a', 'b', 'c', 'too many')"));

        Assert.Contains("'t'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("5", ex.Message, StringComparison.Ordinal);
        Assert.Contains("4", ex.Message, StringComparison.Ordinal);
    }
}
