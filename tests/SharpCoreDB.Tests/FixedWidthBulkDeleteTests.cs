// <copyright file="FixedWidthBulkDeleteTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

/// <summary>
/// B9: single-pass contiguous DELETE fast path (Table.TryBulkDeleteContiguousFixedWidth). Strictly
/// ascending `pk = literal` DELETEs on a plaintext fixed-width table with physically adjacent
/// records remove every PK/hash-index entry in one pass (no per-row pread or full-row
/// deserialization). Any other shape falls back to the generic loop and stays correct.
/// </summary>
public sealed class FixedWidthBulkDeleteTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FixedWidthBulkDeleteTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_BulkDel_{Guid.NewGuid():N}");
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
        var stmts = new List<string>(Math.Abs(toId - fromId) + 1);
        for (int i = fromId; i <= toId; i++)
        {
            stmts.Add(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO docs VALUES ({0}, 'user{0}', {1})", i, i * 0.5));
        }

        db.ExecuteBatchSQL(stmts);
    }

    private static List<string> BuildDeletes(int fromId, int toId)
    {
        var stmts = new List<string>(Math.Abs(toId - fromId) + 1);
        for (int i = fromId; i <= toId; i++)
        {
            stmts.Add(string.Format(CultureInfo.InvariantCulture, "DELETE FROM docs WHERE id = {0}", i));
        }

        return stmts;
    }

    [Fact]
    public void ContiguousAscendingDeletes_EngageBulkPath_AndRemoveEveryIndex()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");
            InsertDocs(db, 1, 2000);
            db.Flush();

            var table = TableOf(db, "docs");
            Assert.Equal(0, table.BulkContiguousDeleteBatches);

            db.ExecuteBatchSQL(BuildDeletes(1, 1000));
            db.Flush();
            Assert.Equal(1, table.BulkContiguousDeleteBatches);

            // Deleted rows are gone from the PK and hash-index paths; live rows stay reachable.
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 500"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user500'"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1500"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user1500'"));

            // A second contiguous DELETE batch over the remaining rows engages again.
            db.ExecuteBatchSQL(BuildDeletes(1001, 2000));
            db.Flush();
            Assert.Equal(2, table.BulkContiguousDeleteBatches);
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1500"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user1500'"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void DescendingUpdateBatch_FallsBackToGenericLoop_AndAppliesEveryRow()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 1000);
            db.Flush();

            var table = TableOf(db, "docs");
            var stmts = new List<string>(1000);
            for (int i = 1000; i >= 1; i--)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture, "UPDATE docs SET score = 7.5 WHERE id = {0}", i));
            }

            // Descending order must refuse the contiguous fast path AND still apply every row via
            // the generic loop (regression: large descending batches used to apply nothing).
            db.ExecuteBatchSQL(stmts);
            db.Flush();
            Assert.Equal(0, table.BulkContiguousUpdateBatches);

            for (int i = 1; i <= 1000; i += 137)
            {
                var rows = db.ExecuteQuery("SELECT score FROM docs WHERE id = @id",
                    new Dictionary<string, object?> { ["@id"] = i });
                Assert.Single(rows);
                Assert.Equal(7.5, Convert.ToDouble(rows[0]["score"]));
            }
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void AscendingUpdateBatch_BatchedPageFlush_AppliesEveryRow_AcrossReopen()
    {
        // Regression for the commit flush of buffered in-place overwrites: a single UPDATE batch of
        // 2000 rows lands in one transaction with 2000 buffered overwrites, which FlushBuffered-
        // Overwrites now writes with one read-modify-write per touched storage page instead of two
        // pwrites per row. Values must survive point reads and a reopen.
        IDatabase? db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 2000);
            db.Flush();

            var table = TableOf(db, "docs");
            var stmts = new List<string>(2000);
            for (int i = 1; i <= 2000; i++)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE docs SET score = 9.25 WHERE id = {0}", i));
            }

            db.ExecuteBatchSQL(stmts);
            db.Flush();
            Assert.Equal(1, table.BulkContiguousUpdateBatches);

            var c = db.ExecuteQuery("SELECT COUNT(*) AS n FROM docs");
            Assert.Equal(2000L, Convert.ToInt64(c[0].Values.First()));
            for (int i = 1; i <= 2000; i += 137)
            {
                var rows = db.ExecuteQuery("SELECT score, name FROM docs WHERE id = @id",
                    new Dictionary<string, object?> { ["@id"] = i });
                Assert.Single(rows);
                Assert.Equal(9.25, Convert.ToDouble(rows[0]["score"]));
            }
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen: the read-modify-write page flushes must have persisted exactly.
        db = CreateDb();
        try
        {
            var scan = db.ExecuteQuery("SELECT id FROM docs");
            Assert.Equal(2000, scan.Count);
            for (int i = 1; i <= 2000; i += 333)
            {
                var rows = db.ExecuteQuery("SELECT score FROM docs WHERE id = @id",
                    new Dictionary<string, object?> { ["@id"] = i });
                Assert.Single(rows);
                Assert.Equal(9.25, Convert.ToDouble(rows[0]["score"]));
            }
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void BulkDelete_ScanAndCountStayConsistent()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 2000);
            db.Flush();

            var table = TableOf(db, "docs");
            var stmts = new List<string>(1000);
            for (int i = 1; i <= 1000; i++)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture, "DELETE FROM docs WHERE id = {0}", i));
            }

            db.ExecuteBatchSQL(stmts);
            db.Flush();
            Assert.Equal(1, table.BulkContiguousDeleteBatches);

            // After logical deletes the full scan and COUNT(*) must reflect exactly the live rows
            // (regression for the earlier inconsistency, resolved by the BTree separator-delete fix).
            var scan = db.ExecuteQuery("SELECT id FROM docs");
            Assert.Equal(1000, scan.Count);
            Assert.All(scan, r => Assert.True(Convert.ToInt64(r["id"]) > 1000));
            var c = db.ExecuteQuery("SELECT COUNT(*) AS n FROM docs");
            Assert.Equal(1000L, Convert.ToInt64(c[0].Values.First()));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void GappedDeletes_FallBackToGenericLoop_AndStayCorrect()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 800);
            db.Flush();

            var table = TableOf(db, "docs");
            var stmts = new List<string>(400);
            for (int i = 1; i <= 800; i += 2)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture, "DELETE FROM docs WHERE id = {0}", i));
            }

            db.ExecuteBatchSQL(stmts);
            db.Flush();
            Assert.Equal(0, table.BulkContiguousDeleteBatches);

            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 3"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 2"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user3'"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user2'"));

            // Generic-path deletes keep the scan/count live too.
            var scan = db.ExecuteQuery("SELECT id FROM docs");
            Assert.Equal(400, scan.Count);
            Assert.All(scan, r => Assert.Equal(0L, Convert.ToInt64(r["id"]) % 2));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void DeletesPersistAcrossReopen_AfterFlushCompaction()
    {
        IDatabase? db = CreateDb();
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        InsertDocs(db, 1, 100);
        db.Flush();

        var stmts = new List<string>(50);
        for (int i = 1; i <= 50; i++)
        {
            stmts.Add(string.Format(CultureInfo.InvariantCulture, "DELETE FROM docs WHERE id = {0}", i));
        }

        db.ExecuteBatchSQL(stmts);
        db.Flush(); // commit-time tombstones (or legacy flush compaction) remove the rows physically
        (db as IDisposable)?.Dispose();

        db = CreateDb();
        var scan = db.ExecuteQuery("SELECT id FROM docs ORDER BY id");
        Assert.Equal(50, scan.Count);
        Assert.Equal(51L, Convert.ToInt64(scan[0]["id"]));
        Assert.Equal(100L, Convert.ToInt64(scan[^1]["id"]));
        var c = db.ExecuteQuery("SELECT COUNT(*) AS n FROM docs");
        Assert.Equal(50L, Convert.ToInt64(c[0].Values.First()));
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void CanonicalBatchDelete_EngagesStructuredPath()
    {
        // Regression: TryScanCanonicalDml's DELETE branch did not consume whitespace/WHERE after
        // the table name, so every canonical `DELETE ... WHERE col = literal` fell back to the
        // regex path and bypassed the structured batch-DELETE fast paths (DeleteMultipleKeys).
        IDatabase? db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertDocs(db, 1, 50);
            db.Flush();

            var before = ((SharpCoreDB.Database)db).CanonicalDeleteStatementsParsed;
            db.ExecuteBatchSQL(BuildDeletes(1, 10));
            db.Flush();

            Assert.True(
                ((SharpCoreDB.Database)db).CanonicalDeleteStatementsParsed >= before + 10,
                "canonical DELETE statements must flow through the structured batch path");
            Assert.Equal(40, db.ExecuteQuery("SELECT id FROM docs").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void LegacyUnorderedLayout_AscendingPkBatchDelete_StaysCorrectAcrossReopen()
    {
        // Sequential ascending-PK batch resolution may not assume physical row order: when rows
        // were inserted in a shuffled order the batch must still delete exactly the requested keys
        // (the sequential scan detects the disorder, keeps scanning to EOF and matches by set).
        IDatabase? db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig { NoEncryptMode = true, AutoFixedWidthRecords = false });
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            // Shuffle 1..2000 with a fixed seed so the physical file order is not PK-ordered.
            var ids = new List<int>(2000);
            for (int i = 1; i <= 2000; i++) ids.Add(i);
            var rng = new Random(20260904);
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            var stmts = new List<string>(2000);
            foreach (int id in ids)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture,
                    "INSERT INTO docs VALUES ({0}, 'user{0}', {1})", id, id * 0.5));
            }

            db.ExecuteBatchSQL(stmts);
            db.Flush();
            Assert.Equal(2000, db.ExecuteQuery("SELECT id FROM docs").Count);

            // Ascending pk batch over a physically unordered file: keys 101..140.
            var dels = new List<string>(40);
            for (int i = 101; i <= 140; i++)
            {
                dels.Add(string.Format(CultureInfo.InvariantCulture, "DELETE FROM docs WHERE id = {0}", i));
            }

            db.ExecuteBatchSQL(dels);
            db.Flush();

            Assert.Equal(1960, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 120"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 100"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 141"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user150'"));
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen: the deleted rows stay gone, live rows stay reachable.
        db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig { NoEncryptMode = true, AutoFixedWidthRecords = false });
        try
        {
            Assert.Equal(1960, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 101"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 140"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 100"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 141"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void BatchDelete_CommitTimeTombstones_SurviveReopenWithoutExplicitFlush()
    {
        // ExecuteBatchSQL wraps the whole DELETE batch in one storage transaction. The deleted
        // positions must be tombstoned AT COMMIT (not at a later db.Flush()), so durability must
        // hold even when the database is disposed without an explicit Flush afterwards.
        IDatabase? db = CreateDb();
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        InsertDocs(db, 1, 200);
        db.Flush();

        db.ExecuteBatchSQL(BuildDeletes(1, 100)); // commits inside ExecuteBatchSQL
        (db as IDisposable)?.Dispose(); // no explicit Flush after the delete batch

        db = CreateDb();
        var scan = db.ExecuteQuery("SELECT id FROM docs ORDER BY id");
        Assert.Equal(100, scan.Count);
        Assert.Equal(101L, Convert.ToInt64(scan[0]["id"]));
        Assert.Equal(200L, Convert.ToInt64(scan[^1]["id"]));
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void LegacyLayoutBatchDelete_BulkPkRemove_StaysCorrectAcrossReopen()
    {
        // Regression for DeleteRecordsCore's PK cleanup: with fixed-width disabled the table uses
        // the legacy layout, so an ascending `pk = literal` batch cannot take the contiguous
        // fast path and flows through the generic loop + Index.DeleteBulk (descending order).
        // Every deleted row must vanish from PK/hash lookups and from a reopened database.
        IDatabase? db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig { NoEncryptMode = true, AutoFixedWidthRecords = false });
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");
            InsertDocs(db, 1, 2000);
            db.Flush();

            var table = TableOf(db, "docs");
            Assert.Equal(0, table.BulkContiguousDeleteBatches);

            db.ExecuteBatchSQL(BuildDeletes(1, 1000));
            db.Flush();

            // Legacy layout must NOT engage the fixed-width contiguous fast path.
            Assert.Equal(0, table.BulkContiguousDeleteBatches);

            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 500"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user500'"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1500"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user1500'"));
            Assert.Equal(1000, db.ExecuteQuery("SELECT id FROM docs").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen: tombstones keep the deleted rows gone; the PK index rebuilt from the file must
        // not resurrect any of them.
        db = CreateDb();
        try
        {
            var scan = db.ExecuteQuery("SELECT id FROM docs ORDER BY id");
            Assert.Equal(1000, scan.Count);
            Assert.Equal(1001L, Convert.ToInt64(scan[0]["id"]));
            Assert.Equal(2000L, Convert.ToInt64(scan[^1]["id"]));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 500"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user500'"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
