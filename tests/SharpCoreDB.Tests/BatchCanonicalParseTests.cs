// <copyright file="BatchCanonicalParseTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Phase-2 canonical batch-DML fast parse: pin the observable behaviour of
/// <c>ExecuteBatchSQL</c> for canonical single-row UPDATE/DELETE statements AND for tricky
/// non-canonical shapes that must fall back to the general regex path (embedded commas,
/// keywords inside string literals, multi-column SET, non-= operators, whitespace in literals).
/// Both routes must produce identical results.
/// <para>
/// Session 76 (plan §5 item 2) added the third route to this file's subject and it needs the same
/// treatment: a canonical statement's WHERE key is now resolved by the classifier while it is still spans,
/// and the UPDATE core skips the predicate parse when it is handed that key. Because two routes producing
/// identical results cannot be told apart by results, the routing itself is pinned through
/// <see cref="Table.StructuredUpdateOperations"/> (the counter <c>Table</c> publishes for exactly this, like
/// <c>BulkContiguousUpdateBatches</c>): a canonical batch must move it, a shape the text resolver owns must
/// not, and a structured key with no index to serve it must still reach the <c>SelectInternal</c> fallback
/// and update the row.
/// </para>
/// </summary>
public sealed class BatchCanonicalParseTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public BatchCanonicalParseTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_BatchCanonical_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private static string? Scalar(IDatabase db, string sql, string column)
    {
        var rows = db.ExecuteQuery(sql);
        if (rows.Count == 0)
        {
            return null;
        }

        foreach (var key in rows[0].Keys)
        {
            if (key.Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                return rows[0][key]?.ToString();
            }
        }

        return null;
    }

    private static double? Num(IDatabase db, string sql, string column)
    {
        var rows = db.ExecuteQuery(sql);
        if (rows.Count == 0)
        {
            return null;
        }

        foreach (var key in rows[0].Keys)
        {
            if (key.Equals(column, StringComparison.OrdinalIgnoreCase) && rows[0][key] is not null)
            {
                return Convert.ToDouble(rows[0][key], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    [Fact]
    public void CanonicalSingleSetUpdate_UpdatesRow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, email TEXT, age INTEGER, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User1', 'u1@x', 30, 0.1)");

            db.ExecuteBatchSQL(["UPDATE t SET score = 99.5 WHERE name = 'User1'"]);

            Assert.Equal(99.5, Num(db, "SELECT score FROM t WHERE name = 'User1'", "score"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SetValue_WithCommaAndWhereKeywordInsideString_IsHandled()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, data TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a,b')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'c')");

            // data value contains a comma and the word WHERE inside the string literal.
            db.ExecuteBatchSQL(["UPDATE t SET data = 'x, WHERE y' WHERE id = 1"]);

            Assert.Equal("x, WHERE y", Scalar(db, "SELECT data FROM t WHERE id = 1", "data"));
            Assert.Equal("c", Scalar(db, "SELECT data FROM t WHERE id = 2", "data"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void MultiColumnSet_FallsBackAndUpdatesAll()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, a TEXT, b INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'x', 10)");

            db.ExecuteBatchSQL(["UPDATE t SET a = 'y', b = 42 WHERE id = 1"]);

            Assert.Equal("y", Scalar(db, "SELECT a FROM t WHERE id = 1", "a"));
            Assert.Equal(42, Num(db, "SELECT b FROM t WHERE id = 1", "b"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void CanonicalDelete_RemovesRow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, age INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES ('a', 1)");
            db.ExecuteSQL("INSERT INTO t VALUES ('b', 2)");

            db.ExecuteBatchSQL(["DELETE FROM t WHERE name = 'a'"]);

            Assert.Null(Scalar(db, "SELECT name FROM t WHERE name = 'a'", "name"));
            Assert.Equal("b", Scalar(db, "SELECT name FROM t WHERE name = 'b'", "name"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void WhereLiteral_WithSpaces_IsHandled()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User 1', 1.0)");

            db.ExecuteBatchSQL(["UPDATE t SET score = 2.0 WHERE name = 'User 1'"]);
            Assert.Equal(2.0, Num(db, "SELECT score FROM t WHERE name = 'User 1'", "score"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    private static Table TableOf(IDatabase db, string tableName)
    {
        Assert.True(db.TryGetTable(tableName, out var t));
        return Assert.IsType<Table>(t);
    }

    [Fact]
    public void CanonicalUpdateBatch_ResolvesKeyInClassifier_UpdatesEveryRow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a', 0.1)");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b', 0.2)");
            db.ExecuteSQL("INSERT INTO t VALUES (3, 'c', 0.3)");

            var table = TableOf(db, "t");
            long before = table.StructuredUpdateOperations;

            // The predicate targets a NON-PK column on purpose: a `pk = <literal>` batch can be claimed by
            // TryBulkUpdateContiguousFixedWidth (which rejects any non-PK key at its PK check), and this test
            // is about the classifier/core route, not about which same-batch fast path wins.
            db.ExecuteBatchSQL(
            [
                "UPDATE t SET score = 1.1 WHERE name = 'a'",
                "UPDATE t SET score = 2.2 WHERE name = 'b'",
                "UPDATE t SET score = 3.3 WHERE name = 'c'",
            ]);

            Assert.Equal(before + 3, table.StructuredUpdateOperations);
            Assert.Equal(1.1, Num(db, "SELECT score FROM t WHERE id = 1", "score"));
            Assert.Equal(2.2, Num(db, "SELECT score FROM t WHERE id = 2", "score"));
            Assert.Equal(3.3, Num(db, "SELECT score FROM t WHERE id = 3", "score"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void NonCanonicalPredicates_KeepTextRoute_UpdateEveryRow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");

            var table = TableOf(db, "t");
            long before = table.StructuredUpdateOperations;

            // Neither shape may be claimed as a structured key: the quoted column is one SQL's resolver
            // normalizes away (so a structured key would carry a name the table layer never matches), and the
            // compound predicate would hand half an expression over as a value.
            db.ExecuteBatchSQL(
            [
                "UPDATE t SET name = 'z1' WHERE \"id\" = 1",
                "UPDATE t SET name = 'z2' WHERE id = 2 AND name = 'b'",
            ]);

            Assert.Equal(before, table.StructuredUpdateOperations);
            Assert.Equal("z1", Scalar(db, "SELECT name FROM t WHERE id = 1", "name"));
            Assert.Equal("z2", Scalar(db, "SELECT name FROM t WHERE id = 2", "name"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void StructuredKey_OnNonIndexedColumnOnPageBased_FallsBackAndUpdatesRow()
    {
        // PageBased never auto-creates hash indexes (Table.AutoHashIndexesEnabled requires Columnar), so a
        // canonical predicate on a non-PK column resolves its key in the classifier and then finds no index to
        // serve it. The core's SelectInternal fallback then runs on the predicate text rebuilt from the
        // classifier's RAW literal — the reason the raw literal is carried next to the value at all.
        var db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig { StorageEngineType = StorageEngineType.PageBased });
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'p', 0.5)");

            var table = TableOf(db, "t");
            long before = table.StructuredUpdateOperations;

            db.ExecuteBatchSQL(["UPDATE t SET score = 9.5 WHERE name = 'p'"]);

            Assert.Equal(before + 1, table.StructuredUpdateOperations);
            Assert.Equal(9.5, Num(db, "SELECT score FROM t WHERE id = 1", "score"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UnquoteSqlLiteral_AlreadyNormalizedLiteral_ReturnsTheCallerInstance()
    {
        // Session 79 (plan §5.5 (ii) residual): the classifier hands the UPDATE core the normalized key
        // value, and the shared normalizer used to build a second, identical string for every numeric
        // predicate literal ("id = 5000" → "5000") — measured at 32 B/statement on the fair-PK arm. The
        // contract stays content equality; this pins the removed allocation (the identity) as well as the
        // normalization the shapes that DO need work must keep.
        const string alreadyNormalized = "5000";
        Assert.Same(alreadyNormalized, Table.UnquoteSqlLiteral(alreadyNormalized));

        Assert.Equal("5000", Table.UnquoteSqlLiteral(" 5000 "));
        Assert.Equal("User9", Table.UnquoteSqlLiteral("'User9'"));
        Assert.Equal("User9", Table.UnquoteSqlLiteral("\"User9\""));
        Assert.NotSame("5000", Table.UnquoteSqlLiteral("5000 "));
    }

    [Fact]
    public void CanonicalNumericKeyBatch_UpdatesEveryRow_AfterTheNormalizerChange()
    {
        // The arm whose second copy the normalizer no longer allocates: a numeric key on the PK
        // (`WHERE id = <n>`) must still resolve to the right row on both engines' structured route.
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a', 0.1)");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b', 0.2)");

            var table = TableOf(db, "t");
            long structuredBefore = table.StructuredUpdateOperations;
            long contiguousBefore = table.BulkContiguousUpdateBatches;

            db.ExecuteBatchSQL(
            [
                "UPDATE t SET score = 11.1 WHERE id = 1",
                "UPDATE t SET score = 22.2 WHERE id = 2",
            ]);

            // Values first — the routing counters are diagnostic (see below) and the values are the
            // contract the normalizer change must not touch.
            Assert.Equal(11.1, Num(db, "SELECT score FROM t WHERE id = 1", "score"));
            Assert.Equal(22.2, Num(db, "SELECT score FROM t WHERE id = 2", "score"));

            // A canonical PK-keyed batch is claimed by *a* structured route, and the two routes publish
            // counters in different units: the per-op loop publishes StructuredUpdateOperations (one per
            // operation, and only from its own branch) while the contiguous pass publishes
            // BulkContiguousUpdateBatches (one per batch) and returns before the per-op publish. So the
            // assertion is "some structured route claimed this batch", not a sum — summing them would add
            // operations to batches. The PageBased fact above pins the per-op counter on its own, because
            // PageBased cannot take the contiguous pass.
            long structuredDelta = table.StructuredUpdateOperations - structuredBefore;
            long contiguousDelta = table.BulkContiguousUpdateBatches - contiguousBefore;
            Assert.True(
                structuredDelta > 0 || contiguousDelta > 0,
                $"expected a structured route to claim the batch (structured ops={structuredDelta}, contiguous batches={contiguousDelta})");
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}

