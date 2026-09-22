// <copyright file="PageBasedRelocationIndexTests.cs" company="MPCoreDeveloper">
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
/// Pins what a CROSS-PAGE relocation does to the hash indexes that the update did not touch.
/// <para>
/// Fix (2) (<c>124b2a62</c>) passes <c>changedColumn</c> so that a relocating update invalidates only the updated
/// column's index, on the reasoning that "an index over a column the statement did not touch cannot have gone stale".
/// That reasoning is about <b>values</b>; the relocation is about <b>positions</b> — the record moved, so every index
/// holding it now points at the old slot. Within a page the slot itself is repointed (the <c>(page, slot)</c>
/// reference is unchanged and those indexes stay correct); across pages the old slot is flagged
/// <c>RecordFlags.Deleted</c> and <c>TryReadRecord</c> returns false for it, while an index that fix (2) deliberately
/// left clean is never rebuilt — so a lookup through the untouched column silently misses the row.
/// </para>
/// <para>
/// The test asserts its own precondition first: if the update did not actually relocate the record (visible as an
/// unchanged storage reference in the PK B-tree), it fails with that message instead of passing silently, because a
/// test that cannot observe its case is worse than no test.
/// </para>
/// </summary>
public sealed class PageBasedRelocationIndexTests : IDisposable
{
    private readonly string testDbPath;

    /// <summary>Creates the per-test database directory.</summary>
    public PageBasedRelocationIndexTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_pbreloc_{Guid.NewGuid():N}");
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
    public void CrossPageRelocation_KeepsTheUntouchedColumnsIndexAbleToFindTheRow()
    {
        var table = CreatePageBasedTable();
        table.CreateHashIndex("category");

        // Fill several pages: the row under test must have no room to grow inside its own page, which is what
        // forces PageManager to relocate it to a different page (and to flag the old slot Deleted).
        for (int i = 1; i <= 12; i++)
        {
            table.Insert(new Dictionary<string, object>
            {
                ["id"] = i,
                ["category"] = i == 1 ? "alpha" : $"cat{i}",
                ["payload"] = new string('p', 1800),
            });
        }

        // The index on `category` must be LOADED, otherwise a later lookup simply rebuilds it from the live rows
        // and the case this test exists for cannot happen.
        table.EnsureIndexLoaded("category");

        long positionBefore = table.Index.Search("1").Value;

        table.UpdateBatch("id", "payload", new[] { (1, new string('q', 5200)) });

        long positionAfter = table.Index.Search("1").Value;
        Assert.True(
            positionBefore != positionAfter,
            $"the record did not relocate (storage reference stayed {positionBefore}), so this test cannot observe the case it exists for — adjust the payload sizes");

        // The relocation repoints the PK index itself, so the row is still reachable by primary key...
        Assert.Single(table.Select("id = 1"));

        // ...and it must also still be reachable through the index on the column the statement did NOT touch.
        Assert.Single(table.Select("category = 'alpha'"));
    }

    private Table CreatePageBasedTable()
    {
        var table = new Table
        {
            Name = "pbreloc_tbl",
            DataFile = Path.Combine(testDbPath, "pbreloc_tbl.pages"),
            StorageMode = StorageMode.PageBased,
        };

        table.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
        table.AddColumn(new ColumnDefinition { Name = "category", DataType = "TEXT" });
        table.AddColumn(new ColumnDefinition { Name = "payload", DataType = "TEXT" });
        table.PrimaryKeyIndex = 0;

        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        table.SetStorage(new Services.Storage(crypto, key, config, null));
        table.InitializeStorageEngine();
        return table;
    }
}
