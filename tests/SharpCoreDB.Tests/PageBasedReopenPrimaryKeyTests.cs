// <copyright file="PageBasedReopenPrimaryKeyTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Regression tests for Issue #404: a directory-mode database created with an explicit
/// <see cref="StorageEngineType.PageBased"/> engine must rebuild the in-memory primary-key
/// index when reopened, so <c>FindByPrimaryKey</c> returns the persisted rows exactly like the
/// append/columnar engines. Before the fix the index was rebuilt from <c>DataFile</c> (which is
/// empty for PageBased — rows live in the engine's <c>.pages</c> files), so point lookups
/// silently returned null after a reopen while full scans still saw every row.
/// </summary>
public sealed class PageBasedReopenPrimaryKeyTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public PageBasedReopenPrimaryKeyTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_PageBasedPk_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private static DatabaseConfig PageBasedConfig()
        => new() { StorageEngineType = StorageEngineType.PageBased };

    private static void DisposeDb(IDatabase db) => (db as IDisposable)?.Dispose();

    [Fact]
    public void PageBased_TextPrimaryKey_FindByPrimaryKey_AfterReopen_ReturnsRow()
    {
        var config = PageBasedConfig();

        // Session 1: create a directory-mode PageBased database, insert rows, flush durably and close.
        var db = _factory.Create(_dirPath, "pw", isReadOnly: false, config);
        try
        {
            db.ExecuteSQL("CREATE TABLE queue (id TEXT PRIMARY KEY, payload TEXT)");

            Assert.True(db.TryGetTable("queue", out var table));
            Assert.Equal(StorageMode.PageBased, Assert.IsType<Table>(table).StorageMode);

            for (int i = 0; i < 10; i++)
            {
                db.ExecuteSQL($"INSERT INTO queue VALUES ('key-{i}', 'payload-{i}')");
            }

            // In-session point lookups already work (the index is maintained on write);
            // this guards the "full scan works but point lookup fails" contrast.
            var inSession = db.FindByPrimaryKey("queue", "key-7");
            Assert.NotNull(inSession);
            Assert.Equal("payload-7", inSession!["payload"]?.ToString());

            db.Flush();
            db.ForceSave();
        }
        finally
        {
            DisposeDb(db);
        }

        // Session 2: reopen with the same configuration.
        var db2 = _factory.Create(_dirPath, "pw", isReadOnly: false, config);
        try
        {
            // Full scan sees the rows (this always worked for PageBased).
            var all = db2.ExecuteQuery("SELECT id FROM queue");
            Assert.Equal(10, all.Count);

            // Issue #404: FindByPrimaryKey returned null for every key after reopen because the
            // PK index was rebuilt from DataFile (no rows for PageBased) instead of the engine.
            for (int i = 0; i < 10; i++)
            {
                var row = db2.FindByPrimaryKey("queue", $"key-{i}");
                Assert.NotNull(row);
                Assert.Equal($"payload-{i}", row!["payload"]?.ToString());
            }

            // A non-existent key still returns null (no index corruption / false positives).
            Assert.Null(db2.FindByPrimaryKey("queue", "missing-key"));
        }
        finally
        {
            DisposeDb(db2);
        }
    }

    [Fact]
    public void PageBased_IntegerPrimaryKey_FindByPrimaryKey_AfterReopen_ReturnsRow()
    {
        var config = PageBasedConfig();

        var db = _factory.Create(_dirPath, "pw", isReadOnly: false, config);
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, title TEXT)");
            for (int i = 1; i <= 25; i++)
            {
                db.ExecuteSQL($"INSERT INTO docs VALUES ({i}, 'title-{i}')");
            }

            db.Flush();
            db.ForceSave();
        }
        finally
        {
            DisposeDb(db);
        }

        var db2 = _factory.Create(_dirPath, "pw", isReadOnly: false, config);
        try
        {
            Assert.Equal(25, db2.ExecuteQuery("SELECT id FROM docs").Count);

            for (int i = 1; i <= 25; i++)
            {
                var row = db2.FindByPrimaryKey("docs", i);
                Assert.NotNull(row);
                Assert.Equal($"title-{i}", row!["title"]?.ToString());
            }

            Assert.Null(db2.FindByPrimaryKey("docs", 999));
        }
        finally
        {
            DisposeDb(db2);
        }
    }
}
