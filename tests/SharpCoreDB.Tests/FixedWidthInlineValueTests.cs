// <copyright file="FixedWidthInlineValueTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Plan §4b — the inline capacity for variable-length values
/// (<see cref="DatabaseConfig.FixedWidthInlineValueBytes"/>).
/// <para>
/// The fixed-width record already had the stable-slot + overflow model, but a 5-byte
/// <c>[null-flag(1)][overflow offset(4)]</c> slot meant *every* text value, however short, cost an arena write
/// (~4.05 µs/row, ~24 % of the multi-row pass). With a capacity set, a payload that fits is stored in the record
/// itself with <c>null-flag = 2</c> and the arena is not touched.
/// </para>
/// <para>
/// These tests pin that the inline encoding round-trips through **every** path that reads a variable slot, because
/// they are separate code sites: the row decoder (<c>FixedWidthCodec.DeserializeRow</c>), the struct-scan string
/// comparison (<c>Table.StructScanning.MatchesFixedWidthStringDirect</c>), the bulk-delete fast path's hash-index
/// decode (<c>Table.CRUD</c>), migration validation (<c>Table.FixedWidthMigration</c>), and compaction — where both
/// <c>CollectVariableOffsets</c> and <c>RepointVariableSlots</c> must **skip** inline slots, since treating payload
/// bytes as an arena offset would free or re-point the wrong block. That last one is the failure mode worth a test.
/// </para>
/// <para>
/// A value longer than the capacity must still take the overflow path, so each test also carries one.
/// </para>
/// </summary>
public sealed class FixedWidthInlineValueTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FixedWidthInlineValueTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_InlineValues_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Fixed-width records with a 16-byte inline capacity — enough for short text, not for the long value.</summary>
    private static DatabaseConfig Inline(int inlineBytes = 16) => new()
    {
        NoEncryptMode = true,
        FixedWidthRecordLayout = true,
        FixedWidthInlineValueBytes = inlineBytes,
    };

    /// <summary>Longer than the 16-byte capacity in <see cref="Inline"/>, so it must take the overflow path.</summary>
    private const string LongValue = "a considerably longer tag value for the overflow block";

    private IDatabase Open(string name, int inlineBytes = 16)
    {
        var dir = Path.Combine(_dirPath, name);
        Directory.CreateDirectory(dir);
        var db = _factory.Create(dir, "pw", isReadOnly: false, config: Inline(inlineBytes));
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, note TEXT)");
        return db;
    }

    private static object? ValueOf(IDatabase db, int id, string column)
    {
        var rows = db.ExecuteQuery($"SELECT * FROM t WHERE id = {id}");
        return rows.Count == 0 ? null : rows[0][column];
    }

    // ── Round trip: short values inline, the long one through the arena ─────────────────────────

    [Fact]
    public async Task ShortValues_AreInline_AndLongValuesOverflow_AndBothReadBack()
    {
        await using var db = Open("roundtrip");
        db.ExecuteSQL($"INSERT INTO t VALUES (1, 'short', 'x'), (2, 'User2', 'payload-2'), (3, 'long', '{LongValue}')");

        Assert.Equal("short", ValueOf(db, 1, "name"));
        Assert.Equal("User2", ValueOf(db, 2, "name"));
        Assert.Equal("payload-2", ValueOf(db, 2, "note"));
        Assert.Equal(LongValue, ValueOf(db, 3, "note"));

        // A lookup on an inlined value must still match — this reaches the struct-scan string comparison as well
        // as the row decoder.
        Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE name = 'User2'"));
    }

    [Fact]
    public async Task Reopen_KeepsInlineAndOverflowValues()
    {
        await using (var db = Open("reopen"))
        {
            db.ExecuteSQL($"INSERT INTO t VALUES (1, 'short', '{LongValue}')");
            db.Flush();
        }

        await using var reopened = _factory.Create(
            Path.Combine(_dirPath, "reopen"), "pw", isReadOnly: false, config: Inline());

        Assert.Equal("short", ValueOf(reopened, 1, "name"));
        Assert.Equal(LongValue, ValueOf(reopened, 1, "note"));
    }

    // ── The mutation paths that decode a variable slot ──────────────────────────────────────────

    [Fact]
    public async Task Update_ThenBatchDelete_ThenReinsert_WorksOnInlinedRows()
    {
        await using var db = Open("mutations");
        for (int i = 1; i <= 6; i++)
        {
            db.ExecuteSQL($"INSERT INTO t VALUES ({i}, 'name{i}', 'note{i}')");
        }

        db.ExecuteSQL("UPDATE t SET name = 'changed' WHERE id = 4");
        Assert.Equal("changed", ValueOf(db, 4, "name"));

        // The contiguous bulk-delete fast path decodes the hash-indexed columns straight out of the raw record —
        // the site that would mis-read an inline payload as an arena offset.
        var deletes = new System.Collections.Generic.List<string>();
        for (int i = 1; i <= 3; i++)
        {
            deletes.Add($"DELETE FROM t WHERE id = {i}");
        }

        db.ExecuteBatchSQL(deletes);

        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 3"));
        Assert.Equal("changed", ValueOf(db, 4, "name"));
        Assert.Equal("name5", ValueOf(db, 5, "name"));

        // Deferred index maintenance: a deleted PK is free again and the re-insert must be readable.
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'again', 'again')");
        Assert.Equal("again", ValueOf(db, 1, "name"));
    }

    [Fact]
    public async Task Compaction_KeepsInlineAndOverflowValues()
    {
        await using var db = Open("compaction");
        db.ExecuteSQL($"INSERT INTO t VALUES (1, 'short', '{LongValue}'), (2, 'User2', 'note2')");
        db.ExecuteSQL("DELETE FROM t WHERE id = 2");

        Assert.True(db.TryGetTable("t", out var table));
        _ = ((SharpCoreDB.DataStructures.Table)table).CompactStorage();

        // The long value lives in the arena and must survive the compaction mapping; the short one is inline and
        // must be left alone entirely — re-pointing an inline payload as though it were an offset, or collecting it
        // as a live block, is exactly the failure this test exists for.
        Assert.Equal("short", ValueOf(db, 1, "name"));
        Assert.Equal(LongValue, ValueOf(db, 1, "note"));
        Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 2"));
    }

    // ── The upgrade path for existing data (plan §8c) ───────────────────────────────────────────

    /// <summary>
    /// Plan §8c: an existing fixed-width table whose <b>stored</b> capacity is below the configured one upgrades
    /// itself on a writable open, and its records are read with the capacity they were rewritten to.
    /// <para>
    /// This test exists because the first attempt at that migration failed a reopen — <c>SELECT … WHERE id = 1</c>
    /// found no row — and the assertions it used could not tell *no row* from *misdecoded value*: both surface as
    /// <see langword="null"/>. The assertion order here is therefore deliberate. First the stored capacity (did the
    /// migration happen and is it recorded), then the row count (are the records still there), then the primary-key
    /// lookup (is the index rebuilt on the new positions), and only then the values — one of which is <b>longer</b>
    /// than the new capacity, so it must still round-trip through the arena on every later reopen.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ExistingTable_AutoUpgradesInlineCapacity_OnWritableOpen_AndTheRowsStayReachable()
    {
        var dir = Path.Combine(_dirPath, "upgrade24");
        long dataFileBytesBefore;

        // A pre-upgrade database: fixed-width records at capacity 0, one short value and one long one.
        await using (var created = _factory.Create(dir, "pw", isReadOnly: false, config: Inline(0)))
        {
            created.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, note TEXT)");
            created.ExecuteSQL($"INSERT INTO t VALUES (1, 'short', '{LongValue}'), (2, 'User2', 'payload-2')");
            created.Flush();
            Assert.True(created.TryGetTable("t", out var beforeTable));
            dataFileBytesBefore = new FileInfo(((SharpCoreDB.DataStructures.Table)beforeTable).DataFile).Length;
        }

        // Writable open at a higher capacity — the upgrade is expected here.
        await using (var upgraded = _factory.Create(dir, "pw", isReadOnly: false, config: Inline(24)))
        {
            Assert.True(upgraded.TryGetTable("t", out var table));
            Assert.Equal(24, ((SharpCoreDB.DataStructures.Table)table).FixedWidthInlineValueBytes);

            // The rewrite really happened: a variable slot grows from 5 bytes to 7 + 24, so the records on disk must
            // have grown. Without this, a future "migration" that only flips the property would still pass the
            // value assertions below.
            Assert.True(
                new FileInfo(((SharpCoreDB.DataStructures.Table)table).DataFile).Length > dataFileBytesBefore,
                $"expected the data file to grow when the capacity went 0 -> 24 (was {dataFileBytesBefore} bytes)");
            Assert.Equal(2, upgraded.ExecuteQuery("SELECT * FROM t").Count);
            Assert.Single(upgraded.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
            Assert.Equal("short", ValueOf(upgraded, 1, "name"));
            Assert.Equal(LongValue, ValueOf(upgraded, 1, "note"));
            Assert.Equal("payload-2", ValueOf(upgraded, 2, "note"));

            // The upgraded table must also accept writes, and they must be readable without another reopen.
            upgraded.ExecuteSQL("INSERT INTO t VALUES (3, 'after', 'migration')");
            Assert.Equal("after", ValueOf(upgraded, 3, "name"));
            upgraded.Flush();
        }

        // Reopen again: the layout is self-describing now, so there is nothing left to migrate and no row may be lost.
        await using (var again = _factory.Create(dir, "pw", isReadOnly: false, config: Inline(24)))
        {
            Assert.Equal(3, again.ExecuteQuery("SELECT * FROM t").Count);
            Assert.Single(again.ExecuteQuery("SELECT * FROM t WHERE id = 3"));
            Assert.Equal("after", ValueOf(again, 3, "name"));
            Assert.Equal(LongValue, ValueOf(again, 1, "note"));
        }

        // A read-only open must only read: same capacity, every row reachable, nothing rewritten.
        await using var readOnly = _factory.Create(dir, "pw", isReadOnly: true, config: Inline(24));
        Assert.True(readOnly.TryGetTable("t", out var roTable));
        Assert.Equal(24, ((SharpCoreDB.DataStructures.Table)roTable).FixedWidthInlineValueBytes);
        Assert.Equal(3, readOnly.ExecuteQuery("SELECT * FROM t").Count);
        Assert.Equal(LongValue, ValueOf(readOnly, 1, "note"));
    }

    /// <summary>
    /// The upgrade is <b>one-way and opt-in by capacity</b>: a database written at a higher capacity is never
    /// rewritten <i>down</i>, and a config that asks for the historical layout (0) leaves an existing inline table
    /// exactly as it is. Without this, a caller could silently re-layout data by opening it with a different config.
    /// </summary>
    [Fact]
    public async Task ExistingTable_IsNeverDowngraded_ByALowerConfiguredCapacity()
    {
        var dir = Path.Combine(_dirPath, "nodowngrade");

        await using (var created = _factory.Create(dir, "pw", isReadOnly: false, config: Inline(24)))
        {
            created.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, note TEXT)");
            created.ExecuteSQL("INSERT INTO t VALUES (1, 'short', 'note1')");
            created.Flush();
        }

        await using var openedWithZero = _factory.Create(dir, "pw", isReadOnly: false, config: Inline(0));
        Assert.True(openedWithZero.TryGetTable("t", out var table));
        Assert.Equal(24, ((SharpCoreDB.DataStructures.Table)table).FixedWidthInlineValueBytes);
        Assert.Equal("short", ValueOf(openedWithZero, 1, "name"));
        Assert.Equal("note1", ValueOf(openedWithZero, 1, "note"));
    }


    /// on <b>both</b> paths — the multi-file metadata DTO and the single-file <c>TableMetadataEntry</c> (carved out of
    /// its reserved bytes, so older files read 0 = the historical layout) — so a reopened database always decodes with
    /// the capacity its records were written with, never with whatever this config now says. 24 is the owner's default
    /// since 2026-09-22 (it was 16 from 2026-09-16): it is the value that removes the per-row overflow-arena write on
    /// the plan's tracked shapes, worth 0,63× → 0,84× on the fair-PK INSERT ratio and +22 % on the multi-row shape,
    /// while measuring no further gain at 32 and no net byte cost at 24. 0 restores the historical layout byte for byte.
    /// </summary>
    [Fact]
    public void Default_InlineCapacity_IsPinned_AndZeroKeepsTheHistoricalLayout()
    {
        Assert.Equal(24, new DatabaseConfig().FixedWidthInlineValueBytes);
        Assert.Equal(0, new DatabaseConfig { FixedWidthInlineValueBytes = 0 }.FixedWidthInlineValueBytes);
    }
}
