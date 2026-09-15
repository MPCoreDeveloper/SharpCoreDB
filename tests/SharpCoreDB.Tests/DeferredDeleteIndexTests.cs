// <copyright file="DeferredDeleteIndexTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Plan §7 (Phase 5) — <c>DatabaseConfig.EnableDeferredDeleteIndexes</c>, which is ON by default (the
/// explicit opt-out is covered below).
/// A deferred DELETE writes the durable tombstone and defers index maintenance: the PK B-tree removal
/// runs in one bulk pass once <c>DeferredDeleteIndexThreshold</c> is crossed, or for free on reopen, so
/// between those points the B-tree holds entries whose target is a tombstone ("stale"). These tests pin
/// the contract that makes that safe: rows are gone to every reader, a deleted primary key can be
/// re-INSERTed through BOTH the single-row and the batch path (uniqueness checks are liveness-aware and
/// never read the stale entry as live), nothing resurrects across a reopen, and the explicit opt-out
/// restores the eager path.
/// </summary>
public sealed class DeferredDeleteIndexTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public DeferredDeleteIndexTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_DeferredDelete_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static DatabaseConfig Deferred(bool atRest) => new()
    {
        NoEncryptMode = !atRest,
        EnableAtRestRecordEncryption = atRest,
        EnableDeferredDeleteIndexes = true,
    };

    private IDatabase Open(string dir, DatabaseConfig config) =>
        _factory.Create(dir, "pw", isReadOnly: false, config: config);

    private string NewDir(string name)
    {
        var dir = Path.Combine(_dirPath, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private IDatabase OpenAndSeed(string dir, DatabaseConfig config, int rows)
    {
        var db = Open(dir, config);
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
        db.ExecuteBatchSQL(Inserts(1, rows));
        Assert.Equal(rows, CountOf(db));
        return db;
    }

    private static long CountOf(IDatabase db) =>
        Convert.ToInt64(db.ExecuteQuery("SELECT COUNT(*) AS n FROM t")[0].Values.First());

    private static List<string> Deletes(IEnumerable<int> ids) =>
        ids.Select(i => $"DELETE FROM t WHERE id = {i}").ToList();

    private static List<string> Inserts(int from, int to)
    {
        var stmts = new List<string>(to - from + 1);
        for (int i = from; i <= to; i++)
        {
            stmts.Add($"INSERT INTO t (id, name) VALUES ({i}, 'user{i}')");
        }
        return stmts;
    }

    // ── Deferred batch DELETE: correct to every reader ──────────────────────────────────────────

    /// <summary>
    /// A deferred DELETE batch (descending key order, so the contiguous fixed-width fast path cannot
    /// short-circuit it) must leave the PK B-tree, the hash index and the data file consistent: the
    /// deleted rows are gone from a full scan, a PK lookup and an indexed lookup.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredDelete_DescendingBatch_RemovesRowsFromEveryReader(bool atRest)
    {
        var dir = NewDir(atRest ? "desc_atrest" : "desc_raw");
        var db = OpenAndSeed(dir, Deferred(atRest), rows: 50);

        // Descending (non-ascending) → falls through the contiguous fast path into the deferred core.
        db.ExecuteBatchSQL(Deletes(Enumerable.Range(31, 20).Reverse()));

        Assert.Equal(30L, CountOf(db));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 50"));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 31"));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE name = 'user50'"));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 30"));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE name = 'user30'"));
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// A deleted primary key must be re-INSERTable in a later statement. The stale B-tree entry is not
    /// reconciled here — reconciling at <c>Flush()</c> was measured slower than the per-key maintenance
    /// the deferral skips — so this works because the uniqueness check is liveness-aware: it reads the
    /// stored position and treats a tombstone as free.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredDelete_ReinsertDeletedPrimaryKey_Succeeds(bool atRest)
    {
        var dir = NewDir(atRest ? "reinsert_atrest" : "reinsert_raw");
        var db = OpenAndSeed(dir, Deferred(atRest), rows: 10);

        db.ExecuteBatchSQL(Deletes(new[] { 5, 6, 7 }.Reverse()));

        // Re-INSERT the same PKs (through a separate batch) must not trip a stale uniqueness check.
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (5, 'user5-again')");
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (6, 'user6-again')");

        Assert.Equal(9L, CountOf(db)); // 10 - 3 + 2
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 5"));
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// The batch-INSERT path must apply the same liveness-aware uniqueness check as the single-row path.
    /// Regression: <c>ValidateBatchPrimaryKeysUpfront</c> read the PK B-tree directly and treated a
    /// tombstoned entry as a live key, so a delete followed by a <em>batch</em> re-INSERT of the same
    /// primary key threw a false duplicate-key violation. Surfaced by
    /// <c>SharpCoreDB.CQRS.Tests.SharpCoreDbOutboxStoreTests.RequeueDeadLetterAsync_MovesMessageFromDeadLetterToOutbox</c>.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredDelete_ReinsertDeletedPrimaryKey_InBatch_Succeeds(bool atRest)
    {
        var dir = NewDir(atRest ? "reinsert_batch_atrest" : "reinsert_batch_raw");
        var db = OpenAndSeed(dir, Deferred(atRest), rows: 10);

        db.ExecuteBatchSQL(Deletes(new[] { 5, 6, 7 }.Reverse()));

        // The outbox-requeue shape: one batch carrying the re-INSERT of a just-deleted key. ExecuteBatchSQL
        // routes INSERTs of any row count to InsertBatch → ValidateBatchPrimaryKeysUpfront.
        db.ExecuteBatchSQL(new List<string>
        {
            "INSERT INTO t (id, name) VALUES (5, 'user5-again')",
            "INSERT INTO t (id, name) VALUES (6, 'user6-again')",
        });

        Assert.Equal(9L, CountOf(db)); // 10 - 3 + 2
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 5"));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 6"));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 7"));
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Same, on the configuration a caller gets by default: deferred delete maintenance is ON by default
    /// (<c>DatabaseConfig.EnableDeferredDeleteIndexes = true</c>), so the false duplicate-key conflict was
    /// reachable without opting into anything.
    /// </summary>
    [Fact]
    public void DeferredDelete_DefaultConfig_ReinsertInBatch_Succeeds()
    {
        var dir = NewDir("default_reinsert_batch");
        var db = OpenAndSeed(dir, new DatabaseConfig { NoEncryptMode = true }, rows: 10);

        db.ExecuteBatchSQL(Deletes(new[] { 5 }.Reverse()));
        db.ExecuteBatchSQL(new List<string> { "INSERT INTO t (id, name) VALUES (5, 'user5-again')" });

        Assert.Equal(10L, CountOf(db));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 5"));
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Deferred-deleted rows must stay deleted across a close/reopen: the tombstone is durable, and
    /// the reopen index rebuild skips tombstoned records, so nothing resurrects.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredDelete_Reopen_DoesNotResurrectRows(bool atRest)
    {
        var dir = NewDir(atRest ? "reopen_atrest" : "reopen_raw");
        var db = OpenAndSeed(dir, Deferred(atRest), rows: 40);

        db.ExecuteBatchSQL(Deletes(Enumerable.Range(21, 20).Reverse()));
        Assert.Equal(20L, CountOf(db));
        db.Flush();
        (db as IDisposable)?.Dispose();

        db = Open(dir, Deferred(atRest));
        Assert.Equal(20L, CountOf(db));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 40"));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
        (db as IDisposable)?.Dispose();
    }

    // ── Default = deferred; explicit opt-out = eager ────────────────────────────────────────────

    /// <summary>
    /// The default configuration (<c>EnableDeferredDeleteIndexes = true</c>) defers hash-index
    /// maintenance, leaving tombstone-tolerant stale entries in the index — yet every reader still
    /// sees the deleted rows as gone (the position reads null).
    /// </summary>
    [Fact]
    public void DefaultConfig_DefersHashIndexMaintenance_ButReadersStayCorrect()
    {
        var dir = NewDir("default_defer");
        var db = OpenAndSeed(dir, new DatabaseConfig { NoEncryptMode = true }, rows: 25);
        Assert.True(db.TryGetTable("t", out var it));
        var table = Assert.IsType<SharpCoreDB.DataStructures.Table>(it);
        table.EnsureIndexLoaded("name");
        Assert.Equal(25, table.GetHashIndexStatistics("name")!.Value.TotalRows);

        db.ExecuteBatchSQL(Deletes(Enumerable.Range(1, 10).Reverse()));

        // Deferred: the index keeps the stale entries (bounded, reconciled on reopen) …
        Assert.Equal(25, table.GetHashIndexStatistics("name")!.Value.TotalRows);
        // … but every reader still sees the deletes as gone.
        Assert.Equal(15L, CountOf(db));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 10"));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE name = 'user1'"));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 11"));
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Opting out (<c>EnableDeferredDeleteIndexes = false</c>) restores the eager path: every DELETE
    /// removes its index entries immediately, so the hash index is exact between deletes.
    /// </summary>
    [Fact]
    public void OptOut_ExplicitFalse_KeepsHashIndexExactAfterDelete()
    {
        var dir = NewDir("optout");
        var db = OpenAndSeed(dir, new DatabaseConfig { NoEncryptMode = true, EnableDeferredDeleteIndexes = false }, rows: 25);
        Assert.True(db.TryGetTable("t", out var it));
        var table = Assert.IsType<SharpCoreDB.DataStructures.Table>(it);
        table.EnsureIndexLoaded("name");

        db.ExecuteBatchSQL(Deletes(Enumerable.Range(1, 10).Reverse()));

        Assert.Equal(15, table.GetHashIndexStatistics("name")!.Value.TotalRows); // eager: removed immediately
        Assert.Equal(15L, CountOf(db));
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// A deferred DELETE inside a transaction cannot rebuild the PK B-tree mid-transaction (its
    /// tombstones are still buffered), and the rebuild is a full O(n) pass — so it is deliberately NOT
    /// run at every <c>Flush()</c>. Measured: reconciling at Flush cost more than the per-key
    /// maintenance the deferral skipped (random-key DELETE 222,752 → 70,248 ops/s). The reconcile runs
    /// at the next <em>non-transactional</em> delete once the threshold is crossed, and for free at
    /// reopen.
    /// </summary>
    [Fact]
    public void DeferredDeletes_Reconcile_AtNextNonTransactionalDelete()
    {
        var dir = NewDir("threshold_reconcile");
        var db = OpenAndSeed(dir, new DatabaseConfig
        {
            NoEncryptMode = true,
            DeferredDeleteIndexThreshold = 10, // small so the test can cross it
        }, rows: 100);

        Assert.True(db.TryGetTable("t", out var it));
        var table = Assert.IsType<SharpCoreDB.DataStructures.Table>(it);
        Assert.Equal(0, table.PendingDeferredDeleteCount);

        // One transactional batch of 50 deletes > the threshold: deferred, nothing reconciled.
        db.ExecuteBatchSQL(Deletes(Enumerable.Range(1, 50).Reverse()));
        Assert.Equal(50, table.PendingDeferredDeleteCount);

        // Flush is a committed-data boundary, but the O(n) rebuild is deliberately not run there.
        db.Flush();
        Assert.Equal(50, table.PendingDeferredDeleteCount);

        // A non-transactional delete crosses the threshold and reconciles the stale entries.
        db.ExecuteSQL("DELETE FROM t WHERE id = 51");
        Assert.Equal(0, table.PendingDeferredDeleteCount);

        Assert.Equal(49L, CountOf(db));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 50"));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 52"));
        (db as IDisposable)?.Dispose();
    }
}
