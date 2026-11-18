// <copyright file="TableCheckConstraintTests.cs" company="MPCoreDeveloper">
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
/// CHECK constraints evaluated as SQLite does.
/// <para>
/// Up to 2.1.4 a table-level CHECK was stored with its keyword (<c>CHECK (n &lt;&gt; id)</c>), failed to evaluate and
/// so rejected every row — a table with a table-level CHECK accepted no INSERT. <c>&lt;&gt;</c> was not an operator, and
/// a NULL operand failed the check, where SQLite passes a CHECK whose result is NULL.
/// </para>
/// </summary>
public sealed class TableCheckConstraintTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_Check_{Guid.NewGuid():N}");

    public TableCheckConstraintTests()
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

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_SatisfyingATableLevelCheck_IsAccepted_AndViolatingItIsRejected(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, lo INTEGER, hi INTEGER, CHECK (lo <= hi))");

        db.ExecuteSQL("INSERT INTO t (id, lo, hi) VALUES (1, 1, 2)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, lo, hi) VALUES (2, 3, 2)"));

        Assert.Equal(["1,1,2"], Rows(db, "SELECT id, lo, hi FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_NamedTableCheckWithNotEqualOperator_IsEnforced(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, CONSTRAINT ck_t CHECK (n <> id))");

        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 0)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, n) VALUES (2, 2)"));

        Assert.Equal(["1,0"], Rows(db, "SELECT id, n FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_NullIntoACheckedColumn_PassesTheCheck(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, price REAL CHECK (price > 0))");

        db.ExecuteSQL("INSERT INTO t (id, price) VALUES (1, NULL)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, price) VALUES (2, -1)"));

        Assert.Equal(["1,NULL"], Rows(db, "SELECT id, price FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_CheckCallingAFunction_IsEnforced(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, code TEXT CHECK (length(code) = 3))");

        db.ExecuteSQL("INSERT INTO t (id, code) VALUES (1, 'abc')");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, code) VALUES (2, 'abcd')"));

        Assert.Equal(["1,abc"], Rows(db, "SELECT id, code FROM t"));
    }
}
