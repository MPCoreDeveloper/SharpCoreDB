// <copyright file="DeltaUpdateWiringTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Hybrid;
using SharpCoreDB.Storage.Scdb;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// WP13 tests: schema-aware DeltaCodec round-trips on real SharpCoreDB row layouts, and the
/// EnableDeltaUpdates wiring in Table.Update (delta statistics recorded when the engine
/// advertises delta support, none when disabled).
/// </summary>
public sealed class DeltaUpdateWiringTests : IDisposable
{
    private readonly string testDbPath;

    public DeltaUpdateWiringTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_wp13_{Guid.NewGuid()}");
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

    private Table CreatePageBasedTable(bool enableDeltaUpdates, Action<Table> schema)
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true, EnableDeltaUpdates = enableDeltaUpdates };

        // The config must flow to the engine via the constructor so that
        // PageBasedEngine.SupportsDeltaUpdates reflects EnableDeltaUpdates.
        var table = new Table(new Services.Storage(crypto, key, config, null), isReadOnly: false, config)
        {
            Name = "wp13_tbl",
            DataFile = Path.Combine(testDbPath, "wp13_tbl.pages"),
            StorageMode = StorageMode.PageBased,
        };

        schema(table);
        table.PrimaryKeyIndex = 0;
        table.InitializeStorageEngine();
        return table;
    }

    private static byte[] BuildRow(int id, string name, double score, bool active, DateTime created)
    {
        // Mirrors the SharpCoreDB serialized row layout:
        // [flag:1][payload] per column; TEXT payload is [len:4][utf8].
        var row = new List<byte>(64);
        Span<byte> scratch = stackalloc byte[16];

        row.Add(1);
        BinaryPrimitives.WriteInt32LittleEndian(scratch, id);
        row.AddRange(scratch[..4].ToArray());

        row.Add(1);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        BinaryPrimitives.WriteInt32LittleEndian(scratch, nameBytes.Length);
        row.AddRange(scratch[..4].ToArray());
        row.AddRange(nameBytes);

        row.Add(1);
        BinaryPrimitives.WriteInt64LittleEndian(scratch, BitConverter.DoubleToInt64Bits(score));
        row.AddRange(scratch[..8].ToArray());

        row.Add(1);
        row.Add(active ? (byte)1 : (byte)0);

        row.Add(1);
        BinaryPrimitives.WriteInt64LittleEndian(scratch, created.ToBinary());
        row.AddRange(scratch[..8].ToArray());

        return row.ToArray();
    }

    [Fact]
    public void DeltaCodec_SchemaAware_EncodeApply_RoundTrip()
    {
        var created = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        byte[] oldRow = BuildRow(1, "ab", 1.5, true, created);
        byte[] newRow = BuildRow(1, "ab", 9.9, true, created); // only score changed

        // Field layout of the built row: id(5), name(7), score(9), active(2), created(9)
        int[] fieldSizes = [5, 7, 9, 2, 9];

        var delta = new byte[oldRow.Length];
        int written = DeltaCodec.EncodeDelta(oldRow, newRow, fieldSizes, delta);

        // Only the score field changed: header(4) + fieldIndex(4) + score bytes(9)
        Assert.Equal(4 + 4 + 9, written);

        var result = new byte[oldRow.Length];
        DeltaCodec.ApplyDelta(oldRow, delta.AsSpan(0, written), fieldSizes, result);

        Assert.Equal(newRow, result);
    }

    [Fact]
    public void DeltaCodec_SchemaAware_MultipleChangedFields_ApplyDelta_ReconstructsNewRow()
    {
        var created = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        byte[] oldRow = BuildRow(1, "ab", 1.5, true, created);
        byte[] newRow = BuildRow(7, "zz", 9.9, false, created.AddDays(1)); // id, name, score, active, created

        int[] fieldSizes = [5, 7, 9, 2, 9];

        // All five fields change: header(4) + per field (fieldIndex 4 + value) exceeds the
        // record size, so use an explicitly large buffer here.
        var delta = new byte[64];
        int written = DeltaCodec.EncodeDelta(oldRow, newRow, fieldSizes, delta);

        var result = new byte[oldRow.Length];
        DeltaCodec.ApplyDelta(oldRow, delta.AsSpan(0, written), fieldSizes, result);

        Assert.Equal(newRow, result);
    }

    [Fact]
    public void DeltaCodec_SchemaAware_ApplyDelta_RejectsInvalidFieldIndex()
    {
        byte[] oldRow = BuildRow(1, "ab", 1.5, true, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Delta claims a changed field at index 99, beyond the 5-field layout.
        var delta = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(delta.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(delta.AsSpan(4, 4), 99);

        var result = new byte[oldRow.Length];
        Assert.Throws<System.IO.InvalidDataException>(
            () => DeltaCodec.ApplyDelta(oldRow, delta, new[] { 5, 7, 9, 2, 9 }, result));
    }

    [Fact]
    public void Table_DeltaWiring_RecordsStats_WhenDeltaSupportEnabled()
    {
        var table = CreatePageBasedTable(enableDeltaUpdates: true, t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "age", DataType = "INTEGER" });
            t.AddColumn(new ColumnDefinition { Name = "score", DataType = "REAL" });
            t.AddColumn(new ColumnDefinition { Name = "active", DataType = "BOOLEAN" });
            t.AddColumn(new ColumnDefinition { Name = "created", DataType = "DATETIME" });
        });

        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["age"] = 30, ["score"] = 1.5, ["active"] = true, ["created"] = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });

        // Fixed-size fields: the WP11 patch applies in place and the delta codec runs.
        table.Update("id = 1", new Dictionary<string, object> { ["age"] = 99 });

        Assert.True(table.TotalDeltaUpdates >= 1, "Expected the delta codec to record the in-place update");
        Assert.True(table.DeltaBytesSaved > 0, "Expected positive byte savings for a single-field delta");

        var row = table.Select("id = 1").Single();
        Assert.Equal(99, row["age"]);
        Assert.Equal(1.5, (double)row["score"]); // untouched
    }

    [Fact]
    public void Table_DeltaWiring_NoStats_WhenDeltaSupportDisabled()
    {
        var table = CreatePageBasedTable(enableDeltaUpdates: false, t =>
        {
            t.AddColumn(new ColumnDefinition { Name = "id", DataType = "INTEGER", IsNotNull = true, IsPrimaryKey = true });
            t.AddColumn(new ColumnDefinition { Name = "age", DataType = "INTEGER" });
        });

        table.Insert(new Dictionary<string, object> { ["id"] = 1, ["age"] = 30 });
        table.Update("id = 1", new Dictionary<string, object> { ["age"] = 99 });

        Assert.Equal(0, table.TotalDeltaUpdates);
        Assert.Equal(0, table.DeltaBytesSaved);
    }
}
