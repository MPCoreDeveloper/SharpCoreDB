// <copyright file="DeleteIndexCleanupTests.cs" company="MPCoreDeveloper">
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
/// WP12 regression tests: every delete path (DeleteByPrimaryKey, Delete, DeleteMultiple)
/// routes through the shared DeleteRecordsCore and must clean up the primary-key B-tree
/// and key-only hash indexes so no stale positions survive.
/// </summary>
public sealed class DeleteIndexCleanupTests : IDisposable
{
    private readonly string testDbPath;

    public DeleteIndexCleanupTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_wp12_{Guid.NewGuid()}");
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
            Name = "wp12_tbl",
            DataFile = Path.Combine(testDbPath, "wp12_tbl.pages"),
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

    private static void InsertRow(Table table, int id, string category)
        => table.Insert(new Dictionary<string, object> { ["id"] = id, ["category"] = category });

    [Fact]
    public void DeleteByPrimaryKey_WithoutHashIndexes_KeyOnly_RemovesRowAndPkIndex()
    {
        var table = CreatePageBasedTable();
        InsertRow(table, 1, "x");
        InsertRow(table, 2, "y");

        // No loaded hash indexes: the delete is key-only (no row storage read needed).
        Assert.True(table.DeleteByPrimaryKey(1));

        Assert.Empty(table.Select("id = 1"));
        Assert.Single(table.Select("id = 2"));
        Assert.False(table.DeleteByPrimaryKey(1)); // PK B-tree entry was removed
    }

    [Fact]
    public void DeleteByPrimaryKey_WithHashIndex_ClearsIndexEntries()
    {
        var table = CreatePageBasedTable();
        table.CreateHashIndex("category");
        InsertRow(table, 1, "x");
        InsertRow(table, 2, "x");
        table.EnsureIndexLoaded("category");

        var stats = table.GetHashIndexStatistics("category");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.Value.UniqueKeys);
        Assert.Equal(2, stats.Value.TotalRows);

        Assert.True(table.DeleteByPrimaryKey(1));

        stats = table.GetHashIndexStatistics("category");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.Value.TotalRows); // stale position for id=1 removed

        Assert.Empty(table.Select("id = 1"));
        Assert.Single(table.Select("id = 2"));
    }

    [Fact]
    public void Delete_Where_RemovesRowsAndHashIndexEntries()
    {
        var table = CreatePageBasedTable();
        table.CreateHashIndex("category");
        InsertRow(table, 1, "x");
        InsertRow(table, 2, "x");
        InsertRow(table, 3, "y");
        table.EnsureIndexLoaded("category");

        table.Delete("category = 'x'");

        Assert.Empty(table.Select("category = 'x'"));
        Assert.Single(table.Select("id = 3"));

        var stats = table.GetHashIndexStatistics("category");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.Value.TotalRows); // only id=3 remains
        Assert.Single(table.Select("id = 3"));
    }

    [Fact]
    public void Delete_Where_PkIndex_EntriesRemoved()
    {
        var table = CreatePageBasedTable();
        InsertRow(table, 1, "x");
        InsertRow(table, 2, "x");

        table.Delete("id = 1");

        Assert.False(table.DeleteByPrimaryKey(1)); // PK entry gone
        Assert.True(table.DeleteByPrimaryKey(2));  // other entry intact
    }
}
