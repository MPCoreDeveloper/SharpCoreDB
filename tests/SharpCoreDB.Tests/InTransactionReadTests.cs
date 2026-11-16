// <copyright file="InTransactionReadTests.cs" company="MPCoreDeveloper">
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
/// Inside a transaction (<c>BeginBatchUpdate()</c>, which every ADO.NET transaction opens) every read sees the
/// transaction's own changes — a full scan as well as a primary-key or index lookup (known issue §12).
/// <para>
/// Up to 2.1.4 a columnar full scan read the data file as it was on disk, while the transaction's in-place UPDATEs and
/// its DELETEs were held in the storage buffers until commit: the scan returned an updated row's old value, still listed
/// a deleted row, and <c>WHERE n = 50</c> after <c>SET n = 50</c> found nothing. A primary-key lookup still returned a
/// deleted row. The scan now lays the buffered overwrites and tombstones over the records, and a point read of a
/// record the transaction deleted returns nothing.
/// </para>
/// </summary>
public sealed class InTransactionReadTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_TxReads_{Guid.NewGuid():N}");

    public InTransactionReadTests()
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

    public static TheoryData<string, bool> ModesAndEncryption => new()
    {
        { "columnar", false },
        { "columnar", true },
        { "page-based", false },
    };

    [Theory]
    [MemberData(nameof(ModesAndEncryption))]
    public void Scan_InsideATransaction_SeesTheTransactionsUpdate(string mode, bool noEncrypt)
    {
        var (db, _) = Open(mode, noEncrypt);

        db.BeginBatchUpdate();
        db.ExecuteSQL("UPDATE t SET n = 50 WHERE id = 1");

        Assert.Equal(["1,50", "2,2", "5,5"], Rows(db, "SELECT id, n FROM t").Order());
        Assert.Equal(["1"], Rows(db, "SELECT id FROM t WHERE n = 50"));
        db.EndBatchUpdate();
    }

    [Theory]
    [MemberData(nameof(ModesAndEncryption))]
    public void Reads_InsideATransaction_DoNotSeeARowItDeleted(string mode, bool noEncrypt)
    {
        var (db, _) = Open(mode, noEncrypt);

        db.BeginBatchUpdate();
        db.ExecuteSQL("DELETE FROM t WHERE id = 2");

        Assert.Equal(["1", "5"], Rows(db, "SELECT id FROM t").Order());
        Assert.Empty(db.ExecuteQuery("SELECT n FROM t WHERE id = 2"));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM t WHERE name = 'b'"));
        db.EndBatchUpdate();

        Assert.Equal(["1", "5"], Rows(db, "SELECT id FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(ModesAndEncryption))]
    public void Scan_InsideATransaction_SeesUpdatesAndInsertsTogether(string mode, bool noEncrypt)
    {
        var (db, _) = Open(mode, noEncrypt);

        db.BeginBatchUpdate();
        db.ExecuteSQL("UPDATE t SET n = 50 WHERE id = 1");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (3, 3, 'c')");
        db.ExecuteSQL("UPDATE t SET name = 'z' WHERE id = 5");

        Assert.Equal(["1,50,a", "2,2,b", "3,3,c", "5,5,z"], Rows(db, "SELECT id, n, name FROM t").Order());
        Assert.Equal(["5"], Rows(db, "SELECT id FROM t WHERE name = 'z'"));
        db.EndBatchUpdate();

        Assert.Equal(["1,50,a", "2,2,b", "3,3,c", "5,5,z"], Rows(db, "SELECT id, n, name FROM t").Order());
    }
}
