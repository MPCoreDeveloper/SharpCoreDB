// <copyright file="BatchUpdateRelocationTests.cs" company="MPCoreDeveloper">
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
using System.Linq;
using Xunit;

/// <summary>
/// WP10 regression tests: every PageBased batch-update path must consume the new storage
/// reference returned by <c>engine.Update</c> when a growing record is relocated to another
/// page, and re-point the primary-key index so the row stays reachable (previously the index
/// pointed at the deleted slot -> silent row loss).
/// </summary>
public sealed class BatchUpdateRelocationTests : IDisposable
{
    private readonly string testDbPath;

    public BatchUpdateRelocationTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_batch_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);
    }

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
            // Ignore cleanup errors
        }
    }

    private Table CreatePageBasedTable()
    {
        var table = new Table
        {
            Name = "batch_reloc",
            DataFile = Path.Combine(testDbPath, "batch_reloc.pages"),
            StorageMode = StorageMode.PageBased,
        };

        // AddColumn keeps every per-column list (IsAuto, IsNotNull, DefaultValues,
        // DefaultExpressions, ColumnCheckExpressions, ColumnCollations, ...) in sync.
        table.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
        table.AddColumn(new ColumnDefinition { Name = "payload", DataType = "TEXT" });
        table.PrimaryKeyIndex = 0;

        // Insert/Select require a non-null IStorage even though PageBased mode routes all
        // record I/O through the page manager (the IStorage is only used for the guard).
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        table.SetStorage(new Services.Storage(crypto, key, config, null));

        table.InitializeStorageEngine();
        return table;
    }

    /// <summary>A payload close to the page data capacity so the updated record must relocate.</summary>
    private static string BigPayload() => new string('X', 8100);

    private static void InsertRow(Table table, int id, string payload)
        => table.Insert(new Dictionary<string, object> { ["id"] = id, ["payload"] = payload });

    private static void AssertRowIntact(Table table, int id, string payload)
        => Assert.Equal(payload, table.Select($"id = {id}").Single()["payload"]);

    [Fact]
    public void UpdateBatch_Where_GrowingRecord_RelocatesAndRepointsIndex()
    {
        var table = CreatePageBasedTable();
        InsertRow(table, 1, "initial-1");
        InsertRow(table, 2, "initial-2");
        InsertRow(table, 3, "initial-3");

        var big = BigPayload();
        int updated = table.UpdateBatch("id = 1", new Dictionary<string, object> { ["payload"] = big });

        Assert.Equal(1, updated);
        AssertRowIntact(table, 1, big); // PK index re-pointed to the relocated slot
        AssertRowIntact(table, 2, "initial-2");
        AssertRowIntact(table, 3, "initial-3");
    }

    [Fact]
    public void UpdateBatch_TypedSingleColumn_GrowingRecord_RelocatesAndRepointsIndex()
    {
        var table = CreatePageBasedTable();
        InsertRow(table, 1, "initial-1");
        InsertRow(table, 2, "initial-2");

        var big = BigPayload();
        int updated = table.UpdateBatch<int, string>(
            "id", "payload",
            new (int id, string value)[] { (1, big) });

        Assert.Equal(1, updated);
        AssertRowIntact(table, 1, big);
        AssertRowIntact(table, 2, "initial-2");
    }

    [Fact]
    public void UpdateBatchMultiColumn_GrowingRecord_RelocatesAndRepointsIndex()
    {
        var table = CreatePageBasedTable();
        InsertRow(table, 1, "initial-1");
        InsertRow(table, 2, "initial-2");

        var big = BigPayload();
        int updated = table.UpdateBatchMultiColumn(
            "id",
            new (int id, Dictionary<string, object> columnUpdates)[]
            {
                (1, new Dictionary<string, object> { ["payload"] = big })
            });

        Assert.Equal(1, updated);
        AssertRowIntact(table, 1, big);
        AssertRowIntact(table, 2, "initial-2");
    }

    [Fact]
    public void UpdateBatchMultiColumnParallel_TrueParallel_GrowingRecords_RelocatesAndRepointsIndex()
    {
        var table = CreatePageBasedTable();

        // >1000 updates routes to the parallel Phase-1 deserialization path.
        const int rowCount = 1002;
        for (int i = 1; i <= rowCount; i++)
        {
            InsertRow(table, i, "initial");
        }

        var big = BigPayload();
        var updates = new List<(int id, Dictionary<string, object> columnUpdates)>(rowCount);
        for (int i = 1; i <= rowCount; i++)
        {
            updates.Add((i, new Dictionary<string, object> { ["payload"] = big }));
        }

        int updated = table.UpdateBatchMultiColumnParallel("id", updates, useParallel: true);

        Assert.Equal(rowCount, updated);
        AssertRowIntact(table, 1, big);      // relocated rows still reachable via PK index
        AssertRowIntact(table, rowCount, big);
    }
}
