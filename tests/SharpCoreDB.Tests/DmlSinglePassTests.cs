// <copyright file="DmlSinglePassTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Issue #7/#8: single-pass DML paths.
/// - DELETE/UPDATE SQL no longer materialize matching rows twice (once for RETURNING/affected-count
///   and once inside the table operation) — the table operation itself returns the affected rows/count.
/// - Simple `pk = value` WHERE clauses are resolved through the primary-key B-tree directly
///   (single search + one read) in the single-row, batch and full-table DELETE/UPDATE paths.
/// These tests pin the observable behavior (affected rows/count and correctness for range /
/// non-indexed / non-PK WHERE clauses that must bypass the fast path).
/// </summary>
public sealed class DmlSinglePassTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public DmlSinglePassTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_DmlSinglePass_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    [Fact]
    public void SqlDelete_ByPrimaryKey_AffectedCountIsOne()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");

            db.ExecuteSQL("DELETE FROM t WHERE id = 1");

            Assert.Equal(1, db.GetLastChanges());
            Assert.Single(db.ExecuteQuery("SELECT * FROM t"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlDelete_ByPrimaryKey_NonExistentKey_AffectsZeroRows()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");

            db.ExecuteSQL("DELETE FROM t WHERE id = 999");

            Assert.Equal(0, db.GetLastChanges());
            Assert.Single(db.ExecuteQuery("SELECT * FROM t"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlDelete_RangeWhere_DeletesAllMatchingRows()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");
            db.ExecuteSQL("INSERT INTO t VALUES (3, 'c')");

            // `id > 1` must NOT hit the PK point-lookup fast path — it goes through the generic
            // machinery and deletes every matching row.
            db.ExecuteSQL("DELETE FROM t WHERE id > 1");

            Assert.Equal(2, db.GetLastChanges());
            var remaining = db.ExecuteQuery("SELECT * FROM t");
            Assert.Single(remaining);
            Assert.Equal(1, Convert.ToInt32(remaining[0]["id"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlDelete_NonIndexedColumn_FallsBackToFullScan()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");
            db.ExecuteSQL("INSERT INTO t VALUES (3, 'b')");

            db.ExecuteSQL("DELETE FROM t WHERE name = 'b'");

            Assert.Equal(2, db.GetLastChanges());
            var remaining = db.ExecuteQuery("SELECT * FROM t");
            Assert.Single(remaining);
            Assert.Equal(1, Convert.ToInt32(remaining[0]["id"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlDelete_Returning_ReturnsPreDeleteRows()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");

            var result = db.ExecuteQuery("DELETE FROM t WHERE id = 1 RETURNING id, name");

            Assert.Single(result);
            Assert.Equal(1, result[0]["id"]);
            Assert.Equal("a", result[0]["name"]);
            Assert.Single(db.ExecuteQuery("SELECT * FROM t"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlUpdate_ByPrimaryKey_AffectedCountIsOne()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");

            db.ExecuteSQL("UPDATE t SET name = 'z' WHERE id = 1");

            Assert.Equal(1, db.GetLastChanges());
            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal("z", row[0]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlUpdate_RangeWhere_AffectedCountIsTwo()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b')");
            db.ExecuteSQL("INSERT INTO t VALUES (3, 'c')");

            db.ExecuteSQL("UPDATE t SET name = 'x' WHERE id > 1");

            Assert.Equal(2, db.GetLastChanges());
            var rows = db.ExecuteQuery("SELECT * FROM t ORDER BY id");
            Assert.Equal("a", rows[0]["name"]);
            Assert.Equal("x", rows[1]["name"]);
            Assert.Equal("x", rows[2]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteBatchSQL_DeleteByPrimaryKey_DeletesRows()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            for (int i = 1; i <= 5; i++)
            {
                db.ExecuteSQL($"INSERT INTO t VALUES ({i}, 'n{i}')");
            }

            db.ExecuteBatchSQL(["DELETE FROM t WHERE id = 1", "DELETE FROM t WHERE id = 3"]);

            var remaining = db.ExecuteQuery("SELECT * FROM t ORDER BY id");
            Assert.Equal(3, remaining.Count);
            Assert.Equal(2, Convert.ToInt32(remaining[0]["id"]));
            Assert.Equal(4, Convert.ToInt32(remaining[1]["id"]));
            Assert.Equal(5, Convert.ToInt32(remaining[2]["id"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteBatchSQL_UpdateByPrimaryKey_UpdatesRows()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            for (int i = 1; i <= 3; i++)
            {
                db.ExecuteSQL($"INSERT INTO t VALUES ({i}, 'n{i}')");
            }

            db.ExecuteBatchSQL(["UPDATE t SET name = 'x' WHERE id = 2", "UPDATE t SET name = 'y' WHERE id = 3"]);

            var rows = db.ExecuteQuery("SELECT * FROM t ORDER BY id");
            Assert.Equal("n1", rows[0]["name"]);
            Assert.Equal("x", rows[1]["name"]);
            Assert.Equal("y", rows[2]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
