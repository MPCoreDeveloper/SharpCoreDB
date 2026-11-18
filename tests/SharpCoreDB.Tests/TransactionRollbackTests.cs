// <copyright file="TransactionRollbackTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Rolling a transaction back — <c>CancelBatchUpdate()</c>, which an ADO.NET <c>DbTransaction.Rollback()</c> calls —
/// restores every table, PageBased ones included (known issue §13).
/// <para>
/// Up to 2.1.4 the PageBased engine had no rollback: it writes into its page cache and page files directly, outside the
/// storage transaction buffer, and <c>PageBasedEngine.Rollback()</c> was a stub. A rolled-back UPDATE or INSERT stayed,
/// also after a reopen. The engine now keeps each changed page's image for the length of the transaction and puts it
/// back. A columnar rollback restored the rows but not the in-memory hash index, so after rolling back an UPDATE of an
/// indexed column, <c>WHERE name = 'old'</c> found nothing.
/// </para>
/// </summary>
public sealed class TransactionRollbackTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_TxRollback_{Guid.NewGuid():N}");

    public TransactionRollbackTests()
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

    private (IDatabase Db, string Path) Open(string mode, bool noEncrypt = false)
    {
        var path = Path.Combine(_root, $"db_{Guid.NewGuid():N}");
        var db = _factory.Create(path, "pw", false, new DatabaseConfig { NoEncryptMode = noEncrypt });
        var ddl = "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, name TEXT)";
        db.ExecuteSQL(mode == "page-based" ? ddl + " STORAGE = PAGE_BASED" : ddl);
        db.ExecuteSQL("CREATE INDEX ix_name ON t (name)");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (1, 1, 'a'), (2, 2, 'b'), (5, 5, 'e')");
        return (db, path);
    }

    private static string[] Rows(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => string.Join(",", r.Values.Select(v => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    [Theory]
    [MemberData(nameof(Modes))]
    public void CancelBatchUpdate_UpdateInsertAndDelete_RestoresEveryRowAndIndex(string mode)
    {
        var (db, path) = Open(mode);

        db.BeginBatchUpdate();
        db.ExecuteSQL("UPDATE t SET n = 99, name = 'zz' WHERE id = 2");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (3, 3, 'c')");
        db.ExecuteSQL("DELETE FROM t WHERE id = 1");
        db.CancelBatchUpdate();

        Assert.Equal(["1,1,a", "2,2,b", "5,5,e"], Rows(db, "SELECT id, n, name FROM t").Order());
        Assert.Equal(["1"], Rows(db, "SELECT n FROM t WHERE id = 1"));
        Assert.Equal(["2"], Rows(db, "SELECT id FROM t WHERE name = 'b'"));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM t WHERE name = 'zz'"));

        (db as IDisposable)?.Dispose();
        var reopened = _factory.Create(path, "pw");
        Assert.Equal(["1,1,a", "2,2,b", "5,5,e"], Rows(reopened, "SELECT id, n, name FROM t").Order());
        (reopened as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CancelBatchUpdate_ThenNewWrites_AreKeptAfterReopen(string mode)
    {
        var (db, path) = Open(mode);

        db.BeginBatchUpdate();
        db.ExecuteSQL("UPDATE t SET n = 99 WHERE id = 1");
        db.CancelBatchUpdate();
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (4, 4, 'd')");
        db.ExecuteSQL("UPDATE t SET n = 22 WHERE id = 2");

        (db as IDisposable)?.Dispose();
        var reopened = _factory.Create(path, "pw");
        Assert.Equal(["1,1,a", "2,22,b", "4,4,d", "5,5,e"], Rows(reopened, "SELECT id, n, name FROM t").Order());
        (reopened as IDisposable)?.Dispose();
    }

    [Fact]
    public void CancelBatchUpdate_PageBasedTransactionSpanningManyPages_RestoresTheTable()
    {
        var (db, path) = Open("page-based");

        db.BeginBatchUpdate();
        for (int i = 10; i < 600; i++)
        {
            db.ExecuteSQL($"INSERT INTO t (id, n, name) VALUES ({i}, {i}, 'row-{i}-{new string('x', 40)}')");
        }

        db.ExecuteSQL("UPDATE t SET n = 7 WHERE id = 2");
        db.CancelBatchUpdate();

        Assert.Equal(["1,1,a", "2,2,b", "5,5,e"], Rows(db, "SELECT id, n, name FROM t").Order());

        (db as IDisposable)?.Dispose();
        var reopened = _factory.Create(path, "pw");
        Assert.Equal(["1,1,a", "2,2,b", "5,5,e"], Rows(reopened, "SELECT id, n, name FROM t").Order());
        (reopened as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AdoNetTransactionRollback_DiscardsTheChanges(string mode)
    {
        var path = Path.Combine(_root, $"ado_{Guid.NewGuid():N}");
        using var connection = new SharpCoreDBConnection($"Data Source={path};Password=pw");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)" + (mode == "page-based" ? " STORAGE = PAGE_BASED" : "");
            create.ExecuteNonQuery();
            create.CommandText = "INSERT INTO t (id, n) VALUES (1, 1)";
            create.ExecuteNonQuery();
        }

        using (var transaction = connection.BeginTransaction())
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE t SET n = 99 WHERE id = 1";
            update.ExecuteNonQuery();
            transaction.Rollback();
        }

        using var select = connection.CreateCommand();
        select.CommandText = "SELECT n FROM t WHERE id = 1";
        Assert.Equal(1L, Convert.ToInt64(select.ExecuteScalar(), CultureInfo.InvariantCulture));
    }
}
