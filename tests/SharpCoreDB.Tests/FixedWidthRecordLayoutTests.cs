// <copyright file="FixedWidthRecordLayoutTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Issue B1: out-of-line overflow (SQLite-model) — opt-in fixed-width record layout
/// (<see cref="DatabaseConfig.FixedWidthRecordLayout"/>). Fixed-size columns live at constant
/// record offsets; TEXT/BLOB values are stored in the table's overflow arena, so the record length
/// is constant per schema and every UPDATE (fixed OR variable column) is an in-place overwrite —
/// the .dat file does not grow.
/// </summary>
public sealed class FixedWidthRecordLayoutTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FixedWidthRecordLayoutTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_FixedWidthLayout_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private IDatabase CreateFixedWidthDb() => _factory.Create(
        _dirPath, "pw", isReadOnly: false, config: new DatabaseConfig { FixedWidthRecordLayout = true });

    private string DatPath(string table) => Path.Combine(_dirPath, $"{table}.dat");

    private string OvfPath(string table) => Path.ChangeExtension(DatPath(table), ".ovf");

    [Fact]
    public void RoundTrip_AllColumnTypes_PointAndFullScan()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL, flag BOOLEAN, created DATETIME)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha', 1.5, 1, '2024-01-01')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'beta', 2.5, 0, '2024-02-02')");

            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 2");
            Assert.Single(row);
            Assert.Equal("beta", row[0]["name"]);
            Assert.Equal(2.5, Convert.ToDouble(row[0]["score"]));
            Assert.Equal(false, Convert.ToBoolean(row[0]["flag"]));

            var all = db.ExecuteQuery("SELECT * FROM t ORDER BY id");
            Assert.Equal(2, all.Count);
            Assert.Equal("alpha", all[0]["name"]);
            Assert.Equal("beta", all[1]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_FixedColumn_DoesNotGrowDataFile()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, val INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 0)");

            long sizeAfterInsert = new FileInfo(DatPath("t")).Length;

            for (int i = 0; i <= 99; i++)
            {
                db.ExecuteSQL($"UPDATE t SET val = {i} WHERE id = 1");
            }

            Assert.Equal(sizeAfterInsert, new FileInfo(DatPath("t")).Length);
            Assert.Equal(99, Convert.ToInt32(db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["val"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_VariableColumn_Grow_DoesNotGrowDataFile_ValueCorrect()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'short')");

            long sizeAfterInsert = new FileInfo(DatPath("t")).Length;
            Assert.True(File.Exists(OvfPath("t"))); // variable values go to the overflow arena

            // Growing the string must NOT grow the data file — the record stays fixed-width and the
            // new payload goes to the arena.
            db.ExecuteSQL("UPDATE t SET name = 'a much longer name value than the original' WHERE id = 1");

            Assert.Equal(sizeAfterInsert, new FileInfo(DatPath("t")).Length);
            Assert.Equal("a much longer name value than the original", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_VariableColumn_Shrink_ValueCorrect()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a long original value')");
            db.ExecuteSQL("UPDATE t SET name = 'x' WHERE id = 1");

            Assert.Equal("x", db.ExecuteQuery("SELECT * FROM t WHERE id = 1")[0]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Delete_ByPrimaryKey_RemovesRow()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");

            db.ExecuteSQL("DELETE FROM t WHERE id = 1");

            Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
            Assert.Single(db.ExecuteQuery("SELECT * FROM t"));
            Assert.Equal("b", db.ExecuteQuery("SELECT * FROM t WHERE id = 2")[0]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Reopen_WithSameConfig_SurvivesArena()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha', 1.5)");
            db.ExecuteSQL("UPDATE t SET name = 'updated name that is longer' WHERE id = 1");
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        var db2 = CreateFixedWidthDb();
        try
        {
            var row = db2.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal("updated name that is longer", row[0]["name"]);
            Assert.Equal(1.5, Convert.ToDouble(row[0]["score"]));
        }
        finally
        {
            (db2 as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Arena_Compacts_ReclaimsSpace_DataCorrect()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'seed-1')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'seed-2')");

            // Many variable updates: each appends a new arena block (the previous one is freed),
            // so the .ovf grows until compaction reclaims it.
            for (int i = 0; i < 300; i++)
            {
                db.ExecuteSQL($"UPDATE t SET name = 'value-{i}-with-enough-length' WHERE id = 1");
            }

            long arenaBefore = new FileInfo(OvfPath("t")).Length;
            Assert.True(arenaBefore > 0);

            // Force compaction deterministically via the Table API (B3: arena + .dat together).
            Assert.True(db.TryGetTable("t", out var table));
            var concrete = Assert.IsType<SharpCoreDB.DataStructures.Table>(table);
            concrete.CompactStorage();

            long arenaAfter = new FileInfo(OvfPath("t")).Length;
            Assert.True(arenaAfter < arenaBefore, $"arena did not shrink: {arenaAfter} >= {arenaBefore}");

            // Data still correct after compaction + arena re-point.
            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal("value-299-with-enough-length", row[0]["name"]);
            Assert.Equal("seed-2", db.ExecuteQuery("SELECT * FROM t WHERE id = 2")[0]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void StructRow_Api_FallsBackToDictionary()
    {
        var db = CreateFixedWidthDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'beta')");

            var rows = db.ExecuteQueryStruct("SELECT * FROM t WHERE id = 2").ToList();
            Assert.Single(rows);
            Assert.Equal("beta", rows[0].GetValueBoxed(1).ToString());
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
