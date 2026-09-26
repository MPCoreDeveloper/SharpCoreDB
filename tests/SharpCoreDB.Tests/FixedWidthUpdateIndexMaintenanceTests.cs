// <copyright file="FixedWidthUpdateIndexMaintenanceTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Index maintenance after an in-place UPDATE of a FIXED-WIDTH record.
/// <para>
/// A fixed-width record keeps its variable-length columns in the table's overflow arena, and the in-place
/// patcher FREES the block it supersedes (<c>Table.Serialization.WriteVariableSlotInPlace</c>). Because
/// <c>OverflowArena.Free</c> drops that block from the arena's cache and <c>OverflowArena.Read</c> consults
/// only the cache, any read of the old value performed <b>after</b> the patch sees a freed block and decodes
/// <c>DBNull</c> — so the hash-index re-point removes the wrong key and the stale entry for the real old value
/// survives. The consequence is a wrong query result, not a lost optimisation: a lookup on the old value keeps
/// returning a row that no longer holds it.
/// </para>
/// <para>
/// <c>FindByIndex</c> is the assertion that pins this, not <c>Select</c>: <c>Select</c> has a scan fallback that
/// hides a stale index entry, which is exactly how the same class of defect survived a naive test before
/// (<c>PageBasedRelocationIndexTests</c>).
/// </para>
/// </summary>
public sealed class FixedWidthUpdateIndexMaintenanceTests : IDisposable
{
    /// <summary>Longer than the product default inline capacity (24 bytes), so the value lives in the arena.</summary>
    private const int OverflowingValueLength = 60;

    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FixedWidthUpdateIndexMaintenanceTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_FixedWidthIndexMaint_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private Interfaces.IDatabase CreateFixedWidthDb() => _factory.Create(
        _dirPath, "pw", isReadOnly: false, config: new DatabaseConfig { FixedWidthRecordLayout = true });

    [Fact]
    public void Update_ArenaBackedIndexedColumn_RemovesTheStaleIndexEntry()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, email TEXT)");
            Assert.True(db.TryGetTable("t", out var it));
            var table = Assert.IsType<Table>(it);
            Assert.True(
                table.IsFixedWidthRecords,
                "the fixed-width layout is what puts `name` in the overflow arena; without it this test cannot observe the case it exists for");

            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");

            var oldName = new string('a', OverflowingValueLength);
            var newName = new string('b', OverflowingValueLength);

            db.ExecuteSQL($"INSERT INTO t (id, name, email) VALUES (1, '{oldName}', 'u1@test.com')");
            Assert.Single(table.FindByIndex("name", oldName));

            db.ExecuteSQL($"UPDATE t SET name = '{newName}' WHERE id = 1");

            var rows = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal(newName, rows[0]["name"]);

            // The new key is reachable...
            Assert.Single(table.FindByIndex("name", newName));

            // ...and the superseded key must be gone. RED before the fix: the remove targeted DBNull, which the
            // patch's arena free had decoded the old value into.
            Assert.Empty(table.FindByIndex("name", oldName));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_ArenaBackedIndexedColumnToNull_RemovesTheStaleIndexEntry()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, email TEXT)");
            Assert.True(db.TryGetTable("t", out var it));
            var table = Assert.IsType<Table>(it);
            Assert.True(table.IsFixedWidthRecords);

            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");

            var oldName = new string('a', OverflowingValueLength);
            db.ExecuteSQL($"INSERT INTO t (id, name, email) VALUES (1, '{oldName}', 'u1@test.com')");
            Assert.Single(table.FindByIndex("name", oldName));

            db.ExecuteSQL("UPDATE t SET name = NULL WHERE id = 1");

            var rows = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(rows);
            Assert.True(rows[0]["name"] is null or DBNull, $"expected NULL, got '{rows[0]["name"]}'");

            // A column set to NULL has no index entry any more — the same rule the append fallback path applies
            // (`if (row.TryGetValue(key, out var newKey) && newKey is not null) Add(...)`).
            Assert.Empty(table.FindByIndex("name", oldName));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
