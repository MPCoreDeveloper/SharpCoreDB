// <copyright file="FixedWidthMigrationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.IO;
using Xunit;

/// <summary>
/// B5: 1.x → 2.0 record-format migration. A legacy (variable-length records) database opened with
/// <see cref="DatabaseConfig.FixedWidthRecordLayout"/> is auto-migrated to the fixed-width layout;
/// an explicit <see cref="IDatabase.MigrateTableToFixedWidth"/> API is also provided. The record
/// format is persisted in metadata so reopen keeps the layout without the config flag.
/// </summary>
public sealed class FixedWidthMigrationTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FixedWidthMigrationTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_FixedWidthMigration_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private IDatabase CreateLegacyDb() => _factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig());

    private IDatabase CreateFixedWidthDb() => _factory.Create(
        _dirPath, "pw", isReadOnly: false, config: new DatabaseConfig { FixedWidthRecordLayout = true });

    private IDatabase CreateReadOnlyFixedWidthDb() => _factory.Create(
        _dirPath, "pw", isReadOnly: true, config: new DatabaseConfig { FixedWidthRecordLayout = true });

    private static bool IsFixedWidth(IDatabase db, string tableName)
        => db.TryGetTable(tableName, out var t) && t.IsFixedWidthRecords;

    private string DatPath(string table) => Path.Combine(_dirPath, $"{table}.dat");

    private string OvfPath(string table) => Path.ChangeExtension(DatPath(table), ".ovf");

    [Fact]
    public void ReopenWithFixedWidthConfig_AutoMigratesLegacyTable()
    {
        // 1.x database: variable-length records.
        IDatabase? db = null;
        try
        {
            db = CreateLegacyDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha', 1.5)");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'beta', 2.5)");
            db.ExecuteSQL("UPDATE t SET name = 'ALPHA-2' WHERE id = 1"); // stale row in .dat
            Assert.False(IsFixedWidth(db, "t"));
        }
        finally { (db as IDisposable)?.Dispose(); }

        // 2.0 reopen with the config flag → auto-migrate.
        db = null;
        try
        {
            db = CreateFixedWidthDb();
            Assert.True(IsFixedWidth(db, "t"));
            Assert.True(File.Exists(OvfPath("t")));

            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal("ALPHA-2", row[0]["name"]);
            Assert.Equal(2.5, Convert.ToDouble(db.ExecuteQuery("SELECT * FROM t WHERE id = 2")[0]["score"]));

            // Fixed-width behavior after migration: in-place UPDATE, no .dat growth.
            long sizeAfterMigrate = new FileInfo(DatPath("t")).Length;
            db.ExecuteSQL("UPDATE t SET name = 'a much longer name value than the original' WHERE id = 2");
            Assert.Equal(sizeAfterMigrate, new FileInfo(DatPath("t")).Length);
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Third open WITHOUT the config flag → persisted record format is authoritative.
        db = null;
        try
        {
            db = CreateLegacyDb();
            Assert.True(IsFixedWidth(db, "t"));
            Assert.Equal("a much longer name value than the original", db.ExecuteQuery("SELECT * FROM t WHERE id = 2")[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void ExplicitApi_MigrateTableToFixedWidth_PersistsFormat()
    {
        IDatabase? db = null;
        try
        {
            db = CreateLegacyDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'beta')");

            int migrated = db.MigrateTableToFixedWidth("t");
            Assert.Equal(2, migrated);
            Assert.True(IsFixedWidth(db, "t"));

            Assert.Equal("alpha", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen without the config flag → still fixed-width (flag persisted in metadata).
        db = null;
        try
        {
            db = CreateLegacyDb();
            Assert.True(IsFixedWidth(db, "t"));
            Assert.Equal("beta", db.ExecuteQuery("SELECT * FROM t WHERE id = 2")[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void ExplicitApi_OnAlreadyFixedWidth_ReturnsZero()
    {
        IDatabase? db = null;
        try
        {
            db = CreateFixedWidthDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha')");
            Assert.Equal(0, db.MigrateTableToFixedWidth("t"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void EmptyTable_Migrates_SetsFormat()
    {
        IDatabase? db = null;
        try
        {
            db = CreateLegacyDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            Assert.Equal(0, db.MigrateTableToFixedWidth("t"));
            Assert.True(IsFixedWidth(db, "t"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void ReadOnlyOpen_WithFixedWidthConfig_StaysLegacy_DataReadable()
    {
        IDatabase? db = null;
        try
        {
            db = CreateLegacyDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha')");
        }
        finally { (db as IDisposable)?.Dispose(); }

        db = null;
        try
        {
            db = CreateReadOnlyFixedWidthDb();
            // Read-only opens never rewrite data: the table must stay legacy and stay readable.
            Assert.False(IsFixedWidth(db, "t"));
            Assert.Equal("alpha", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void PageBasedTable_ExplicitMigration_Throws()
    {
        IDatabase? db = null;
        try
        {
            db = CreateLegacyDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha')");

            Assert.True(db.TryGetTable("t", out var table));
            var concrete = Assert.IsType<Table>(table);
            concrete.StorageMode = SharpCoreDB.Storage.Hybrid.StorageMode.PageBased;

            Assert.Throws<NotSupportedException>(() => concrete.MigrateToFixedWidth());
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void Migration_DropsStaleVersions()
    {
        IDatabase? db = null;
        try
        {
            db = CreateLegacyDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'v1')");
            db.ExecuteSQL("UPDATE t SET name = 'v2' WHERE id = 1");
            db.ExecuteSQL("UPDATE t SET name = 'v3' WHERE id = 1");

            db.MigrateTableToFixedWidth("t");

            // Only the current version remains after migration.
            var rows = db.ExecuteQuery("SELECT * FROM t");
            Assert.Single(rows);
            Assert.Equal("v3", rows[0]["name"]);

            Assert.True(db.TryGetTable("t", out var table));
            var concrete = Assert.IsType<Table>(table);
            Assert.Equal(1, concrete.Select().Count);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
