// <copyright file="AppendUpdateIndexMaintenanceTests.cs" company="MPCoreDeveloper">
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
/// Pins hash-index maintenance on the Columnar <b>append</b> update path: when an update changes a record's
/// stored length, the engine appends a new version and leaves the old record in the file, so every loaded
/// index must drop the entries that point at the position the update moved away from.
/// <para>
/// Why it exists: the single-row UPDATE captures the old values of hash-indexed columns only when the
/// statement names one of them (the S2 shortcut — "the statement named no indexed column, so every entry
/// still holds the same key at the same position"). That reasoning holds while the record stays in place, but
/// the append fallback moves it, so a statement that names no loaded index left its entries pointing at the
/// record it abandoned. The abandoned entry is invisible while the row's indexed value stays the same — the
/// record it points at still carries that key — and becomes a returned <b>ghost row</b> as soon as the value
/// changes, because then the entry names a key the current row no longer has. Found in session 71 while
/// implementing §9 row 5 (the eager auto-index set had made every column "named" for this purpose and masked
/// it); measured there on the raw entry count: 4 entries for 3 rows, and one lookup answered with the ghost.
/// </para>
/// </summary>
public sealed class AppendUpdateIndexMaintenanceTests : IDisposable
{
    private const string LongerNote = "note-that-is-much-longer-than-the-old-one";

    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public AppendUpdateIndexMaintenanceTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_AppendIdx_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    [Fact]
    public void AppendUpdate_OfAColumnNoIndexNames_LeavesNoEntryForThePositionItAbandoned()
    {
        // The legacy variable-length layout is the point of the test: only there does a growing value change
        // the stored record length and force the append fallback.
        using var db = (Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = true,
            AutoFixedWidthRecords = false,
        });

        const string Table = "append_idx_vw";
        db.ExecuteSQL($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, v TEXT, note TEXT)");
        for (int i = 0; i < 3; i++)
        {
            db.InsertBatch(Table, [new Dictionary<string, object>
            {
                ["id"] = i,
                ["v"] = $"old{i}",
                ["note"] = $"n{i}",
            }]);
        }

        // One explicit index on `v`, loaded by a query first: the entries the updates below have to drop must
        // exist before they run.
        db.ExecuteSQL($"CREATE INDEX {Table}_idx_v ON {Table}(v)");
        Assert.Single(db.ExecuteQuery($"SELECT id FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "old1" }));

        // (1) Appends a new version of row 1 and names no loaded index (`note` is in no index), so the update
        // path never snapshots the old keys: the entries for the position it abandons are dropped by the
        // append branch itself, or not at all.
        db.ExecuteSQL($"UPDATE {Table} SET note = '{LongerNote}' WHERE id = 1");

        // (2) The row's indexed value changes. This update removes the entries of the position IT leaves; any
        // entry still pointing at an abandoned position afterwards is the defect.
        db.ExecuteSQL($"UPDATE {Table} SET v = 'brand-new-value' WHERE id = 1");

        // Every row is still there exactly once, and row 1 shows the version both updates wrote...
        Assert.Equal(3, db.ExecuteQuery($"SELECT id FROM {Table}").Count);
        var scannedRows = db.ExecuteQuery($"SELECT id, v, note FROM {Table}")
            .Select(r => $"{r["id"]}={r["v"]}/{r["note"]}")
            .ToArray();
        Assert.Contains($"1=brand-new-value/{LongerNote}", scannedRows);

        // ...the loaded index holds exactly one entry per live row (the abandoned entry made it 4)...
        Assert.True(db.TryGetTable(Table, out var probe));
        var stats = Assert.IsType<Table>(probe).GetHashIndexStatistics("v");
        Assert.NotNull(stats);
        Assert.Equal(3, stats!.Value.TotalRows);

        // ...and the value row 1 no longer has is reachable through no index: before the fix this lookup
        // returned the record the first update abandoned — a ghost row no scan shows (SQLite: 0 rows).
        Assert.Empty(db.ExecuteQuery($"SELECT id FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "old1" }));
        Assert.Single(db.ExecuteQuery($"SELECT id FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "brand-new-value" }));
        Assert.Single(db.ExecuteQuery($"SELECT id FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "old2" }));
    }
}
