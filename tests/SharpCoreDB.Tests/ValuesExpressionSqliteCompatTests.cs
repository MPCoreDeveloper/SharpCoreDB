// <copyright file="ValuesExpressionSqliteCompatTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Option 2 of issue #525: every value in an UPDATE SET list or an INSERT … VALUES list that parses as an expression
/// is evaluated as SQLite evaluates it — constant arithmetic, a function of literals, a comparison — while a bare word
/// that names no column keeps the lenient literal reading, which is where SQLite itself would raise "no such column".
/// <para>
/// One deliberate exception is kept (and was offered in the issue): an unquoted date in a date/time column stays a
/// date. SharpCoreDB is typed, so a <c>DATETIME</c> column cannot hold the integer <c>2008</c> that SQLite's
/// <c>2024-01-15</c> arithmetic would store. Each case below is a row of the issue's measured table.
/// </para>
/// </summary>
public sealed class ValuesExpressionSqliteCompatTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_ValuesExpr_{Guid.NewGuid():N}");

    public ValuesExpressionSqliteCompatTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_root);
    }

    public static TheoryData<string> Modes => new() { "columnar", "page-based" };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp directory.
        }
    }

    private (IDatabase Db, string Path) Open(string mode, string ddl)
    {
        var path = Path.Combine(_root, mode == "single-file" ? $"db_{Guid.NewGuid():N}.scdb" : $"db_{Guid.NewGuid():N}");
        var db = Reopen(mode, path);
        db.ExecuteSQL(mode == "page-based" ? ddl + " STORAGE = PAGE_BASED" : ddl);
        return (db, path);
    }

    private IDatabase Reopen(string mode, string path) => mode == "single-file"
        ? _factory.CreateWithOptions(path, "pw", DatabaseOptions.CreateSingleFileDefault())
        : _factory.Create(path, "pw");

    private static string[] Rows(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => string.Join(",", r.Values.Select(v => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    // ── UPDATE … SET (issue #525 table, rows 1–4) ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_TextColumn_ConstantArithmetic_IsEvaluated(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'x')");

        db.ExecuteSQL("UPDATE t SET name = 2 * 3 WHERE id = 1");

        Assert.Equal(["1,6"], Rows(db, "SELECT id, name FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_TextColumn_UnquotedWord_RemainsALenientLiteral(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, status TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, status) VALUES (1, 'x')");

        db.ExecuteSQL("UPDATE t SET status = active WHERE id = 1");

        Assert.Equal(["1,active"], Rows(db, "SELECT id, status FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_DateTimeColumn_UnquotedDate_StaysADate(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, d DATETIME)");
        db.ExecuteSQL("INSERT INTO t (id, d) VALUES (1, '2026-01-01')");

        db.ExecuteSQL("UPDATE t SET d = 2024-01-15 WHERE id = 1");

        var row = Rows(db, "SELECT d FROM t")[0];
        Assert.Contains("2024", row, StringComparison.Ordinal);
        Assert.DoesNotContain("2008", row, StringComparison.Ordinal);
    }

    // ── INSERT … VALUES (issue #525 table, rows 5–7) ────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_TextColumn_ConstantArithmetic_IsEvaluated(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, 2 * 3)");

        Assert.Equal(["1,6"], Rows(db, "SELECT id, name FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_IntegerColumn_ConstantArithmetic_IsEvaluated(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, 2 * 3)");

        Assert.Equal(["1,6"], Rows(db, "SELECT id, n FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_TextColumn_FunctionOfLiteral_IsEvaluated(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, upper('abc'))");

        Assert.Equal(["1,ABC"], Rows(db, "SELECT id, name FROM t"));
    }

    // ── Exceptions and literal-safety ──────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_DateTimeColumn_UnquotedDate_StaysADate(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, d DATETIME)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, 2024-01-15)");

        var row = Rows(db, "SELECT d FROM t")[0];
        Assert.Contains("2024", row, StringComparison.Ordinal);
        Assert.DoesNotContain("2008", row, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_TextColumn_UnquotedWord_RemainsALenientLiteral(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, status TEXT)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, active)");

        Assert.Equal(["1,active"], Rows(db, "SELECT id, status FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_QuotedArithmeticText_IsStoredLiterally(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, label TEXT)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, '2 * 3')");

        Assert.Equal(["1,2 * 3"], Rows(db, "SELECT id, label FROM t"));
    }

    [Fact]
    public void Insert_TextColumn_ConstantArithmetic_IsEvaluatedOnSingleFile()
    {
        var (db, _) = Open("single-file", "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, 2 * 3)");

        Assert.Equal(["1,6"], Rows(db, "SELECT id, name FROM t"));
    }

    /// <summary>
    /// The <c>ExecuteBatchSQL</c> INSERT fast path reads values through a second parser
    /// (<c>PreparedInsertStatement.ParseValuesToArray</c>); it must evaluate expressions exactly as the single-statement
    /// path does, or a batched insert and a plain one would disagree about the stored value.
    /// </summary>
    [Fact]
    public void BatchInsert_ConstantArithmetic_MatchesSingleStatementInsert()
    {
        var (db, _) = Open("columnar", "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");

        db.ExecuteBatchSQL(["INSERT INTO t (id, n) VALUES (1, 2 * 3)", "INSERT INTO t (id, n) VALUES (2, 4 + 1)"]);

        Assert.Equal(["1,6", "2,5"], Rows(db, "SELECT id, n FROM t").Order());
    }
}
