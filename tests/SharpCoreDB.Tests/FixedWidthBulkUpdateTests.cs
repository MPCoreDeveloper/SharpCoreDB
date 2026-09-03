// <copyright file="FixedWidthBulkUpdateTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

/// <summary>
/// B8: single-pass contiguous UPDATE fast path (Table.TryBulkUpdateContiguousFixedWidth). When a
/// batch UPDATE hits a plaintext fixed-width table with strictly ascending `pk = literal` matches
/// whose records are physically adjacent, the target records are read as one contiguous byte range
/// and patched in memory. Every other shape must fall back to the generic per-row loop and stay
/// correct.
/// </summary>
public sealed class FixedWidthBulkUpdateTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FixedWidthBulkUpdateTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_BulkUpd_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private IDatabase CreateDb(bool noEncrypt = true) => _factory.Create(_dirPath, "pw", isReadOnly: false,
        config: new DatabaseConfig { NoEncryptMode = noEncrypt });

    private static Table TableOf(IDatabase db, string tableName)
    {
        Assert.True(db.TryGetTable(tableName, out var t));
        return Assert.IsType<Table>(t);
    }

    private static void InsertDocs(IDatabase db, int fromId, int toId)
    {
        var stmts = new List<string>(toId - fromId + 1);
        for (int i = fromId; i <= toId; i++)
        {
            stmts.Add(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO docs VALUES ({0}, 'user{0}', {1}, {2})", i, i * 0.5, 20 + (i % 60)));
        }

        db.ExecuteBatchSQL(stmts);
    }

    private static List<string> BuildUpdates(int fromId, int toId, string setExpr)
    {
        var stmts = new List<string>(Math.Abs(toId - fromId) + 1);
        for (int i = fromId; i <= toId; i++)
        {
            stmts.Add(string.Format(CultureInfo.InvariantCulture,
                "UPDATE docs SET {0} WHERE id = {1}", setExpr, i));
        }

        return stmts;
    }

    [Fact]
    public void ContiguousAscendingUpdates_EngageBulkPath_AndPersist()
    {
        IDatabase? db = null;
        try
        {
            db = CreateDb();
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL, age INTEGER)");
            InsertDocs(db, 1, 2000);
            db.Flush();

            var table = TableOf(db, "docs");
            Assert.True(table.IsFixedWidthRecords);
            Assert.Equal(StorageMode.Columnar, table.StorageMode);
            Assert.Equal(0, table.BulkContiguousUpdateBatches);

            // 1000 ascending `id = literal` updates — every row patched via one contiguous read.
            db.ExecuteBatchSQL(BuildUpdates(1, 1000, "score = 99.0"));
            db.Flush();
            Assert.Equal(1, table.BulkContiguousUpdateBatches);

            // Full-scan integrity: no rows lost, untouched rows unchanged.
            var all = db.ExecuteQuery("SELECT COUNT(*) AS total FROM docs");
            Assert.Equal(2000L, Convert.ToInt64(all[0].Values.First()));
            var tail = db.ExecuteQuery("SELECT score FROM docs WHERE id = 2000");
            Assert.Equal(1000.0, Convert.ToDouble(tail[0]["score"]));

            // Reopen: the in-place writes must be durable and readable.
            (db as IDisposable)?.Dispose();
            db = CreateDb();
            for (int i = 1; i <= 2000; i += 251)
            {
                var rows = db.ExecuteQuery("SELECT score FROM docs WHERE id = @id",
                    new Dictionary<string, object?> { ["@id"] = i });
                Assert.Single(rows);
                Assert.Equal(i <= 1000 ? 99.0 : i * 0.5, Convert.ToDouble(rows[0]["score"]));
            }
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void GappedUpdates_FallBackToGenericLoop_AndStayCorrect()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 1000);
            db.Flush();

            var table = TableOf(db, "docs");

            // Odd ids only: the records are not physically adjacent within the batch (id 1,3,5,...),
            // so the contiguous fast path must refuse and the generic per-row loop applies them.
            var stmts = new List<string>(500);
            for (int i = 1; i <= 1000; i += 2)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture, "UPDATE docs SET score = 7.5 WHERE id = {0}", i));
            }

            db.ExecuteBatchSQL(stmts);
            db.Flush();
            Assert.Equal(0, table.BulkContiguousUpdateBatches);

            for (int i = 1; i <= 1000; i += 137)
            {
                var rows = db.ExecuteQuery("SELECT score FROM docs WHERE id = @id",
                    new Dictionary<string, object?> { ["@id"] = i });
                Assert.Single(rows);
                Assert.Equal(i % 2 == 1 ? 7.5 : i * 0.5, Convert.ToDouble(rows[0]["score"]));
            }
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void RepeatedCommits_EngageAgain_WithCurrentBytes()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 500);
            db.Flush();

            var table = TableOf(db, "docs");
            db.ExecuteBatchSQL(BuildUpdates(1, 500, "score = 1.0"));
            db.Flush();
            Assert.Equal(1, table.BulkContiguousUpdateBatches);

            // Second committed batch over the same rows must see the first batch's bytes (no stale reads).
            db.ExecuteBatchSQL(BuildUpdates(1, 500, "score = 2.0"));
            db.Flush();
            Assert.Equal(2, table.BulkContiguousUpdateBatches);

            var rows = db.ExecuteQuery("SELECT score FROM docs WHERE id = 250");
            Assert.Single(rows);
            Assert.Equal(2.0, Convert.ToDouble(rows[0]["score"]));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void IndexedColumnUpdate_FallsBack_AndRepointsIndex()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");
            InsertDocs(db, 1, 800);
            db.Flush();

            var table = TableOf(db, "docs");
            // name is hash-indexed → the bulk path must refuse (index re-point is required).
            var stmts = new List<string>(800);
            for (int i = 1; i <= 800; i++)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE docs SET name = 'renamed-{0}' WHERE id = {0}", i));
            }

            db.ExecuteBatchSQL(stmts);
            Assert.Equal(0, table.BulkContiguousUpdateBatches);

            // Index lookups must return the re-pointed rows (no stale entries).
            var byIndex = db.ExecuteQuery("SELECT id FROM docs WHERE name = 'renamed-42'");
            Assert.Single(byIndex);
            Assert.Equal(42L, Convert.ToInt64(byIndex[0]["id"]));
            var byPk = db.ExecuteQuery("SELECT name FROM docs WHERE id = 42");
            Assert.Equal("renamed-42", byPk[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void EncryptedOrDefaultConfig_FallsBack_AndStaysCorrect()
    {
        var db = CreateDb(noEncrypt: false); // default config: the gate requires NoEncryptMode
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 300);
            db.Flush();

            var table = TableOf(db, "docs");
            db.ExecuteBatchSQL(BuildUpdates(1, 300, "score = 4.25"));
            db.Flush();
            Assert.Equal(0, table.BulkContiguousUpdateBatches);

            var rows = db.ExecuteQuery("SELECT score FROM docs WHERE id = 300");
            Assert.Single(rows);
            Assert.Equal(4.25, Convert.ToDouble(rows[0]["score"]));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
