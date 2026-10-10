// <copyright file="PageBasedIndexRebuildTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// Pins the PageBased half of the index-staleness invariant: a hash index that is <b>not loaded</b>
/// when an UPDATE/DELETE runs must still answer with the live truth once it is rebuilt lazily.
/// <para>
/// This is the counterpart of <c>FixedWidthPatchTests</c>' stale-index regression, and it exists
/// because the two halves need different protection. The append/Columnar paths must load their
/// indexes <i>before</i> the write — their rebuild re-reads a version-bearing data file, which is the
/// "stale row returned for the same PK" regression <c>54b0a5b8</c> fixed by pre-loading every index
/// (<c>Table.EnsureAllRegisteredIndexesLoaded</c>). PageBased needs no such pre-load: its rebuild
/// walks the live slot array, and a superseded record's slot is flagged deleted and skipped. Paying
/// the pre-load there cost a full-table decode per batch UPDATE entry (measured: <c>row-decode</c>
/// 100,000 calls for a 10,000-row UPDATE over 100,000 rows), so the helper is a no-op on PageBased —
/// which makes these tests the guard for that decision. Each test writes with the index unloaded (no
/// <c>EnsureIndexLoaded</c> call, exactly the state the no-op leaves behind) and then asserts the
/// indexed lookups, so a rebuild that ever returned a superseded or deleted row fails here.
/// </para>
/// </summary>
public sealed class PageBasedIndexRebuildTests : IDisposable
{
    private readonly string testDbPath;

    /// <summary>Creates the per-test database directory.</summary>
    public PageBasedIndexRebuildTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_pbidx_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDbPath);
    }

    /// <summary>Removes the per-test database directory.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Update_IndexedColumn_WithIndexUnloaded_SeesTheNewValueAndNotTheOldOne()
    {
        var table = CreatePageBasedTable();
        table.CreateHashIndex("category");
        InsertRow(table, 1, "alpha");
        InsertRow(table, 2, "beta");

        // No EnsureIndexLoaded: the write must not depend on a pre-loaded index.
        int affected = table.UpdateAffectedCount("id = 1", new Dictionary<string, object> { ["category"] = "gamma" });

        Assert.Equal(1, affected);
        Assert.Single(table.Select("id = 1"));
        Assert.Empty(table.Select("category = 'alpha'")); // the superseded key must not survive a rebuild
        Assert.Single(table.Select("category = 'gamma'"));
        Assert.Single(table.Select("category = 'beta'"));
    }

    [Fact]
    public void BatchUpdate_IndexedColumn_WithIndexUnloaded_SeesTheNewValuesAndNotTheOldOnes()
    {
        var table = CreatePageBasedTable();
        table.CreateHashIndex("category");
        InsertRow(table, 1, "alpha");
        InsertRow(table, 2, "beta");
        InsertRow(table, 3, "gamma");

        // UpdateMultiple is the batch entry point whose PageBased pre-load is skipped — the exact
        // route the fair-PK harness arm takes (ExecuteBatchSQL → UpdateMultiple).
        table.UpdateMultiple(
        [
            ("id = 1", new Dictionary<string, object> { ["category"] = "delta" }),
            ("id = 2", new Dictionary<string, object> { ["category"] = "epsilon" }),
        ]);

        Assert.Empty(table.Select("category = 'alpha'"));
        Assert.Empty(table.Select("category = 'beta'"));
        Assert.Single(table.Select("category = 'delta'"));
        Assert.Single(table.Select("category = 'epsilon'"));
        Assert.Single(table.Select("category = 'gamma'"));
    }

    [Fact]
    public void Delete_IndexedColumn_WithIndexUnloaded_IndexedLookupDoesNotReturnTheDeletedRow()
    {
        var table = CreatePageBasedTable();
        table.CreateHashIndex("category");
        InsertRow(table, 1, "alpha");
        InsertRow(table, 2, "alpha");
        InsertRow(table, 3, "beta");

        // No EnsureIndexLoaded: the delete's index cleanup must not depend on a pre-loaded index.
        table.Delete("category = 'alpha'");

        Assert.Empty(table.Select("category = 'alpha'"));
        Assert.Empty(table.Select("id = 1"));
        Assert.Empty(table.Select("id = 2"));
        Assert.Single(table.Select("id = 3"));
        Assert.Single(table.Select("category = 'beta'"));
    }

    private static void InsertRow(Table table, int id, string category)
        => table.Insert(new Dictionary<string, object> { ["id"] = id, ["category"] = category });

    private Table CreatePageBasedTable()
    {
        var table = new Table
        {
            Name = "pbidx_tbl",
            DataFile = Path.Combine(testDbPath, "pbidx_tbl.pages"),
            StorageMode = StorageMode.PageBased,
        };

        table.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
        table.AddColumn(new ColumnDefinition { Name = "category", DataType = "TEXT" });
        table.PrimaryKeyIndex = 0;

        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        table.SetStorage(new Services.Storage(crypto, key, config, null));
        table.InitializeStorageEngine();
        return table;
    }
}
