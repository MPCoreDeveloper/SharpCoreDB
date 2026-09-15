// <copyright file="OverflowArenaConcurrencyTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Regression guard for the overflow-arena race. <c>Table.ValidateAndSerializeBatchOutsideLock</c>
/// serialises batches larger than 10,000 rows with <c>Parallel.For</c>, and every variable-length
/// value reaches the shared <see cref="SharpCoreDB.DataStructures.OverflowArena"/> through
/// <c>FixedWidthCodec.WriteSlot</c>. The arena's cache/free-list were plain <c>Dictionary</c>s, so a
/// fixed-width table with a TEXT column (the default layout for an explicit primary key) threw
/// <c>InvalidOperationException: Operations that change non-concurrent collections must have exclusive
/// access…</c> on a large <c>InsertBatch</c>. The arena is now synchronised.
/// </summary>
public sealed class OverflowArenaConcurrencyTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dir;

    public OverflowArenaConcurrencyTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dir = Path.Combine(Path.GetTempPath(), $"SCDB_arena_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static long CountOf(IDatabase db) =>
        Convert.ToInt64(db.ExecuteQuery("SELECT COUNT(*) AS n FROM t")[0].Values.First());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertBatch_LargeTextBatch_ParallelSerialization_KeepsEveryRow(bool atRest)
    {
        // 25,000 rows > the 10,000-row Parallel.For threshold, and both TEXT columns live in the arena.
        const int N = 25_000;
        var d = Path.Combine(_dir, atRest ? "enc" : "raw");
        Directory.CreateDirectory(d);
        using var db = (Database)_factory.Create(d, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = !atRest,
            EnableAtRestRecordEncryption = atRest,
        });

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, data TEXT)");

        var rows = new List<Dictionary<string, object>>(N);
        for (int i = 0; i < N; i++)
        {
            rows.Add(new Dictionary<string, object> { ["id"] = i, ["name"] = $"n{i}", ["data"] = $"payload-{i}" });
        }

        db.InsertBatch("t", rows); // previously threw on the concurrent arena write
        db.Flush();

        Assert.Equal(N, CountOf(db));

        // The arena offsets written during parallel serialization must resolve to the right payloads.
        for (int i = 0; i < N; i += 4099)
        {
            var hit = db.ExecuteQuery($"SELECT name, data FROM t WHERE id = {i}");
            Assert.Single(hit);
            Assert.Equal($"n{i}", hit[0]["name"]);
            Assert.Equal($"payload-{i}", hit[0]["data"]);
        }
    }

    [Fact]
    public void InsertBatch_LargeTextBatch_Reopen_ResolvesArenaBlocks()
    {
        const int N = 12_000;
        var d = Path.Combine(_dir, "reopen");
        Directory.CreateDirectory(d);

        using (var db = (Database)_factory.Create(d, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true }))
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, data TEXT)");
            var rows = new List<Dictionary<string, object>>(N);
            for (int i = 0; i < N; i++)
            {
                rows.Add(new Dictionary<string, object> { ["id"] = i, ["name"] = $"n{i}", ["data"] = $"payload-{i}" });
            }
            db.InsertBatch("t", rows);
            db.Flush();
            Assert.Equal(N, CountOf(db));
        }

        using var reopened = (Database)_factory.Create(d, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
        Assert.Equal(N, CountOf(reopened));
        var hit = reopened.ExecuteQuery("SELECT name, data FROM t WHERE id = 7777");
        Assert.Single(hit);
        Assert.Equal("n7777", hit[0]["name"]);
        Assert.Equal("payload-7777", hit[0]["data"]);
    }
}
