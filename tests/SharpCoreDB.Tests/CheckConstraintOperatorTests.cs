// <copyright file="CheckConstraintOperatorTests.cs" company="MPCoreDeveloper">
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
/// CHECK constraints written with IN, NOT IN, BETWEEN, NOT BETWEEN and GLOB, evaluated as SQLite does.
/// <para>
/// Up to 2.1.4 those operators were left to an older evaluator that accepted every row: <c>CHECK (status IN ('a', 'b'))</c>
/// let <c>'z'</c> in, and <c>CHECK (n BETWEEN 1 AND 10)</c> let <c>50</c> in. They are evaluated with SQL's three-valued
/// logic now, so a NULL still passes. The same operators over a computed value in a WHERE clause
/// (<c>length(name) BETWEEN 3 AND 6</c>, <c>upper(name) IN (…)</c>) use the same evaluator.
/// </para>
/// </summary>
public sealed class CheckConstraintOperatorTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_CheckOps_{Guid.NewGuid():N}");

    public CheckConstraintOperatorTests()
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
        var path = Path.Combine(_root, $"db_{Guid.NewGuid():N}");
        var db = _factory.Create(path, "pw");
        db.ExecuteSQL(mode == "page-based" ? ddl + " STORAGE = PAGE_BASED" : ddl);
        return (db, path);
    }

    private static string[] Rows(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => string.Join(",", r.Values.Select(v => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_ValueOutsideACheckInList_IsRejected_AndNullPasses(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, status TEXT CHECK (status IN ('a', 'b')))");

        db.ExecuteSQL("INSERT INTO t (id, status) VALUES (1, 'a')");
        db.ExecuteSQL("INSERT INTO t (id, status) VALUES (2, NULL)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, status) VALUES (3, 'z')"));

        Assert.Equal(["1,a", "2,NULL"], Rows(db, "SELECT id, status FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_ValueOutsideACheckBetween_IsRejected(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER CHECK (n BETWEEN 1 AND 10))");

        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 5)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, n) VALUES (2, 50)"));

        Assert.Equal(["1,5"], Rows(db, "SELECT id, n FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_ValueInANegatedCheck_IsRejected(string mode)
    {
        var (db, _) = Open(mode,
            "CREATE TABLE t (id INTEGER PRIMARY KEY, s TEXT CHECK (s NOT IN ('x')), n INTEGER CHECK (n NOT BETWEEN 5 AND 6))");

        db.ExecuteSQL("INSERT INTO t (id, s, n) VALUES (1, 'a', 1)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, s, n) VALUES (2, 'x', 1)"));
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, s, n) VALUES (3, 'a', 5)"));

        Assert.Equal(["1,a,1"], Rows(db, "SELECT id, s, n FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_ValueNotMatchingACheckGlob_IsRejected_CaseSensitively(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, code TEXT CHECK (code GLOB '[a-z][0-9]*'))");

        db.ExecuteSQL("INSERT INTO t (id, code) VALUES (1, 'a1')");
        db.ExecuteSQL("INSERT INTO t (id, code) VALUES (2, 'b22x')");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, code) VALUES (3, 'A1')"));
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, code) VALUES (4, 'ab')"));

        Assert.Equal(["1,a1", "2,b22x"], Rows(db, "SELECT id, code FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_ToAValueOutsideACheckInList_IsRejectedAndChangesNothing(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, status TEXT CHECK (status IN ('a', 'b')))");
        db.ExecuteSQL("INSERT INTO t (id, status) VALUES (1, 'a')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET status = 'z' WHERE id = 1"));

        Assert.Equal(["1,a"], Rows(db, "SELECT id, status FROM t"));
    }

    [Fact]
    public void Where_BetweenAndInOverAComputedValue_FilterRows()
    {
        var (db, _) = Open("columnar", "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'ab'), (2, 'abcd'), (3, 'abcdef'), (4, 'abcdefgh')");

        Assert.Equal(["2", "3"], Rows(db, "SELECT id FROM t WHERE length(name) BETWEEN 3 AND 6 AND id > 0").Order());
        Assert.Equal(["1", "3"], Rows(db, "SELECT id FROM t WHERE upper(name) IN ('AB', 'ABCDEF')").Order());
    }
}
