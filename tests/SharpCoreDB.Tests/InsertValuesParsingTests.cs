// <copyright file="InsertValuesParsingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Pins the observable behaviour of the multi-row <c>INSERT … VALUES</c> literal scanner
/// (<c>SqlParser.ParseInsertValues</c>).
/// <para>
/// The scanner is structural: it splits a tuple into element texts and hands each one, quotes and all, to
/// <c>SqlParser.ParseValue</c>. The rules below are what the suites pin:
/// </para>
/// <list type="number">
/// <item>a single quote opens and closes a literal, and <c>''</c> inside one is <b>one quote of data</b> that does
/// not close it — SQL and SQLite (<c>'it''s'</c> → <c>it's</c>). A backslash is data too: it is a legal last
/// character of a value, so it can never escape the closing quote. (Before known issue 7 was fixed the scanner
/// dropped the quote of a doubled pair and let a backslash suppress a toggle, which is why a bound value
/// containing either could not round-trip.)</item>
/// <item>a comma splits literals only <b>outside</b> quotes and outside a bracketed literal.</item>
/// <item>every literal is <c>Trim()</c>ed.</item>
/// <item>a literal that is present is a value even when it is empty, so <c>(1, '')</c> stores an empty string, as
/// SQLite does; only a literally absent element is dropped, so <c>(1,)</c> leaves the column to the
/// missing-value path and therefore <c>NULL</c>, while <c>(1, )</c> stores an empty string. A missing literal and
/// an empty-string literal are different values.</item>
/// </list>
/// </summary>
public sealed class InsertValuesParsingTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public InsertValuesParsingTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_InsertValues_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private IDatabase Open(string name)
    {
        var dir = Path.Combine(_dirPath, name);
        Directory.CreateDirectory(dir);
        var db = _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        return db;
    }

    private static object? NameOf(IDatabase db, int id)
    {
        // SELECT * rather than the single column: the engine returns rows in table column order regardless of
        // the projection, so a projected SELECT still yields (id, name). The existing suites index the value the
        // same way — Values.Last() with a full-row select.
        var rows = db.ExecuteQuery($"SELECT * FROM t WHERE id = {id}");
        return rows.Count == 0 ? null : rows[0].Values.Last();
    }

    // ── Rule 2: a comma inside quotes does not split literals ───────────────────────────────────

    [Fact]
    public async Task QuotedComma_IsOneLiteral_NotATupleBreak()
    {
        await using var db = Open("quoted_comma");
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'a,b'), (2, 'c')");

        Assert.Equal("a,b", NameOf(db, 1));
        Assert.Equal("c", NameOf(db, 2));
    }

    // ── Rule 1: a doubled quote is one quote; a backslash is data ───────────────────────────────

    [Fact]
    public async Task DoubledQuote_IsOneQuote_NotAnEscape()
    {
        await using var db = Open("doubled_quote");

        // 'it''s' → the doubled quote is data, so the literal is it's (SQL and SQLite behaviour).
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'it''s'), (2, 'ok')");

        Assert.Equal("it's", NameOf(db, 1));
    }

    [Fact]
    public async Task TrailingBackslash_DoesNotSwallowTheClosingQuote()
    {
        await using var db = Open("escaped_quote");

        // 'C:\temp\' → the backslash is data, so the final quote still closes the literal. A scanner that treats
        // a backslash as an escape cannot represent this value at all — it read the closing quote as escaped and
        // reported "Mismatched parentheses in VALUES clause".
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'C:\\temp\\'), (2, 'ok')");

        Assert.Equal("C:\\temp\\", NameOf(db, 1));
    }

    // ── Rule 3: literals are trimmed ────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhitespaceAroundLiterals_IsTrimmed()
    {
        await using var db = Open("trimmed");
        db.ExecuteSQL("INSERT INTO t VALUES (  1  ,   'x'   ), ( 2 , 'y' )");

        Assert.Equal("x", NameOf(db, 1));
        Assert.Equal("y", NameOf(db, 2));
    }

    // ── Rule 4: a missing literal is not emitted; an empty-string literal is ────────────────────

    [Fact]
    public async Task MissingTrailingLiteral_IsNotEmitted_SoTheColumnIsNull()
    {
        await using var db = Open("missing_literal");
        db.ExecuteSQL("INSERT INTO t VALUES (1,), (2, 'ok')");

        var value = NameOf(db, 1);
        Assert.True(value is null or DBNull,
            $"a literal with no text must not be emitted, so the column is NULL; got '{value}'");
    }

    [Fact]
    public async Task TrailingCommaFollowedBySpace_EmitsAnEmptyString_NotNull()
    {
        await using var db = Open("empty_literal");

        // A space between the comma and the close paren is text, so the literal IS emitted — and trims to "".
        db.ExecuteSQL("INSERT INTO t VALUES (1, ), (2, 'ok')");

        Assert.Equal(string.Empty, NameOf(db, 1));
    }

    [Fact]
    public async Task EmptyStringLiteral_InAnInteriorPosition_IsEmitted_AndStaysEmpty()
    {
        await using var db = Open("interior_empty");
        db.ExecuteSQL("CREATE TABLE t3 (id INTEGER PRIMARY KEY, name TEXT, note INTEGER)");

        db.ExecuteSQL("INSERT INTO t3 VALUES (1, '', 42), (2, 'x', 43)");

        var rows = db.ExecuteQuery("SELECT * FROM t3 WHERE id = 1");
        Assert.Equal(string.Empty, rows[0]["name"]);
        Assert.Equal(42, Convert.ToInt32(rows[0]["note"]));
    }

    [Fact]
    public async Task EmptyStringLiteral_InTheTrailingPosition_IsStoredAsAnEmptyString()
    {
        await using var db = Open("trailing_empty");

        // SQLite stores '' here, so SharpCoreDB must too. The old rule suppressed a trailing empty literal, which
        // meant a *present* empty value became NULL — and, through a bound parameter, an empty string was not
        // round-trippable at all (known issue 7).
        db.ExecuteSQL("INSERT INTO t VALUES (1, ''), (2, 'ok')");

        Assert.Equal(string.Empty, NameOf(db, 1));
    }

    // ── Control: the single-tuple shape still parses ────────────────────────────────────────────

    [Fact]
    public async Task SingleTuple_StillParses_AndValuesReachTheirColumns()
    {
        await using var db = Open("single_tuple");
        db.ExecuteSQL("INSERT INTO t VALUES (7, 'only')");

        Assert.Equal("only", NameOf(db, 7));
    }
}
