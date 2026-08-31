// <copyright file="SingleFilePkIndexTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// Issue A1: single-file (.scdb) primary-key hash index. <c>FindByPrimaryKey</c> and
/// <c>SELECT … WHERE pk = value</c> resolve through the PK index (O(1)) instead of an O(N) cache
/// scan; the index is maintained on INSERT / UPDATE (including PK changes) / DELETE and rebuilt
/// when the row cache is loaded (reopen).
/// </summary>
public sealed class SingleFilePkIndexTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _scdbPath;

    public SingleFilePkIndexTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_PkIndex_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    private IDatabase CreateScdb() => _factory.CreateWithOptions(_scdbPath, "pw", DatabaseOptions.CreateSingleFileDefault());

    private static void Seed(IDatabase db)
    {
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'a', 1.0)");
        db.ExecuteSQL("INSERT INTO t VALUES (2, 'b', 2.0)");
        db.ExecuteSQL("INSERT INTO t VALUES (3, 'c', 3.0)");
    }

    [Fact]
    public void FindByPrimaryKey_ReturnsRow()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
            var row = db.FindByPrimaryKey("t", 2);
            Assert.NotNull(row);
            Assert.Equal("b", row!["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void FindByPrimaryKey_NonExistent_ReturnsNull()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
            Assert.Null(db.FindByPrimaryKey("t", 999));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Select_ByPrimaryKey_ReturnsRow()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
            var rows = db.ExecuteQuery("SELECT * FROM t WHERE id = 2");
            Assert.Single(rows);
            Assert.Equal("b", rows[0]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Select_ByPrimaryKey_NonExistent_ReturnsEmpty()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
            Assert.Empty(db.ExecuteQuery("SELECT * FROM t WHERE id = 999"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_ByPrimaryKey_MaintainsIndex()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
            db.ExecuteSQL("UPDATE t SET name = 'zzz' WHERE id = 2");

            Assert.Equal("zzz", db.FindByPrimaryKey("t", 2)!["name"]);
            Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 2"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_ChangingPk_Reindexes()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
            db.ExecuteSQL("UPDATE t SET id = 99 WHERE id = 2");

            Assert.Null(db.FindByPrimaryKey("t", 2));
            Assert.Equal("b", db.FindByPrimaryKey("t", 99)!["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Delete_ByPrimaryKey_MaintainsIndex()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
            db.ExecuteSQL("DELETE FROM t WHERE id = 2");

            Assert.Null(db.FindByPrimaryKey("t", 2));
            Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Index_SurvivesReopen()
    {
        var db = CreateScdb();
        try
        {
            Seed(db);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        var db2 = CreateScdb();
        try
        {
            Assert.Equal("b", db2.FindByPrimaryKey("t", 2)!["name"]);
            Assert.Single(db2.ExecuteQuery("SELECT * FROM t WHERE id = 3"));
        }
        finally
        {
            (db2 as IDisposable)?.Dispose();
        }
    }
}
