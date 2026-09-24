// <copyright file="StructuredBatchDmlTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// The SQL-free batch UPDATE/DELETE API (<c>Database.UpdateBatch</c> / <c>Database.DeleteBatch</c>) — the
/// UPDATE/DELETE siblings of the INSERT fast path. These tests exist because a batch API that is fast and
/// wrong is worse than no API: every shape the API can take has to be pinned (indexed key, primary key,
/// fixed-width, PageBased, encrypted, and the unindexed fallback), and both a delete and an update have to
/// survive a reopen rather than merely look right in the open instance.
/// <para>
/// The path they exercise is new, so the assertions are deliberately about <b>values and row identity</b>,
/// not about counters: the counters already have their own canaries (FixedWidthBulkUpdateTests,
/// FixedWidthBulkDeleteTests), and this file's job is to prove the structured entry point reaches the same
/// machinery they pin.
/// </para>
/// </summary>
public sealed class StructuredBatchDmlTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public StructuredBatchDmlTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_StructuredDml_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private Database CreateDb(bool noEncrypt = true, StorageEngineType? engine = null) =>
        (Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = noEncrypt,
            EnableAtRestRecordEncryption = !noEncrypt,
            StorageEngineType = engine ?? StorageEngineType.Auto,
        });

    private static List<(string KeyColumn, object? KeyValue, Dictionary<string, object> Values)> UpdateOp(
        string keyColumn, object? keyValue, string valueColumn, object value) =>
        [(keyColumn, keyValue, new Dictionary<string, object>(1) { [valueColumn] = value })];

    private static List<(string KeyColumn, object? KeyValue)> DeleteOp(string keyColumn, object? keyValue) =>
        [(keyColumn, keyValue)];

    private void InsertFive(Database db, string table, bool withScoreColumn = true)
    {
        // No primary key: the shape the fair arm measures (row location through a secondary index).
        db.ExecuteSQL(withScoreColumn
            ? $"CREATE TABLE {table} (name TEXT NOT NULL, note TEXT, score REAL)"
            : $"CREATE TABLE {table} (name TEXT NOT NULL, note TEXT)");
        db.ExecuteSQL($"CREATE INDEX idx_{table}_name ON {table}(name)");
        var rows = Enumerable.Range(1, 5).Select(i => new Dictionary<string, object>
        {
            ["name"] = $"u{i}",
            ["note"] = $"n{i}",
            ["score"] = i * 1.0,
        }).ToList();
        db.InsertBatch(table, rows);
        db.Flush();
    }

    private static double ScoreOf(Database db, string table, string name)
    {
        var rows = db.ExecuteQuery($"SELECT score FROM {table} WHERE name = @name",
            new Dictionary<string, object?> { ["@name"] = name });
        Assert.Single(rows);
        return Convert.ToDouble(rows[0]["score"]);
    }

    [Fact]
    public void UpdateBatch_ByIndexedKeyOnNonPkTable_UpdatesOnlyTheMatchingRow()
    {
        using var db = CreateDb();
        InsertFive(db, "t");

        int updated = db.UpdateBatch("t", UpdateOp("name", "u3", "score", 9.5));

        Assert.Equal(1, updated);
        Assert.Equal(9.5, ScoreOf(db, "t", "u3"));
        Assert.Equal(1.0, ScoreOf(db, "t", "u1"));
        Assert.Equal(5.0, ScoreOf(db, "t", "u5"));
    }

    [Fact]
    public void DeleteBatch_ByIndexedKeyOnNonPkTable_RemovesOnlyTheMatchingRow()
    {
        using var db = CreateDb();
        InsertFive(db, "t");

        int deleted = db.DeleteBatch("t", DeleteOp("name", "u3"));

        Assert.Equal(1, deleted);
        var remaining = db.ExecuteQuery("SELECT name FROM t ORDER BY name");
        Assert.Equal(4, remaining.Count);
        Assert.DoesNotContain(remaining, r => (string?)r["name"] == "u3");
    }

    [Fact]
    public void UpdateBatch_ByPrimaryKey_UpdatesRowAndSurvivesReopen()
    {
        const string Table = "docs";
        using (var db = CreateDb())
        {
            db.ExecuteSQL($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.InsertBatch(Table, [.. Enumerable.Range(1, 5).Select(i => new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"u{i}",
                ["score"] = i * 1.0,
            })]);
            db.Flush();

            Assert.Equal(1, db.UpdateBatch(Table, UpdateOp("id", 3, "score", 42.5)));
            db.Flush();
        }

        using var reopened = CreateDb();
        var rows = reopened.ExecuteQuery($"SELECT score FROM {Table} WHERE id = 3");
        Assert.Single(rows);
        Assert.Equal(42.5, Convert.ToDouble(rows[0]["score"]));
    }

    [Fact]
    public void DeleteBatch_ByPrimaryKey_RemovesRowAndSurvivesReopen()
    {
        const string Table = "docs";
        using (var db = CreateDb())
        {
            db.ExecuteSQL($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, name TEXT)");
            db.InsertBatch(Table, [.. Enumerable.Range(1, 5).Select(i => new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"u{i}",
            })]);
            db.Flush();

            Assert.Equal(1, db.DeleteBatch(Table, DeleteOp("id", 3)));
            db.Flush();
        }

        using var reopened = CreateDb();
        Assert.Empty(reopened.ExecuteQuery($"SELECT name FROM {Table} WHERE id = 3"));
        Assert.Single(reopened.ExecuteQuery($"SELECT name FROM {Table} WHERE id = 4"));
    }

    [Fact]
    public void UpdateBatch_EncryptedDatabase_UpdatesRowAndSurvivesReopen()
    {
        const string Table = "t";
        using (var db = CreateDb(noEncrypt: false))
        {
            InsertFive(db, Table);
            Assert.Equal(1, db.UpdateBatch(Table, UpdateOp("name", "u2", "score", 7.25)));
            db.Flush();
        }

        using var reopened = CreateDb(noEncrypt: false);
        Assert.Equal(7.25, ScoreOf(reopened, Table, "u2"));
    }

    [Fact]
    public void UpdateBatch_FixedWidthTable_UsesTheContiguousFastPath()
    {
        // An explicit PRIMARY KEY grants the fixed-width layout (SqlParser.DDL.cs:392-407), which is what the
        // B8 contiguous path is gated on — so a structured batch that resolves its own keys must reach it too.
        const string Table = "docs";
        using var db = CreateDb();
        db.ExecuteSQL($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        db.InsertBatch(Table, [.. Enumerable.Range(1, 400).Select(i => new Dictionary<string, object>
        {
            ["id"] = i,
            ["name"] = $"u{i}",
            ["score"] = i * 1.0,
        })]);
        db.Flush();

        Assert.True(db.TryGetTable(Table, out var probe));
        var table = Assert.IsType<Table>(probe);
        Assert.True(table.IsFixedWidthRecords);
        Assert.Equal(0, table.BulkContiguousUpdateBatches);

        var ops = new List<(string KeyColumn, object? KeyValue, Dictionary<string, object> Values)>();
        for (int i = 1; i <= 200; i++)
        {
            ops.Add(("id", i, new Dictionary<string, object>(1) { ["score"] = 99.0 }));
        }

        Assert.Equal(200, db.UpdateBatch(Table, ops));

        Assert.Equal(1, table.BulkContiguousUpdateBatches);
        Assert.Equal(99.0, Convert.ToDouble(db.ExecuteQuery($"SELECT score FROM {Table} WHERE id = 150")[0]["score"]));
        Assert.Equal(300.0, Convert.ToDouble(db.ExecuteQuery($"SELECT score FROM {Table} WHERE id = 300")[0]["score"]));
    }

    [Fact]
    public void UpdateBatch_PageBased_UpdatesRowThroughTheIndexedKey()
    {
        const string Table = "t";
        using var db = CreateDb(engine: StorageEngineType.PageBased);
        InsertFive(db, Table);

        Assert.Equal(1, db.UpdateBatch(Table, UpdateOp("name", "u4", "score", 3.5)));

        Assert.Equal(3.5, ScoreOf(db, Table, "u4"));
        Assert.Equal(1.0, ScoreOf(db, Table, "u1"));
    }

    [Fact]
    public void DeleteBatch_OnUnindexedColumn_FallsBackAndDeletesTheMatchingRow()
    {
        const string Table = "t";
        using var db = CreateDb();
        InsertFive(db, Table);

        // `note` carries no explicit index. A Columnar CREATE TABLE does auto-register one per column, so this
        // asserts the fallback's *effect* (the row is gone, and only that row) rather than which branch ran:
        // the branch itself is what the fixed-width and PK tests above pin.
        int deleted = db.DeleteBatch(Table, DeleteOp("note", "n2"));

        Assert.Equal(1, deleted);
        var remaining = db.ExecuteQuery($"SELECT name FROM {Table} ORDER BY name");
        Assert.Equal(4, remaining.Count);
        Assert.DoesNotContain(remaining, r => (string?)r["name"] == "u2");
    }

    [Fact]
    public void UpdateBatch_NullKeyValue_Throws()
    {
        using var db = CreateDb();
        InsertFive(db, "t");

        var ops = new List<(string KeyColumn, object? KeyValue, Dictionary<string, object> Values)>
        {
            ("name", null, new Dictionary<string, object>(1) { ["score"] = 1.0 }),
        };

        Assert.Throws<ArgumentException>(() => db.UpdateBatch("t", ops));
        Assert.Equal(1.0, ScoreOf(db, "t", "u1"));
    }

    [Fact]
    public void DeleteBatch_NullKeyValue_Throws()
    {
        using var db = CreateDb();
        InsertFive(db, "t");

        Assert.Throws<ArgumentException>(() =>
            db.DeleteBatch("t", new List<(string KeyColumn, object? KeyValue)> { ("name", null) }));
        Assert.Equal(5, db.ExecuteQuery("SELECT name FROM t").Count);
    }

    [Fact]
    public void DeleteBatch_KeyThatMatchesNothing_ReturnsZero()
    {
        using var db = CreateDb();
        InsertFive(db, "t");

        Assert.Equal(0, db.DeleteBatch("t", DeleteOp("name", "not-there")));
        Assert.Equal(5, db.ExecuteQuery("SELECT name FROM t").Count);
    }

    [Fact]
    public void UpdateBatch_UnknownTable_Throws()
    {
        using var db = CreateDb();
        Assert.Throws<InvalidOperationException>(() =>
            db.UpdateBatch("missing", UpdateOp("name", "u1", "score", 1.0)));
        Assert.Throws<InvalidOperationException>(() =>
            db.DeleteBatch("missing", DeleteOp("name", "u1")));
    }
}
