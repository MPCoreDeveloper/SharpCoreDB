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
/// (<c>SqlParser.ParseInsertValues</c>, <c>SqlParser.DML.cs:654</c>) <b>before</b> it is rewritten to slice
/// literals out of the statement text instead of appending them to a <c>StringBuilder</c> one character at a
/// time (plan §9 priority 2, lever 1 — the measured 25 % of a multi-row pass).
/// <para>
/// The rewrite is only safe if these four rules are reproduced exactly, and three of them are surprising enough
/// to be documented here rather than in prose:
/// <list type="number">
/// <item>a single quote toggles quote state and is <b>dropped</b> — unless the previously <i>appended</i>
/// character was a backslash, a check against the scanner's accumulated content rather than the source text.
/// So <c>'it''s'</c> becomes <c>its</c> (standard SQL quote-doubling is <b>not</b> an escape here) while
/// <c>'a\'b'</c> keeps its backslash and its content.</item>
/// <item>a comma splits literals only <b>outside</b> quotes.</item>
/// <item>every literal is <c>Trim()</c>ed.</item>
/// <item>a literal with <b>no text at all is not emitted</b>, so <c>(1,)</c> yields a single value — leaving the
/// second column to the missing-value path and therefore <c>NULL</c> — while <c>(1, )</c> yields an empty
/// literal, which for a TEXT column stores an empty string. A missing literal and an empty-string literal are
/// different values.</item>
/// </list>
/// </para>
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

    // ── Rule 1: quote toggling, and the escape check against accumulated content ────────────────

    [Fact]
    public async Task DoubledQuote_IsDroppedAndContentConcatenated_NotEscaped()
    {
        await using var db = Open("doubled_quote");

        // 'it''s' → quote(dropped) i t quote(dropped) quote(dropped) s quote(dropped) → "its"
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'it''s'), (2, 'ok')");

        Assert.Equal("its", NameOf(db, 1));
    }

    [Fact]
    public async Task BackslashEscapedQuote_IsKeptVerbatim_AndDoesNotToggle()
    {
        await using var db = Open("escaped_quote");

        // 'a\'b' → quote(dropped) a \ ' (the accumulated content ends in '\', so this is NOT a toggle — the
        // quote is appended and stays) b quote(dropped) → "a\'b". The backslash is data, not an escape: it is
        // stored verbatim, and so is the quote it "escaped".
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'a\\'b'), (2, 'ok')");

        Assert.Equal("a\\'b", NameOf(db, 1));
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

        // Found by the suites when the scanner was rewritten (2026-09-16): a literal that is followed by a comma
        // is emitted unconditionally, so '' stores an empty string — only a *trailing* literal is suppressed when
        // it has no text. Inside `(1, '')` the empty literal IS the trailing one, so it is not emitted at all and
        // the column falls to NULL; expressing the interior case needs a value after it.
        db.ExecuteSQL("INSERT INTO t3 VALUES (1, '', 42), (2, 'x', 43)");

        var rows = db.ExecuteQuery("SELECT * FROM t3 WHERE id = 1");
        Assert.Equal(string.Empty, rows[0]["name"]);
        Assert.Equal(42, Convert.ToInt32(rows[0]["note"]));
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
