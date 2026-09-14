// <copyright file="BufferedAppendTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Plan §5 phase 3 item: <c>DatabaseConfig.EnableBufferedAppends</c> (opt-in, default OFF) lets the
/// single-row INSERT path buffer appends instead of opening a write-through FileStream PER ROW
/// (~512 µs → ~4.5 µs per 64-byte row for the write itself). These tests pin the contract that makes
/// that safe: a buffered row is visible to every reader the moment it is inserted (point lookup via
/// ReadBytesFrom, enumeration via ReadAllRecords), it is never lost or duplicated by a structural
/// operation (compaction, DROP TABLE, transaction rollback), and the DEFAULT configuration is
/// byte-for-byte unchanged (nothing is buffered at all).
/// </summary>
public sealed class BufferedAppendTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly ICryptoService _crypto;
    private readonly string _dirPath;

    public BufferedAppendTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _crypto = provider.GetRequiredService<ICryptoService>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_BufferedAppend_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Buffered appends with a threshold a handful of rows will not reach.</summary>
    private static DatabaseConfig Buffered(bool atRest) => new()
    {
        NoEncryptMode = !atRest,
        EnableAtRestRecordEncryption = atRest,
        EnableBufferedAppends = true,
        AppendBufferFlushThresholdBytes = 1024 * 1024,
        AppendBufferFlushIntervalMs = 0, // deterministic: only the byte threshold / explicit flush
    };

    private static DatabaseConfig Raw => new() { NoEncryptMode = true };

    private IDatabase Open(string dir, DatabaseConfig config) =>
        _factory.Create(dir, "pw", isReadOnly: false, config: config);

    private string NewDir(string name)
    {
        var dir = Path.Combine(_dirPath, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string DatPath(string dir, string table = "t") => Path.Combine(dir, table + ".dat");

    private static long DatLength(string dir, string table = "t") =>
        File.Exists(DatPath(dir, table)) ? new FileInfo(DatPath(dir, table)).Length : 0;

    private static long CountOf(IDatabase db) =>
        Convert.ToInt64(db.ExecuteQuery("SELECT COUNT(*) AS n FROM t")[0].Values.First());

    private static List<string> Inserts(int from, int to)
    {
        var stmts = new List<string>(to - from + 1);
        for (int i = from; i <= to; i++)
        {
            stmts.Add(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO t VALUES ({0}, 'name{0}')", i));
        }

        return stmts;
    }

    /// <summary>
    /// Single-row INSERT statements, one at a time. Deliberately NOT <c>ExecuteBatchSQL</c>: that opens a
    /// transaction, whose commit flushes everything — these tests must exercise the
    /// non-transactional path the buffered mode actually changes.
    /// </summary>
    private static void InsertRows(IDatabase db, int from, int to)
    {
        foreach (var statement in Inserts(from, to))
        {
            db.ExecuteSQL(statement);
        }
    }

    private SharpCoreDB.Services.Storage NewStorage(DatabaseConfig config)
    {
        var key = AesGcmEncryption.DeriveKeyFromPassword(
            "pw", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], 1000);
        return new SharpCoreDB.Services.Storage(_crypto, key, config);
    }

    // ── Default configuration: nothing is buffered (the opt-in guarantee) ───────────────────────

    /// <summary>
    /// The whole feature is opt-in: with a default <see cref="DatabaseConfig"/> the very first INSERT must
    /// already be on disk (a buffered row would leave the DDL-created empty file at length 0). Regression
    /// test for "the flip must not change anyone who did not ask for it".
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultConfig_DoesNotBuffer_FirstInsertIsAlreadyOnDisk(bool atRest)
    {
        var dir = NewDir(atRest ? "default_atrest" : "default_raw");
        var db = Open(dir, atRest
            ? new DatabaseConfig { NoEncryptMode = false, EnableAtRestRecordEncryption = true }
            : Raw);

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        Assert.Equal(0, DatLength(dir));

        db.ExecuteSQL("INSERT INTO t VALUES (1, 'one')");

        Assert.True(DatLength(dir) > 0, "default config must write each record through, not buffer it");
        (db as IDisposable)?.Dispose();
    }

    // ── Read-your-writes before any flush (matrix rows 1-2) ─────────────────────────────────────

    /// <summary>
    /// The rows exist only in memory when these reads run: the point lookup goes through the PK index →
    /// engine.Read → <c>ReadBytesFrom</c> (overlay), and COUNT(*) goes through the whole-file scan, which
    /// flushes first. Both must return the rows, while the data file still shows that nothing was written
    /// per row.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferedRows_AreVisibleToPointLookupAndCount_BeforeAnyFlush(bool atRest)
    {
        var dir = NewDir(atRest ? "buffered_atrest" : "buffered_raw");
        var db = Open(dir, Buffered(atRest));

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        InsertRows(db, 1, 200);

        Assert.True(DatLength(dir) < 8 * 1024,
            $"buffered mode wrote {DatLength(dir)} bytes for 200 rows");

        // Point lookup (overlay) — without it the read would land past the end of the file.
        var row = db.ExecuteQuery("SELECT name FROM t WHERE id = 137");
        Assert.Single(row);
        Assert.Equal("name137", row[0]["name"]);

        Assert.Equal(200L, CountOf(db));
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// A flush (here <c>Database.Flush()</c>, which reaches <c>Storage.FlushBufferedAppends</c> through the
    /// engine) must make every buffered row durable and re-readable after a reopen.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flush_ThenReopen_KeepsEveryBufferedRow(bool atRest)
    {
        var dir = NewDir(atRest ? "flush_atrest" : "flush_raw");
        var db = Open(dir, Buffered(atRest));

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        InsertRows(db, 1, 150);
        db.Flush();

        Assert.True(DatLength(dir) > 0, "flush must put the buffered rows on disk");
        Assert.Equal(150L, CountOf(db));
        (db as IDisposable)?.Dispose();

        db = Open(dir, Buffered(atRest));
        Assert.Equal(150L, CountOf(db));
        Assert.Equal("name75", db.ExecuteQuery("SELECT name FROM t WHERE id = 75")[0]["name"]);
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Threshold behaviour: with a small byte threshold the buffer flushes itself, so the file grows
    /// without any explicit flush, and every row stays readable.
    /// </summary>
    [Fact]
    public void ByteThreshold_FlushesWithoutAnExplicitFlush()
    {
        var dir = NewDir("threshold");
        var db = Open(dir, new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableBufferedAppends = true,
            AppendBufferFlushThresholdBytes = 2048,
            AppendBufferFlushIntervalMs = 0,
        });

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        InsertRows(db, 1, 300);

        Assert.True(DatLength(dir) >= 2048, $"threshold did not flush: {DatLength(dir)} bytes on disk");
        Assert.Equal(300L, CountOf(db));
        (db as IDisposable)?.Dispose();
    }

    // ── Storage-level overlay (matrix rows 1-2, incl. a file that does not exist yet) ───────────

    /// <summary>
    /// Direct storage contract, with NOTHING flushed: the file may not even exist yet, so both readers must
    /// be served entirely from the buffer — the point lookup through the position index and the enumeration
    /// through the buffered tail. After the flush the same reads must come off disk, which also proves the
    /// flush wrote the header (encrypted) before the records.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Storage_OverlaysBufferedAppends_ForPointAndFullReads(bool atRest)
    {
        var file = Path.Combine(NewDir(atRest ? "storage_atrest" : "storage_raw"), "x.dat");
        var storage = NewStorage(Buffered(atRest));
        try
        {
            var first = System.Text.Encoding.UTF8.GetBytes("first-record");
            var second = System.Text.Encoding.UTF8.GetBytes("second-record");

            long p1 = storage.AppendBytes(file, first);
            long p2 = storage.AppendBytes(file, second);

            Assert.True(storage.HasBufferedAppends(file));
            Assert.False(File.Exists(file));

            Assert.Equal("first-record",
                System.Text.Encoding.UTF8.GetString(storage.ReadBytesFrom(file, p1)!));
            Assert.Equal("second-record",
                System.Text.Encoding.UTF8.GetString(storage.ReadBytesFrom(file, p2)!));

            var enumerated = storage.ReadAllRecords(file).ToList();
            Assert.Equal(new[] { p1, p2 }, enumerated.Select(static record => record.RecordOffset));
            Assert.Equal("first-record", System.Text.Encoding.UTF8.GetString(enumerated[0].Data));
            Assert.Equal("second-record", System.Text.Encoding.UTF8.GetString(enumerated[1].Data));

            storage.FlushPendingAppends();

            Assert.False(storage.HasBufferedAppends(file));
            Assert.True(File.Exists(file));
            Assert.Equal(atRest, storage.AreRecordsEncrypted(file));
            Assert.Equal("first-record",
                System.Text.Encoding.UTF8.GetString(storage.ReadBytesFrom(file, p1)!));
            Assert.Equal(2, storage.ReadAllRecords(file).Count());
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }
    }

    // ── Transaction interplay: a rollback must not discard pre-transaction rows (matrix row 8) ──

    /// <summary>
    /// <c>Rollback()</c> clears the append buffer, so rows buffered BEFORE the transaction must be flushed
    /// when it begins — otherwise the rollback silently discards rows the caller already has positions
    /// for. This is the hook that makes buffered mode safe to combine with transactions.
    /// </summary>
    [Fact]
    public void BeginTransaction_FlushesBufferedAppends_SoRollbackKeepsThem()
    {
        var file = Path.Combine(NewDir("rollback"), "x.dat");
        var storage = NewStorage(Buffered(atRest: true));
        try
        {
            long committed = storage.AppendBytes(file, System.Text.Encoding.UTF8.GetBytes("before"));

            storage.BeginTransaction();
            long rolledBack = storage.AppendBytes(file, System.Text.Encoding.UTF8.GetBytes("inside"));
            storage.Rollback();

            Assert.False(storage.HasBufferedAppends(file));
            Assert.True(new FileInfo(file).Length > 0, "the pre-transaction row must have been flushed");
            Assert.Equal("before",
                System.Text.Encoding.UTF8.GetString(storage.ReadBytesFrom(file, committed)!));
            Assert.Null(storage.ReadBytesFrom(file, rolledBack));
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }
    }

    // ── Structural operations: no loss, no duplicates, no resurrected files (rows 6, 9) ─────────

    /// <summary>
    /// Compaction reads the data file and then REPLACES it. Pending buffered rows must be on disk before
    /// that — flushing afterwards would append the same rows a second time into the rewritten file.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compaction_WithPendingBufferedRows_KeepsEveryRowExactlyOnce(bool atRest)
    {
        var dir = NewDir(atRest ? "compact_atrest" : "compact_raw");
        var db = Open(dir, Buffered(atRest));

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        InsertRows(db, 1, 120);
        Assert.Equal(0, DatLength(dir)); // still in memory

        Assert.True(db.TryGetTable("t", out var table));
        var concrete = Assert.IsType<SharpCoreDB.DataStructures.Table>(table);
        concrete.CompactStorage();

        Assert.True(DatLength(dir) > 0, "compaction must flush pending appends before rewriting");
        Assert.Equal(120L, CountOf(db));
        (db as IDisposable)?.Dispose();

        db = Open(dir, Buffered(atRest));
        Assert.Equal(120L, CountOf(db)); // no duplicates, no loss
        (db as IDisposable)?.Dispose();
    }

    /// <summary>
    /// DROP TABLE deletes the data file. A row that is still buffered would be flushed AFTER the drop and
    /// RECREATE the file the user just removed, so the drop path flushes first.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropTable_WithPendingBufferedRows_DoesNotRecreateTheDataFile(bool atRest)
    {
        var dir = NewDir(atRest ? "drop_atrest" : "drop_raw");
        var db = Open(dir, Buffered(atRest));

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        InsertRows(db, 1, 50);
        Assert.Equal(0, DatLength(dir));

        db.ExecuteSQL("DROP TABLE t");
        db.Flush();
        (db as IDisposable)?.Dispose();

        Assert.False(File.Exists(DatPath(dir)),
            "a pending buffered append must not recreate a dropped table's data file");
    }
}
