// <copyright file="SingleFileFixedWidthTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using System;
using System.IO;
using Xunit;

/// <summary>
/// B6: single-file (.scdb) fixed-width record layout. With <see cref="DatabaseConfig.FixedWidthRecordLayout"/>
/// the single-file table stores binary fixed-width records in its data block (variable values in a
/// dedicated overflow block) instead of JSON rows, so value-only updates keep the data block
/// constant-size. The on-disk format is detected on reopen; legacy JSON tables migrate on demand.
/// </summary>
public sealed class SingleFileFixedWidthTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _scdbPath;

    public SingleFileFixedWidthTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_FixedWidth_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    private static DatabaseOptions FixedWidthOptions() => new()
    {
        StorageMode = StorageMode.SingleFile,
        EnableMemoryMapping = true,
        AutoVacuum = true,
        AutoVacuumMode = VacuumMode.Quick,
        DatabaseConfig = new DatabaseConfig { FixedWidthRecordLayout = true },
    };

    private static DatabaseOptions JsonOptions() => new()
    {
        StorageMode = StorageMode.SingleFile,
        EnableMemoryMapping = true,
        AutoVacuum = true,
        AutoVacuumMode = VacuumMode.Quick,
    };

    private IDatabase CreateFixedWidthDb() => _factory.CreateWithOptions(_scdbPath, "pw", FixedWidthOptions());

    private IDatabase CreateJsonDb() => _factory.CreateWithOptions(_scdbPath, "pw", JsonOptions());

    private static bool IsFixedWidth(IDatabase db, string tableName)
        => db.TryGetTable(tableName, out var t) && t.IsFixedWidthRecords;

    [Fact]
    public void RoundTrip_AllColumnTypes_Reopen()
    {
        IDatabase? db = null;
        try
        {
            db = CreateFixedWidthDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL, flag BOOLEAN, created DATETIME)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha', 1.5, 1, '2024-01-01')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'beta', 2.5, 0, '2024-02-02')");
            Assert.True(IsFixedWidth(db, "t"));

            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 2");
            Assert.Single(row);
            Assert.Equal("beta", row[0]["name"]);
            Assert.Equal(2.5, Convert.ToDouble(row[0]["score"]));
            Assert.Equal(false, Convert.ToBoolean(row[0]["flag"]));
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen: the on-disk format is detected (no config flag needed for reading).
        db = null;
        try
        {
            db = CreateJsonDb(); // config flag OFF
            Assert.True(IsFixedWidth(db, "t"));
            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal("alpha", row[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void SameLengthUpdate_DoesNotGrowDataBlock()
    {
        IDatabase? db = null;
        try
        {
            db = CreateFixedWidthDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'AAAAAAAA')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'BBBBBBBB')");

            // The first same-length update appends one arena block (the free-list is empty); from
            // then on freed blocks are reused in place, so the file must stop growing.
            db.ExecuteSQL("UPDATE t SET name = 'CCCCCCCC' WHERE id = 1");
            long sizeAfterFirstUpdate = new FileInfo(_scdbPath).Length;

            string[] names = { "DDDDDDDD", "EEEEEEEE" };
            for (int i = 0; i < 100; i++)
            {
                db.ExecuteSQL($"UPDATE t SET name = '{names[i % names.Length]}' WHERE id = 1");
            }

            Assert.Equal("EEEEEEEE", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["name"]);
            Assert.Equal(sizeAfterFirstUpdate, new FileInfo(_scdbPath).Length);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void ExplicitMigration_JsonToFixedWidth()
    {
        // Legacy single-file table (JSON rows).
        IDatabase? db = null;
        try
        {
            db = CreateJsonDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'beta')");
            Assert.False(IsFixedWidth(db, "t"));

            int migrated = db.MigrateTableToFixedWidth("t");
            Assert.Equal(2, migrated);
            Assert.True(IsFixedWidth(db, "t"));

            Assert.Equal("alpha", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen without any config → binary format detected, data intact.
        db = null;
        try
        {
            db = CreateJsonDb();
            Assert.True(IsFixedWidth(db, "t"));
            Assert.Equal("beta", db.ExecuteQuery("SELECT * FROM t WHERE id = 2")[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void ReopenWithFixedWidthConfig_AutoMigratesJson()
    {
        IDatabase? db = null;
        try
        {
            db = CreateJsonDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha')");
        }
        finally { (db as IDisposable)?.Dispose(); }

        db = null;
        try
        {
            db = CreateFixedWidthDb(); // config opts into fixed-width → auto-migrate on load
            Assert.True(IsFixedWidth(db, "t"));
            Assert.Equal("alpha", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void PkIndexWorksOnBinaryFormat()
    {
        IDatabase? db = null;
        try
        {
            db = CreateFixedWidthDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");

            var row = db.FindByPrimaryKey("t", 2);
            Assert.NotNull(row);
            Assert.Equal("b", row!["name"]);

            Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
