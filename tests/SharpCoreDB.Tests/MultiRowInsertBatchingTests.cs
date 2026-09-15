// <copyright file="MultiRowInsertBatchingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

/// <summary>
/// The multi-row <c>INSERT … VALUES (…), (…)</c> batched lowering (plan §5, item 1).
/// <para>
/// The statement used to lower to one <see cref="Table.Insert"/> call per row — one standalone
/// write-through append each — and now routes to <see cref="Table.InsertBatch"/> when nothing needs
/// per-row semantics. These tests pin both halves of that contract: that the gate rejects exactly the
/// tables where the batch core does not implement the per-row behaviour (CHECK constraints, unique
/// secondary indexes), and that the batched path lands every row correctly — count, values, RETURNING and
/// <c>last_insert_rowid</c>.
/// </para>
/// <para>
/// Trigger gating has no test here on purpose: the parser's trigger registry is <em>static</em>
/// (<c>SqlParser.Triggers._triggers</c>) and this suite runs collections in parallel, so a trigger created
/// in one test fires for every other test in the process. The risk the gate exists to prevent is not worth
/// that blast radius. The trigger path keeps its own tests in <c>DdlProcedureViewTriggerTests</c>, and the
/// gate's trigger dimension (<c>SqlParser.HasTriggersFor</c>) is verified by inspection: it returns false
/// when the registry is empty, and true for a matching (table, event) pair.
/// </para>
/// </summary>
public sealed class MultiRowInsertBatchingTests : IDisposable
{
    /// <summary>
    /// Rows per statement the tests use to exercise the batched path — comfortably above the parser's floor,
    /// which is **2** (see the measurements recorded in <c>SqlParser.DML.ExecuteInsert</c>: batching wins
    /// from the smallest multi-row statement upward, because it removes one write-through table append per
    /// row against one transaction per statement).
    /// </summary>
    private const int BatchedShapeRows = 1200;

    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public MultiRowInsertBatchingTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_MultiRowInsert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private string NewDir(string name)
    {
        var dir = Path.Combine(_dirPath, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private IDatabase Open(string dir) =>
        _factory.Create(dir, "pw", isReadOnly: false, new DatabaseConfig { NoEncryptMode = true });

    private Table TableOf(IDatabase db)
    {
        Assert.True(db.TryGetTable("t", out var t));
        return Assert.IsType<Table>(t);
    }

    /// <summary>One <c>INSERT … VALUES</c> statement carrying <paramref name="rows"/> tuples.</summary>
    private static string MultiRowInsert(int rows, int firstId = 1)
    {
        var sb = new StringBuilder(rows * 24);
        sb.Append("INSERT INTO t (id, name, score) VALUES ");

        for (int i = 0; i < rows; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            int id = firstId + i;
            sb.Append('(').Append(id.ToString(CultureInfo.InvariantCulture))
              .Append(", 'user").Append(id.ToString(CultureInfo.InvariantCulture))
              .Append("', ").Append((id % 100).ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        return sb.ToString();
    }

    private static long CountOf(IDatabase db) =>
        Convert.ToInt64(db.ExecuteQuery("SELECT COUNT(*) AS n FROM t")[0].Values.First(), CultureInfo.InvariantCulture);

    // ── The gate ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanUseBatchedInsert_IsTrue_ForPlainTableWithNonUniqueIndex()
    {
        var db = Open(NewDir("gate_plain"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");
        db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)"); // non-unique: the batch core need not enforce it

        Assert.True(TableOf(db).CanUseBatchedInsert);
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void CanUseBatchedInsert_IsFalse_WithCheckConstraint()
    {
        var db = Open(NewDir("gate_check"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER CHECK (score < 1000))");

        // The batch core does not evaluate CHECK constraints, so the per-row loop must be kept — otherwise
        // the fast path would start accepting rows the per-row path rejects.
        Assert.False(TableOf(db).CanUseBatchedInsert);
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void CanUseBatchedInsert_IsFalse_WithUniqueSecondaryIndex()
    {
        var db = Open(NewDir("gate_unique"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");
        TableOf(db).CreateHashIndex("idx_t_name_unique", "name", isUnique: true);

        Assert.False(TableOf(db).CanUseBatchedInsert);
        (db as IDisposable)?.Dispose();
    }

    // ── The batched path ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BatchedMultiRowInsert_LandsEveryRowAndIsQueryable()
    {
        var db = Open(NewDir("batched_rows"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");
        db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");

        int rows = BatchedShapeRows + 200;
        db.ExecuteSQL(MultiRowInsert(rows));

        Assert.Equal(rows, CountOf(db));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = " + rows.ToString(CultureInfo.InvariantCulture)));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = " + (rows + 1).ToString(CultureInfo.InvariantCulture)));
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void BatchedMultiRowInsert_SurvivesReopen()
    {
        var dir = NewDir("batched_reopen");
        int rows = BatchedShapeRows + 100;

        var db = Open(dir);
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");
        db.ExecuteSQL(MultiRowInsert(rows));
        (db as IDisposable)?.Dispose();

        var reopened = Open(dir);
        Assert.Equal(rows, CountOf(reopened));
        Assert.Single(reopened.ExecuteQuery("SELECT * FROM t WHERE id = " + rows.ToString(CultureInfo.InvariantCulture)));
        (reopened as IDisposable)?.Dispose();
    }

    [Fact]
    public void BatchedMultiRowInsert_Returning_ReturnsEveryRow()
    {
        var db = Open(NewDir("batched_returning"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");

        int rows = BatchedShapeRows + 50;
        var returned = db.ExecuteQuery(MultiRowInsert(rows) + " RETURNING id, name");

        Assert.Equal(rows, returned.Count);
        Assert.Equal(rows, CountOf(db));
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void BatchedMultiRowInsert_SetsLastInsertRowIdFromPrimaryKey()
    {
        var db = Open(NewDir("batched_rowid"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");

        int rows = BatchedShapeRows + 7;
        db.ExecuteSQL(MultiRowInsert(rows, firstId: 5000));

        // The batch core records the last storage POSITION with the database; the parser re-points it at the
        // primary key, exactly as a loop of per-row Insert calls would have left it.
        Assert.Equal(5000 + rows - 1, db.GetLastInsertRowId());
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void BatchedMultiRowInsert_RejectsDuplicatePrimaryKeyWithinOneStatement()
    {
        var db = Open(NewDir("batched_duppk"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");

        var statement = MultiRowInsert(BatchedShapeRows) + ", (1, 'dup', 1)";
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL(statement));
        (db as IDisposable)?.Dispose();
    }

    // ── The gate protecting semantics ───────────────────────────────────────────────────────────

    [Fact]
    public void MultiRowInsert_WithCheckConstraint_StillRejectsViolations_AtBatchedSizes()
    {
        var db = Open(NewDir("check_enforced"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER CHECK (score < 50))");
        Assert.False(TableOf(db).CanUseBatchedInsert);

        // A statement at the batched-path size, carrying one violating row. The gate keeps the per-row loop,
        // so the CHECK constraint must still throw — the guarantee the row-count floor must not erode.
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL(MultiRowInsert(BatchedShapeRows + 10)));
        (db as IDisposable)?.Dispose();
    }

    // ── The floor ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultiRowInsert_FromTwoRowsUp_IsAtomicOnFailure()
    {
        var db = Open(NewDir("floor_two"));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, score INTEGER)");

        // Which path ran is observable through atomicity, without touching any global state (the profiler is
        // process-wide and another test class asserts on it): the batched core validates every row before
        // writing any, so a statement whose *second* row violates the primary key inserts NOTHING, whereas the
        // per-row loop would have left row 1 behind. The parser's floor is 2, so a two-row statement must take
        // the batched path — this test fails if the floor is ever raised again, which is exactly the mistake
        // it was written to prevent.
        Assert.ThrowsAny<Exception>(() =>
            db.ExecuteSQL("INSERT INTO t (id, name, score) VALUES (1, 'a', 1), (1, 'b', 2)"));

        Assert.Equal(0L, CountOf(db));
        (db as IDisposable)?.Dispose();
    }
}
