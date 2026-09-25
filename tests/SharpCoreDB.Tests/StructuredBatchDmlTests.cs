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
using System.Globalization;
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

    private Database CreateDb(bool noEncrypt = true, StorageEngineType? engine = null, bool eagerDeleteIndexes = false) =>
        (Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = noEncrypt,
            EnableAtRestRecordEncryption = !noEncrypt,
            StorageEngineType = engine ?? StorageEngineType.Auto,
            EnableDeferredDeleteIndexes = !eagerDeleteIndexes,
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
    public void DeleteBatch_WithDeferredIndexesAndNoPk_DeletesRowsAndSurvivesReopen()
    {
        // The product default defers index maintenance (EnableDeferredDeleteIndexes), which is the mode where a
        // delete's index work is skipped and the tombstone is the durable truth. This pins both halves from the
        // outside: the rows go, the index still answers for the survivors, and the deletion survives a reopen.
        const string Table = "t";
        using (var db = CreateDb())
        {
            InsertFive(db, Table);
            var table = GetTable(db, Table);
            Assert.True(table.DeferredDeleteIndexesEnabled);

            var keys = new List<(string KeyColumn, object? KeyValue)>();
            for (int i = 1; i <= 3; i++)
            {
                keys.Add(("name", $"u{i}"));
            }

            Assert.Equal(3, db.DeleteBatch(Table, keys));
            db.Flush();

            Assert.Equal(2, db.ExecuteQuery($"SELECT name FROM {Table}").Count);
            Assert.Single(db.ExecuteQuery($"SELECT name FROM {Table} WHERE name = @name", new Dictionary<string, object?> { ["@name"] = "u5" }));
            Assert.Empty(db.ExecuteQuery($"SELECT name FROM {Table} WHERE name = @name", new Dictionary<string, object?> { ["@name"] = "u2" }));
        }

        using var reopened = CreateDb();
        Assert.Equal(2, reopened.ExecuteQuery($"SELECT name FROM {Table}").Count);
        Assert.Single(reopened.ExecuteQuery($"SELECT name FROM {Table} WHERE name = @name", new Dictionary<string, object?> { ["@name"] = "u5" }));
    }

    [Fact]
    public void DeleteBatch_WithDeferredIndexesDisabled_StillDecodesAndDeletes()
    {
        // The eager half of the same pair: with EnableDeferredDeleteIndexes=false every delete removes its index
        // entries immediately, so the row payloads are read. A no-PK table takes the per-key locate path here.
        const string Table = "t";
        using (var db = CreateDb(eagerDeleteIndexes: true))
        {
            InsertFive(db, Table);
            var keys = new List<(string KeyColumn, object? KeyValue)> { ("name", "u4") };

            Assert.Equal(1, db.DeleteBatch(Table, keys));

            Assert.Equal(4, db.ExecuteQuery($"SELECT name FROM {Table}").Count);
            Assert.Empty(db.ExecuteQuery($"SELECT name FROM {Table} WHERE name = @name", new Dictionary<string, object?> { ["@name"] = "u4" }));
        }
    }

    [Fact]
    public void InsertBatchWithColumnOrder_WritesEveryColumnInTheGivenOrder()
    {
        // The dictionary-free overload's whole contract is the column ORDER: values arrive positionally, so a
        // mis-mapping would silently write a name into the score column. This reads every column back.
        const string Table = "t";
        using var db = CreateDb();
        db.ExecuteSQL($"CREATE TABLE {Table} (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");

        List<object[]> rows =
        [
            ["u1", "u1@test.com", 21, 1.5, "payload-1"],
            ["u2", "u2@test.com", 22, 2.5, "payload-2"],
        ];
        string[] columns = ["name", "email", "age", "score", "data"];

        var positions = db.InsertBatch(Table, rows, columns);
        Assert.Equal(2, positions.Length);
        db.Flush();

        var read = db.ExecuteQuery($"SELECT * FROM {Table} WHERE name = @name", new Dictionary<string, object?> { ["@name"] = "u2" });
        Assert.Single(read);
        Assert.Equal("u2@test.com", read[0]["email"]);
        Assert.Equal(22, Convert.ToInt32(read[0]["age"], CultureInfo.InvariantCulture));
        Assert.Equal(2.5, Convert.ToDouble(read[0]["score"], CultureInfo.InvariantCulture));
        Assert.Equal("payload-2", read[0]["data"]);
    }

    [Fact]
    public void InsertBatchWithColumnOrder_OnPkTable_WritesKeysAndSurvivesReopen()
    {
        const string Table = "docs";
        using (var db = CreateDb())
        {
            db.ExecuteSQL($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, name TEXT, score REAL)");

            List<object[]> rows =
            [
                [1, "u1", 1.0],
                [2, "u2", 2.0],
                [3, "u3", 3.0],
            ];
            string[] columns = ["id", "name", "score"];

            Assert.Equal(3, db.InsertBatch(Table, rows, columns).Length);
            db.Flush();
        }

        using var reopened = CreateDb();
        var rowsBack = reopened.ExecuteQuery($"SELECT name, score FROM {Table} ORDER BY id");
        Assert.Equal(3, rowsBack.Count);
        Assert.Equal("u3", rowsBack[2]["name"]);
        Assert.Equal(3.0, Convert.ToDouble(rowsBack[2]["score"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InsertBatchWithColumnOrder_OmittedColumns_MatchTheDictionaryOverloadExactly()
    {
        // Parity with the dictionary overload, asserted by *comparing the two overloads* rather than by assuming
        // what the default should be: the engine's default for an absent TEXT column is the empty string (not
        // NULL), and the array overload must reach that same value through the same defaulting pass.
        const string Table = "t";
        using var db = CreateDb();
        db.ExecuteSQL($"CREATE TABLE {Table} (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL)");

        string[] columns = ["name", "score"];

        List<object[]> arrayRows = [["array-row", 7.5]];
        Assert.Single(db.InsertBatch(Table, arrayRows, columns));

        Assert.Single(db.InsertBatch(Table, [new Dictionary<string, object>
        {
            ["name"] = "dict-row",
            ["score"] = 8.5,
        }]));

        db.Flush();

        var arrayBack = db.ExecuteQuery($"SELECT * FROM {Table} WHERE name = @name", new Dictionary<string, object?> { ["@name"] = "array-row" });
        var dictBack = db.ExecuteQuery($"SELECT * FROM {Table} WHERE name = @name", new Dictionary<string, object?> { ["@name"] = "dict-row" });
        Assert.Single(arrayBack);
        Assert.Single(dictBack);

        foreach (var column in new[] { "email", "age" })
        {
            Assert.Equal(
                Convert.ToString(dictBack[0][column], CultureInfo.InvariantCulture) ?? "<null>",
                Convert.ToString(arrayBack[0][column], CultureInfo.InvariantCulture) ?? "<null>");
        }

        Assert.Equal(7.5, Convert.ToDouble(arrayBack[0]["score"], CultureInfo.InvariantCulture));
    }

    private static Table GetTable(Database db, string table)
    {
        Assert.True(db.TryGetTable(table, out var probe));
        return Assert.IsType<Table>(probe);
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
    public void DeleteBatch_FixedWidthPkTable_UsesTheContiguousFastPath()
    {
        // The structured DELETE must reach B9 the same way the SQL path does. Measured (session 46): without the
        // contiguous attempt the structured entry point ran 0,38× the SQL batch path on the default posture,
        // because the SQL path delegates to this resolver and the typed loop does a PK search + row decode per
        // key. This test is the canary for that regression, in the shape FixedWidthBulkDeleteTests pins.
        const string Table = "docs";
        using var db = CreateDb();
        db.ExecuteSQL($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        db.InsertBatch(Table, [.. Enumerable.Range(1, 2000).Select(i => new Dictionary<string, object>
        {
            ["id"] = i,
            ["name"] = $"u{i}",
            ["score"] = i * 1.0,
        })]);
        db.Flush();

        Assert.True(db.TryGetTable(Table, out var probe));
        var table = Assert.IsType<Table>(probe);
        Assert.True(table.IsFixedWidthRecords);
        Assert.Equal(0, table.BulkContiguousDeleteBatches);

        var keys = new List<(string KeyColumn, object? KeyValue)>();
        for (int i = 1; i <= 1000; i++)
        {
            keys.Add(("id", i));
        }

        Assert.Equal(1000, db.DeleteBatch(Table, keys));

        Assert.Equal(1, table.BulkContiguousDeleteBatches);
        Assert.Empty(db.ExecuteQuery($"SELECT id FROM {Table} WHERE id = 500"));
        Assert.Single(db.ExecuteQuery($"SELECT id FROM {Table} WHERE id = 1500"));
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
