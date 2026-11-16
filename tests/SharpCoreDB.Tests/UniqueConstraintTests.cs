// <copyright file="UniqueConstraintTests.cs" company="MPCoreDeveloper">
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
/// A single-column UNIQUE constraint — column-level <c>email TEXT UNIQUE</c> or table-level <c>UNIQUE (email)</c> —
/// is enforced on INSERT, multi-row INSERT and UPDATE, on columnar and page-based storage, and after a reopen; NULLs
/// never conflict, as in SQLite. Single-file storage is not covered yet (the skipped test).
/// <para>
/// Up to 2.1.4 the constraint was parsed and stored but never checked (known issue §9): the INSERT-time validation
/// was a stub, a UNIQUE <em>index</em> was checked only on a single-row INSERT into a columnar table, and a multi-row
/// INSERT or an UPDATE was never checked at all. A UNIQUE column now gets an automatic unique index, as in SQLite
/// (<c>sqlite_autoindex_…</c>), and one validator covers every write path.
/// </para>
/// </summary>
public sealed class UniqueConstraintTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_Unique_{Guid.NewGuid():N}");

    public UniqueConstraintTests()
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

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_DuplicateIntoUniqueColumn_IsRejected(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'a@x')"));
        Assert.Equal([1], Ids(db));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_NullsIntoUniqueColumn_AreAllowed(string mode)
    {
        var (db, _) = Open(mode);

        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, NULL)");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, NULL)");

        Assert.Equal([1, 2], Ids(db));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void MultiRowInsert_DuplicateWithinTheStatement_KeepsNoRow(string mode)
    {
        var (db, _) = Open(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x'), (2, 'b@x'), (3, 'a@x')"));
        Assert.Empty(Ids(db));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void MultiRowInsert_DuplicatingAStoredValue_KeepsNoRowOfTheStatement(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'b@x'), (3, 'a@x')"));
        Assert.Equal([1], Ids(db));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_UniqueColumnToAnotherRowsValue_IsRejectedAndChangesNothing(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'b@x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET email = 'a@x' WHERE id = 2"));
        Assert.Equal("b@x", Email(db, 2));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_UniqueColumnToItsOwnValue_IsAccepted(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");

        db.ExecuteSQL("UPDATE t SET email = 'a@x' WHERE id = 1");

        Assert.Equal("a@x", Email(db, 1));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_UniqueColumnToAFreeValue_IsAccepted(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'b@x')");

        db.ExecuteSQL("UPDATE t SET email = 'c@x' WHERE id = 2");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (3, 'b@x')");

        Assert.Equal("c@x", Email(db, 2));
        Assert.Equal([1, 2, 3], Ids(db));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_UniqueColumnOnSeveralRowsToOneValue_IsRejectedAndChangesNothing(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'b@x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET email = 'z@x' WHERE id > 0"));
        Assert.Equal("a@x", Email(db, 1));
        Assert.Equal("b@x", Email(db, 2));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_DuplicateIntoUniqueColumn_AfterReopen_IsRejected(string mode)
    {
        var (db, path) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");
        (db as IDisposable)?.Dispose();

        var reopened = Reopen(mode, path);
        Assert.ThrowsAny<Exception>(() => reopened.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'a@x')"));
        Assert.Equal([1], Ids(reopened));
        (reopened as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Insert_DuplicateIntoTableLevelUniqueColumn_IsRejected(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, email TEXT, UNIQUE (email))");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'a@x')"));
        Assert.Equal([1], Ids(db));
    }

    [Fact(Skip = "Single-file storage does not enforce UNIQUE yet: its persisted ColumnFlags.Unique is also written for " +
                 "every inline PRIMARY KEY column (\"PRIMARY KEY implies UNIQUE\"), so a reopened file cannot tell a declared " +
                 "UNIQUE from a column of an EF Core composite key. Needs the column-entry format decision of known issues " +
                 "§10/§11.")]
    public void Insert_DuplicateIntoUniqueColumn_IsRejectedOnSingleFile()
    {
        var (db, _) = Open("single-file");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'a@x')"));
        Assert.Equal([1], Ids(db));
    }

    [Fact]
    public void MultiRowInsert_DuplicatingAUniqueIndex_IsRejected()
    {
        // A UNIQUE index was checked only on a single-row INSERT; the batched multi-row path skipped it.
        var (db, _) = Open("columnar", "CREATE TABLE t (id INTEGER PRIMARY KEY, email TEXT)");
        db.ExecuteSQL("CREATE UNIQUE INDEX ux_email ON t (email)");
        db.ExecuteSQL("INSERT INTO t (id, email) VALUES (1, 'a@x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, email) VALUES (2, 'b@x'), (3, 'a@x')"));
        Assert.Equal([1], Ids(db));
    }

    private (IDatabase Db, string Path) Open(string mode, string? ddl = null)
    {
        var path = Path.Combine(_root, mode == "single-file" ? $"db_{Guid.NewGuid():N}.scdb" : $"db_{Guid.NewGuid():N}");
        var db = Reopen(mode, path);
        var create = ddl ?? "CREATE TABLE t (id INTEGER PRIMARY KEY, email TEXT UNIQUE)";
        db.ExecuteSQL(mode == "page-based" ? create + " STORAGE = PAGE_BASED" : create);
        return (db, path);
    }

    private IDatabase Reopen(string mode, string path) => mode == "single-file"
        ? _factory.CreateWithOptions(path, "pw", DatabaseOptions.CreateSingleFileDefault())
        : _factory.Create(path, "pw");

    private static int[] Ids(IDatabase db) =>
        [.. db.ExecuteQuery("SELECT id FROM t").Select(r => Convert.ToInt32(r["id"], CultureInfo.InvariantCulture)).Order()];

    private static string? Email(IDatabase db, int id) =>
        db.ExecuteQuery($"SELECT email FROM t WHERE id = {id}").Single()["email"] as string;
}
