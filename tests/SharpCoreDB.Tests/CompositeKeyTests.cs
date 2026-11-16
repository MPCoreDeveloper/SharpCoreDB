// <copyright file="CompositeKeyTests.cs" company="MPCoreDeveloper">
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
/// A composite PRIMARY KEY and a multi-column UNIQUE constraint on directory storage (columnar and page-based).
/// <para>
/// Up to 2.1.4 a table-level <c>PRIMARY KEY (a, b)</c> was read as a column called PRIMARY (with
/// <c>CONSTRAINT pk PRIMARY KEY (a, b)</c>, a column called CONSTRAINT) that became the key — always NULL, so the table
/// held one row. <c>PRIMARY KEY</c> written on two columns, as EF Core's <c>EnsureCreated</c> declares a composite key,
/// made the last column the whole key. And a multi-column <c>UNIQUE (a, b)</c> was never checked. A composite key is now
/// enforced as SQLite enforces it in a rowid table: a UNIQUE constraint over all its columns, the row id staying the
/// storage key.
/// </para>
/// </summary>
public sealed class CompositeKeyTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_CompositeKey_{Guid.NewGuid():N}");

    public CompositeKeyTests()
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
    public void CreateTable_TableLevelCompositePrimaryKey_AcceptsDistinctPairsAndRejectsADuplicate(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (a INTEGER NOT NULL, b INTEGER NOT NULL, v TEXT, PRIMARY KEY (a, b))");
        db.ExecuteSQL("INSERT INTO t (a, b, v) VALUES (1, 1, 'x')");
        db.ExecuteSQL("INSERT INTO t (a, b, v) VALUES (2, 1, 'y')");
        db.ExecuteSQL("INSERT INTO t (a, b, v) VALUES (1, 2, 'z')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (a, b, v) VALUES (1, 1, 'dup')"));
        Assert.Equal(["1,1,x", "1,2,z", "2,1,y"], Rows(db, "SELECT a, b, v FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CreateTable_NamedCompositePrimaryKeyConstraint_DeclaresNoPhantomColumn(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (a INTEGER NOT NULL, b INTEGER NOT NULL, CONSTRAINT pk_t PRIMARY KEY (a, b))");
        db.TryGetTable("t", out var table);

        Assert.DoesNotContain(table.Columns, c => c.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                                                  c.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase));
        db.ExecuteSQL("INSERT INTO t (a, b) VALUES (1, 1)");
        db.ExecuteSQL("INSERT INTO t (a, b) VALUES (2, 1)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (a, b) VALUES (1, 1)"));
        Assert.Equal(2, db.ExecuteQuery("SELECT a FROM t").Count);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CreateTable_PrimaryKeyWrittenOnTwoColumns_IsOneCompositeKey(string mode)
    {
        // The shape EF Core's EnsureCreated writes for HasKey(x => new { x.A, x.B }).
        var (db, _) = Open(mode, "CREATE TABLE t (a INTEGER NOT NULL PRIMARY KEY, b INTEGER NOT NULL PRIMARY KEY, v TEXT)");
        db.ExecuteSQL("INSERT INTO t (a, b, v) VALUES (1, 1, 'x')");
        db.ExecuteSQL("INSERT INTO t (a, b, v) VALUES (2, 1, 'y')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (a, b, v) VALUES (1, 1, 'dup')"));
        Assert.Equal(2, db.ExecuteQuery("SELECT a FROM t").Count);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CreateTable_TableLevelSingleColumnPrimaryKey_MakesThatColumnTheKey(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER NOT NULL, v TEXT, PRIMARY KEY (id))");
        db.TryGetTable("t", out var table);

        Assert.Equal("id", table.Columns[table.PrimaryKeyIndex]);
        db.ExecuteSQL("INSERT INTO t (id, v) VALUES (1, 'x')");
        db.ExecuteSQL("INSERT INTO t (id, v) VALUES (2, 'y')");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, v) VALUES (1, 'dup')"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_DuplicateOfCompositeUnique_IsRejected_AndNullsNeverConflict(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, a INTEGER, b INTEGER, UNIQUE (a, b))");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (1, 1, 1)");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (2, 1, 2)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (3, 1, 1)"));
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (4, 1, NULL)");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (5, 1, NULL)");
        Assert.Equal(["1", "2", "4", "5"], Rows(db, "SELECT id FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void MultiRowInsert_DuplicateOfCompositeUniqueWithinTheStatement_KeepsNoRow(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, a INTEGER, b INTEGER, UNIQUE (a, b))");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (1, 1, 1), (2, 2, 2), (3, 1, 1)"));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_OntoAnotherRowsCompositeKey_IsRejectedAndChangesNothing(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, a INTEGER, b INTEGER, UNIQUE (a, b))");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (1, 1, 1)");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (2, 1, 2)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET b = 1 WHERE id = 2"));
        Assert.Equal(["1,1,1", "2,1,2"], Rows(db, "SELECT id, a, b FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_MovingARowOffItsCompositeKey_ReleasesTheKey(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, a INTEGER, b INTEGER, UNIQUE (a, b))");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (1, 1, 1)");

        db.ExecuteSQL("UPDATE t SET b = 9 WHERE id = 1");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (2, 1, 1)");

        Assert.Equal(["1,1,9", "2,1,1"], Rows(db, "SELECT id, a, b FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_DuplicateOfCompositeKey_AfterReopen_IsRejected(string mode)
    {
        var (db, path) = Open(mode, "CREATE TABLE t (a INTEGER NOT NULL, b INTEGER NOT NULL, PRIMARY KEY (a, b))");
        db.ExecuteSQL("INSERT INTO t (a, b) VALUES (1, 1)");
        (db as IDisposable)?.Dispose();

        var reopened = Reopen(mode, path);
        reopened.ExecuteSQL("INSERT INTO t (a, b) VALUES (1, 2)");
        Assert.ThrowsAny<Exception>(() => reopened.ExecuteSQL("INSERT INTO t (a, b) VALUES (1, 1)"));
        Assert.Equal(2, reopened.ExecuteQuery("SELECT a FROM t").Count);
        (reopened as IDisposable)?.Dispose();
    }
}
