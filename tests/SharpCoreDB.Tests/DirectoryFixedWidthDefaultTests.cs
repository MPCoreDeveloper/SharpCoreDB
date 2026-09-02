// <copyright file="DirectoryFixedWidthDefaultTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.IO;
using Xunit;

/// <summary>
/// B7+: DatabaseConfig.AutoFixedWidthRecords (default true) — NEW columnar tables that declare a
/// PRIMARY KEY are created with the fixed-width record layout even when
/// <see cref="DatabaseConfig.FixedWidthRecordLayout"/> is left false. Existing tables are never
/// rewritten by this default; the persisted per-table format stays authoritative on reopen.
/// </summary>
public sealed class DirectoryFixedWidthDefaultTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public DirectoryFixedWidthDefaultTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_FixedWidthDefault_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private IDatabase CreateDb(DatabaseConfig? config = null)
        => _factory.Create(_dirPath, "pw", isReadOnly: false, config: config ?? new DatabaseConfig());

    private static bool IsFixedWidth(IDatabase db, string tableName)
        => db.TryGetTable(tableName, out var t) && t.IsFixedWidthRecords;

    private string DatPath(string table) => Path.Combine(_dirPath, $"{table}.dat");

    [Fact]
    public void DefaultConfig_PkTable_IsCreatedFixedWidth()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            Assert.True(IsFixedWidth(db, "t"));
            Assert.True(db.TryGetTable("t", out var t) && t.PrimaryKeyIndex >= 0);

            db.ExecuteSQL("INSERT INTO t VALUES (1, 'alpha', 1.5)");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'beta', 2.5)");

            // Growing variable-column UPDATE is an in-place overwrite: the .dat never grows.
            long sizeBefore = new FileInfo(DatPath("t")).Length;
            db.ExecuteSQL("UPDATE t SET name = 'a considerably longer name value' WHERE id = 2");
            Assert.Equal(sizeBefore, new FileInfo(DatPath("t")).Length);

            var rows = db.ExecuteQuery("SELECT * FROM t ORDER BY id");
            Assert.Equal(2, rows.Count);
            Assert.Equal("a considerably longer name value", rows[1]["name"]);
            Assert.Equal(2.5, Convert.ToDouble(rows[1]["score"]));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void DefaultConfig_PkTable_FormatPersistsAcrossReopen()
    {
        IDatabase? db = null;
        try
        {
            db = CreateDb();
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'persisted')");
            Assert.True(IsFixedWidth(db, "t"));
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen WITHOUT any fixed-width config: the persisted per-table flag is authoritative.
        db = null;
        try
        {
            db = CreateDb();
            Assert.True(IsFixedWidth(db, "t"));
            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal("persisted", row[0]["name"]);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void DefaultConfig_NoPrimaryKey_StaysLegacyVariableLength()
    {
        var db = CreateDb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, score REAL)");
            Assert.False(IsFixedWidth(db, "t"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void AutoFixedWidthOptOut_PkTable_StaysLegacyVariableLength()
    {
        var db = CreateDb(new DatabaseConfig { AutoFixedWidthRecords = false });
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            Assert.False(IsFixedWidth(db, "t"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void ExplicitFixedWidthConfig_NoPrimaryKey_StillFixedWidth()
    {
        var db = CreateDb(new DatabaseConfig { FixedWidthRecordLayout = true });
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, score REAL)");
            Assert.True(IsFixedWidth(db, "t"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
