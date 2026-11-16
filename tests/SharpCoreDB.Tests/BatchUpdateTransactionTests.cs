// <copyright file="BatchUpdateTransactionTests.cs" company="MPCoreDeveloper">
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
/// The storage transaction behind <c>BeginBatchUpdate()</c> / <c>EndBatchUpdate()</c> — the one an ADO.NET transaction
/// (and so linq2db's and EF Core's) opens.
/// <para>
/// Up to 2.1.4 two defects made the commit itself fail or lose its result. Committing a transaction that had buffered a
/// whole-file write (the database metadata among them) threw "Collection was modified": the flush sent each buffered
/// write back through <c>Storage.WriteBytes</c>, which — the transaction still being open — buffered it again into the
/// list being flushed. And every commit rebuilt each table's primary-key index by searching the index it had just
/// cleared, so after <c>EndBatchUpdate()</c> a columnar table's scan returned no rows until the database was reopened.
/// A PageBased DELETE — in a transaction or not — was undone by every reopen.
/// </para>
/// </summary>
public sealed class BatchUpdateTransactionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_BatchTx_{Guid.NewGuid():N}");

    public BatchUpdateTransactionTests()
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
    public void EndBatchUpdate_WithoutChanges_KeepsEveryRowVisible(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 2)");

        db.BeginBatchUpdate();
        db.EndBatchUpdate();

        Assert.Equal(["1,1", "2,2"], Rows(db, "SELECT id, n FROM t").Order());
        Assert.Equal(["1"], Rows(db, "SELECT n FROM t WHERE id = 1"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EndBatchUpdate_AfterUpdateInsertAndDelete_CommitsAndShowsTheResult(string mode)
    {
        var (db, path) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, name TEXT)");
        db.ExecuteSQL("CREATE INDEX ix_name ON t (name)");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (1, 1, 'ann'), (2, 2, 'bob')");

        db.BeginBatchUpdate();
        db.ExecuteSQL("UPDATE t SET n = 50 WHERE id = 1");
        db.ExecuteSQL("UPDATE t SET name = 'zed' WHERE id = 2");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (3, 3, 'cy')");
        db.ExecuteSQL("DELETE FROM t WHERE id = 1");
        db.ExecuteSQL("UPDATE t SET n = n + 1 WHERE id = 3");
        db.EndBatchUpdate();

        Assert.Equal(["2,2,zed", "3,4,cy"], Rows(db, "SELECT id, n, name FROM t").Order());
        Assert.Equal(["2"], Rows(db, "SELECT id FROM t WHERE name = 'zed'"));

        (db as IDisposable)?.Dispose();
        var reopened = _factory.Create(path, "pw");
        Assert.Equal(["2,2,zed", "3,4,cy"], Rows(reopened, "SELECT id, n, name FROM t").Order());
        (reopened as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageBasedDelete_IsKeptAfterReopen_AndLaterInsertsCoexist(bool inTransaction)
    {
        // PageBased slots stored only offset and length, so the Deleted flag was never written and a reopen read every
        // slot as Active: up to 2.1.4 every PageBased DELETE was undone by a reopen.
        var (db, path) = Open("page-based", "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 2), (3, 3)");
        if (inTransaction)
        {
            db.BeginBatchUpdate();
        }

        db.ExecuteSQL("DELETE FROM t WHERE id = 2");
        if (inTransaction)
        {
            db.EndBatchUpdate();
        }

        (db as IDisposable)?.Dispose();

        var reopened = _factory.Create(path, "pw");
        Assert.Equal(["1,1", "3,3"], Rows(reopened, "SELECT id, n FROM t").Order());
        reopened.ExecuteSQL("INSERT INTO t (id, n) VALUES (4, 4)");
        (reopened as IDisposable)?.Dispose();

        var again = _factory.Create(path, "pw");
        Assert.Equal(["1,1", "3,3", "4,4"], Rows(again, "SELECT id, n FROM t").Order());
        Assert.Empty(again.ExecuteQuery("SELECT n FROM t WHERE id = 2"));
        (again as IDisposable)?.Dispose();
    }

    [Fact]
    public void CancelBatchUpdate_Columnar_DiscardsTheUpdate()
    {
        var (db, _) = Open("columnar", "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 2)");

        db.BeginBatchUpdate();
        db.ExecuteSQL("UPDATE t SET n = 99 WHERE id = 1");
        db.CancelBatchUpdate();

        Assert.Equal(["1,1", "2,2"], Rows(db, "SELECT id, n FROM t").Order());
    }
}
