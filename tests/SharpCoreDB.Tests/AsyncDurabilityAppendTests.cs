// <copyright file="AsyncDurabilityAppendTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Plan §5 item 2 (A1) changed how the append path treats <c>DatabaseConfig.WalDurabilityMode</c>, and this file
/// pins where that change stands <b>after it was partially reversed on measurement (2026-09-16)</b>.
/// <para>
/// A1 (2026-09-15) made <c>Async</c> disable <c>FileOptions.WriteThrough</c> for table appends, so a caller who
/// chose asynchronous WAL writes stopped paying a synchronous write per record. On a workload that reads between
/// appends that cost 22–54 % on every phase — the deferred bytes have to be made visible to the next read — and
/// <c>Async</c> is set by six presets spanning opposite append regimes, so it cannot distinguish an append that
/// will never be read back from one that will. The coupling is therefore gone: table appends are written through
/// per record at either durability setting, and buffering is requested only by
/// <see cref="DatabaseConfig.EnableBufferedAppends"/> — which <c>BulkImport</c> and the write-once logging sink
/// now set explicitly, which is where the 9.4× that A1 measured belongs.
/// </para>
/// <para>
/// The tests below pin both halves of the resulting contract:
/// <list type="bullet">
/// <item><c>EnableBufferedAppends</c> buffers: a row is readable the moment it is inserted, it is durable after
/// <c>Flush()</c> or a commit, it survives a reopen, and the overflow arena — which is what a fixed-width
/// table's TEXT columns use — resolves through the same buffer.</item>
/// <item><c>Async</c> does <b>not</b> buffer (see <c>Async_Alone_DoesNotBufferTableAppends</c>) and neither does
/// the default <c>FullSync</c>: no database silently gains a durability window it did not ask for.</item>
/// </list>
/// </para>
/// <para>
/// The trade the opt-in buys, stated plainly: with buffering on, rows still in the buffer are lost by a process
/// crash as well as by power loss — they have not left managed memory. The buffer is bounded by
/// <c>AppendBufferFlushThresholdBytes</c> (1 MB) and <c>AppendBufferFlushIntervalMs</c> (10 ms), and is
/// flushed by <c>Database.Flush()</c>, a commit, <c>BeginTransaction</c> and every structural operation
/// (compaction, fixed-width migration, overflow-arena compaction, <c>DROP TABLE</c>, dispose). A test with a
/// real power cut is not automatable here, so what these tests pin is every boundary that keeps the window
/// bounded — the part that can be verified in-process.
/// </para>
/// </summary>
public sealed class AsyncDurabilityAppendTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly ICryptoService _crypto;
    private readonly string _dirPath;

    public AsyncDurabilityAppendTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _crypto = provider.GetRequiredService<ICryptoService>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_AsyncDurability_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The explicit buffering opt-in — <see cref="DatabaseConfig.EnableBufferedAppends"/> — with a threshold a
    /// handful of rows will not reach and a zero interval, so the only thing that flushes is an explicit
    /// boundary, which is what these tests assert about. The durability mode is deliberately left at the default
    /// (<c>FullSync</c>): since 2026-09-16 buffering comes from the opt-in alone, so these tests keep passing even
    /// if the removed coupling ever came back — which is exactly why
    /// <c>Async_Alone_DoesNotBufferTableAppends</c> exists to catch it.
    /// </summary>
    private static DatabaseConfig Buffered(bool atRest, bool fixedWidth = false) => new()
    {
        NoEncryptMode = !atRest,
        EnableAtRestRecordEncryption = atRest,
        EnableBufferedAppends = true,
        AppendBufferFlushThresholdBytes = 1024 * 1024,
        AppendBufferFlushIntervalMs = 0,
        FixedWidthRecordLayout = fixedWidth,
        AutoFixedWidthRecords = !fixedWidth,
    };

    /// <summary>The product default posture for the append path: synchronous, write-through per record.</summary>
    private static DatabaseConfig FullSyncRaw => new()
    {
        NoEncryptMode = true,
        WalDurabilityMode = DurabilityMode.FullSync,
    };

    private IDatabase Open(string dir, DatabaseConfig config) =>
        _factory.Create(dir, "pw", isReadOnly: false, config: config);

    /// <summary>
    /// A bare storage instance on the same configuration, so the append-buffer contract can be asserted at
    /// the level it is implemented at — this is the layer <c>AppendBytes</c> lives on, and the level the
    /// existing <c>BufferedAppendTests</c> use for the transaction-ordering case.
    /// </summary>
    private SharpCoreDB.Services.Storage NewStorage(DatabaseConfig config)
    {
        var key = AesGcmEncryption.DeriveKeyFromPassword(
            "pw", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], 1000);
        return new SharpCoreDB.Services.Storage(_crypto, key, config);
    }

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

    /// <summary>
    /// Single-row INSERT statements, one at a time. Deliberately NOT <c>ExecuteBatchSQL</c>: a batch opens
    /// a transaction and its commit flushes, which would hide whether the non-transactional path buffers.
    /// </summary>
    private static void InsertRows(IDatabase db, int from, int to)
    {
        for (int i = from; i <= to; i++)
        {
            db.ExecuteSQL(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO t VALUES ({0}, 'name{0}')", i));
        }
    }

    // ── The reversed contract: Async is durability, EnableBufferedAppends is buffering ───────────

    /// <summary>
    /// The new contract, asserted where the old one was: <c>WalDurabilityMode = Async</c> must <b>not</b> buffer
    /// table appends. It did for one day (A1, 2026-09-15), and on a workload that reads between appends that cost
    /// 22–54 % on every phase — UPDATE +54 %, DELETE +28 %, INSERT +22 % on the tuned <c>--pk</c> arm, one
    /// variable, same build. <c>Async</c> is a durability statement; buffering is the explicit opt-in asserted
    /// below. If this test fails, the coupling is back and every read-interleaved workload is paying for it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Async_Alone_DoesNotBufferTableAppends(bool atRest)
    {
        var file = Path.Combine(NewDir(atRest ? "async_nobuffer_atrest" : "async_nobuffer_raw"), "x.dat");
        var storage = NewStorage(new DatabaseConfig
        {
            NoEncryptMode = !atRest,
            EnableAtRestRecordEncryption = atRest,
            WalDurabilityMode = DurabilityMode.Async,
        });

        try
        {
            storage.AppendBytes(file, System.Text.Encoding.UTF8.GetBytes("row"));

            Assert.False(storage.HasBufferedAppends(file),
                "Async is a durability mode, not a buffering request: the row must be written through");
            Assert.True(new FileInfo(file).Length > 0, "the row must be on disk immediately under Async");
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// The other half, and the reason the split is safe: buffering is decided by
    /// <see cref="DatabaseConfig.EnableBufferedAppends"/> alone, at either durability setting — the two settings
    /// are now independent, so a bulk caller can buffer without weakening the WAL and a latency-sensitive caller
    /// can keep <c>FullSync</c> without losing the append speed <c>Async</c> used to bring with it.
    /// </summary>
    [Theory]
    [InlineData(DurabilityMode.Async)]
    [InlineData(DurabilityMode.FullSync)]
    public void EnableBufferedAppends_Buffers_AtEitherDurabilityMode(DurabilityMode mode)
    {
        var file = Path.Combine(NewDir($"optin_{mode}"), "x.dat");
        var storage = NewStorage(new DatabaseConfig
        {
            NoEncryptMode = true,
            WalDurabilityMode = mode,
            EnableBufferedAppends = true,
            AppendBufferFlushThresholdBytes = 1024 * 1024,
            AppendBufferFlushIntervalMs = 0,
        });

        try
        {
            storage.AppendBytes(file, System.Text.Encoding.UTF8.GetBytes("row"));

            Assert.True(storage.HasBufferedAppends(file),
                "EnableBufferedAppends is the buffering request and must be honoured at either durability mode");
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }
    }

    // ── Buffered appends: read-your-writes, and nothing on disk until a boundary ─────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_BuffersAppends_AndEveryRowIsReadableImmediately(bool atRest)
    {
        var dir = NewDir(atRest ? "readwrite_atrest" : "readwrite_raw");
        await using var db = Open(dir, Buffered(atRest));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");

        InsertRows(db, 1, 25);

        // Buffered: nothing has been written through yet …
        Assert.Equal(0, DatLength(dir));
        // … and yet every reader already sees all 25 rows (point lookup, full scan, count).
        Assert.Equal(25L, CountOf(db));
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 7"));
        Assert.Equal(25, db.ExecuteQuery("SELECT * FROM t").Count);
        Assert.Equal("name19", db.ExecuteQuery("SELECT * FROM t WHERE id = 19")[0].Values.Last());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_Flush_MakesBufferedRowsDurable_AcrossReopen(bool atRest)
    {
        var dir = NewDir(atRest ? "flush_atrest" : "flush_raw");
        await using (var db = Open(dir, Buffered(atRest)))
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            InsertRows(db, 1, 50);
            Assert.Equal(0, DatLength(dir));

            db.Flush();
            Assert.True(DatLength(dir) > 0, "Flush() must write the buffered rows through");
        }

        await using var reopened = Open(dir, Buffered(atRest));
        Assert.Equal(50L, CountOf(reopened));
        Assert.Single(reopened.ExecuteQuery("SELECT * FROM t WHERE id = 50"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_Commit_MakesBufferedRowsDurable_AcrossReopen(bool atRest)
    {
        var dir = NewDir(atRest ? "commit_atrest" : "commit_raw");
        await using (var db = Open(dir, Buffered(atRest)))
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");

            // A batch opens a transaction; its commit is a flush boundary for the append buffer, so rows
            // inserted inside it must not depend on the caller remembering to call Flush().
            db.ExecuteBatchSQL([.. Enumerable.Range(1, 40).Select(i =>
                $"INSERT INTO t VALUES ({i}, 'name{i}')")]);

            Assert.Equal(40L, CountOf(db));
        }

        await using var reopened = Open(dir, Buffered(atRest));
        Assert.Equal(40L, CountOf(reopened));
        Assert.Single(reopened.ExecuteQuery("SELECT * FROM t WHERE id = 40"));
    }

    /// <summary>
    /// The arena path, which is what A1 was opened for: a fixed-width table keeps TEXT values out of line,
    /// so every one of them is an overflow-arena block written through the same buffered append. A flush and
    /// a reopen must resolve every block — if the arena's appends did not ride the same buffer boundary, the
    /// fixed-width records would point at offsets that never landed.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_FixedWidthTextColumn_ArenaBlocksResolveAfterReopen(bool atRest)
    {
        var dir = NewDir(atRest ? "arena_atrest" : "arena_raw");
        await using (var db = Open(dir, Buffered(atRest, fixedWidth: true)))
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            InsertRows(db, 1, 30);

            // Read-your-writes through the arena before anything has been flushed.
            Assert.Equal("name27", db.ExecuteQuery("SELECT * FROM t WHERE id = 27")[0].Values.Last());
            db.Flush();
        }

        await using var reopened = Open(dir, Buffered(atRest, fixedWidth: true));
        Assert.Equal(30L, CountOf(reopened));
        Assert.Equal("name1", reopened.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0].Values.Last());
        Assert.Equal("name30", reopened.ExecuteQuery("SELECT * FROM t WHERE id = 30")[0].Values.Last());
        Assert.Empty(reopened.ExecuteQuery("SELECT * FROM t WHERE id = 31"));
    }

    /// <summary>
    /// <c>Rollback()</c> clears the append buffer, so rows buffered BEFORE the transaction must be flushed
    /// when it begins — otherwise a rollback silently discards rows whose positions the caller already
    /// holds. That ordering is what makes buffering safe beside the transaction buffer, and Async inherits
    /// it. Asserted at the storage level, which is where <c>AppendBytes</c> and the buffer live.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Async_BeginTransaction_FlushesBufferedAppends_SoRollbackKeepsThem(bool atRest)
    {
        var file = Path.Combine(NewDir(atRest ? "rollback_atrest" : "rollback_raw"), "x.dat");
        var storage = NewStorage(Buffered(atRest));
        try
        {
            long before = storage.AppendBytes(file, System.Text.Encoding.UTF8.GetBytes("before"));
            Assert.True(storage.HasBufferedAppends(file), "buffered appends must engage for an append made outside a transaction");

            storage.BeginTransaction();
            long inside = storage.AppendBytes(file, System.Text.Encoding.UTF8.GetBytes("inside"));
            storage.Rollback();

            Assert.False(storage.HasBufferedAppends(file));
            Assert.True(new FileInfo(file).Length > 0, "the pre-transaction row must have been flushed");
            Assert.Equal("before",
                System.Text.Encoding.UTF8.GetString(storage.ReadBytesFrom(file, before)!));
            Assert.Null(storage.ReadBytesFrom(file, inside));
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }
    }

    // ── Structural boundaries ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_DropTable_ThenRecreate_KeepsOnlyNewRows()
    {
        var dir = NewDir("droptable");
        await using var db = Open(dir, Buffered(atRest: false));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        InsertRows(db, 1, 20);
        Assert.Equal(20L, CountOf(db));

        db.ExecuteSQL("DROP TABLE t");
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'fresh')");
        db.Flush();

        Assert.Equal(1L, CountOf(db));
        Assert.Equal("fresh", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0].Values.Last());

        await using var reopened = Open(dir, Buffered(atRest: false));
        Assert.Equal(1L, CountOf(reopened));
    }

    // ── The default posture is untouched ────────────────────────────────────────────────────────

    /// <summary>
    /// The counterpart assertion, and the reason the change is safe: <c>FullSync</c> — the
    /// <see cref="DatabaseConfig"/> default — must still write every row through immediately. If this fails,
    /// the default durability posture moved, which is a promise change rather than a performance one.
    /// </summary>
    [Fact]
    public async Task FullSync_Default_IsNotBuffered()
    {
        var dir = NewDir("fullsync");
        await using var db = Open(dir, FullSyncRaw);
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");

        db.ExecuteSQL("INSERT INTO t VALUES (1, 'one')");

        Assert.True(DatLength(dir) > 0, "FullSync must write each row through, not buffer it");
        Assert.Equal(1L, CountOf(db));
    }
}
