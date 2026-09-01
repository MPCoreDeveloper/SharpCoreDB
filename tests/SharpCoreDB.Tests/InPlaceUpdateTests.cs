// <copyright file="InPlaceUpdateTests.cs" company="MPCoreDeveloper">
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
/// WP11 regression tests: PageBased single-row UPDATE must overwrite only the updated
/// fields in the existing serialized row at their cached fixed column offsets (no
/// deserialize -> re-serialize round trip), and fall back to full serialization when a
/// variable-length field grows beyond its previous encoding.
/// </summary>
public sealed class InPlaceUpdateTests : IDisposable
{
    private readonly string testDbPath;

    public InPlaceUpdateTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_wp11_{Guid.NewGuid()}");
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

    private Table CreatePageBasedTable(Action<Table> schema)
    {
        var table = new Table
        {
            Name = "wp11_tbl",
            DataFile = Path.Combine(testDbPath, "wp11_tbl.pages"),
            StorageMode = StorageMode.PageBased,
        };

        schema(table);
        table.PrimaryKeyIndex = 0;

        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        table.SetStorage(new Services.Storage(crypto, key, config, null));
        table.InitializeStorageEngine();
        return table;
    }

    [Fact]
    public void Update_FixedSizeColumns_OverwritesInPlace_KeepsOtherColumns()
    {
        var table = CreatePageBasedTable(t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "age", DataType = "INTEGER" });
            t.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
            t.AddColumn(new ColumnDefinition { Name = "active", DataType = "BOOLEAN" });
            t.AddColumn(new ColumnDefinition { Name = "created", DataType = "DATETIME" });
        });

        var created = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["age"] = 30, ["score"] = 1.5, ["active"] = true, ["created"] = created });

        // All columns are fixed-size: the update overwrites fields at cached offsets.
        table.Update("id = 1", new Dictionary<string, object> { ["age"] = 99, ["score"] = 9.9 });

        var row = table.Select("id = 1").Single();
        Assert.Equal(99, row["age"]);
        Assert.Equal(9.9, (double)row["score"]);
        Assert.True((bool)row["active"]!); // untouched
        Assert.Equal(created, row["created"]); // untouched
    }

    [Fact]
    public void Update_StringShrinksAsLastColumn_OverwritesInPlace()
    {
        var table = CreatePageBasedTable(t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
            t.AddColumn(new ColumnDefinition { Name = "name", DataType = "TEXT" }); // last column
        });

        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["score"] = 4.25, ["name"] = "a-very-long-name-value" });

        // The string is the last column: shrinking it has no following columns to shift,
        // so the update overwrites the field in place at its cached offset.
        table.Update("id = 1", new Dictionary<string, object> { ["name"] = "short" });

        var row = table.Select("id = 1").Single();
        Assert.Equal("short", row["name"]);
        Assert.Equal(4.25, (double)row["score"]); // untouched
    }

    [Fact]
    public void Update_StringShrinksBeforeColumns_FallsBackToFullSerialization()
    {
        var table = CreatePageBasedTable(t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "name", DataType = "TEXT" });
            t.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
        });

        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["name"] = "a-very-long-name-value", ["score"] = 4.25 });

        // A variable-length field before the last column cannot be overwritten in place
        // (the following columns would shift): the update falls back to full serialization.
        table.Update("id = 1", new Dictionary<string, object> { ["name"] = "short" });

        var row = table.Select("id = 1").Single();
        Assert.Equal("short", row["name"]);
        Assert.Equal(4.25, (double)row["score"]); // untouched
    }

    [Fact]
    public void Update_StringGrows_FallsBackToFullSerialization()
    {
        var table = CreatePageBasedTable(t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "name", DataType = "TEXT" });
            t.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
        });

        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["name"] = "short", ["score"] = 4.25 });

        // The string field grows beyond its old encoding: the patch cannot apply in place,
        // so the row falls back to full serialization and everything must still round-trip.
        var grown = new string('G', 200);
        table.Update("id = 1", new Dictionary<string, object> { ["name"] = grown });

        var row = table.Select("id = 1").Single();
        Assert.Equal(grown, row["name"]);
        Assert.Equal(4.25, (double)row["score"]);
    }

    [Fact]
    public void Update_NullValue_InPlacePatch_AndBack()
    {
        var table = CreatePageBasedTable(t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
        });

        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["score"] = 4.25 });

        table.Update("id = 1", new Dictionary<string, object> { ["score"] = DBNull.Value });
        Assert.Equal(DBNull.Value, table.Select("id = 1").Single()["score"]);

        table.Update("id = 1", new Dictionary<string, object> { ["score"] = 8.75 });
        Assert.Equal(8.75, (double)table.Select("id = 1").Single()["score"]);
    }

    [Fact]
    public void UpdateBatchMultiColumn_FixedSizeField_OverwritesInPlace()
    {
        var table = CreatePageBasedTable(t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "age", DataType = "INTEGER" });
            t.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
        });

        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["age"] = 30, ["score"] = 4.25 });
        table.Insert(new Dictionary<string, object> { ["id"] = 2, ["age"] = 40, ["score"] = 8.5 });

        // PK-lookup batch update of a fixed-size field: the WP11 patch overwrites the
        // fields in the existing row bytes instead of re-serializing every column.
        int updated = table.UpdateBatchMultiColumn(
            "id",
            new (int id, Dictionary<string, object> columnUpdates)[]
            {
                (1, new Dictionary<string, object> { ["age"] = 99 }),
                (2, new Dictionary<string, object> { ["age"] = 55 }),
            });

        Assert.Equal(2, updated);
        var row1 = table.Select("id = 1").Single();
        Assert.Equal(99, row1["age"]);
        Assert.Equal(4.25, (double)row1["score"]); // untouched
        var row2 = table.Select("id = 2").Single();
        Assert.Equal(55, row2["age"]);
        Assert.Equal(8.5, (double)row2["score"]); // untouched
    }
}
