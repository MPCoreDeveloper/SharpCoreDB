// <copyright file="SingleFileWriteTests.cs" company="MPCoreDeveloper">
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
/// Issue A2: single-file (.scdb) write-path behavior. <see cref="SingleFileStorageProvider.WriteBlockAsync"/>
/// reuses a table's existing block offset when the new row-cache JSON fits the block's allocated
/// pages, so a fixed-length (same-size) update overwrites the block in place — the .scdb file must
/// not grow and the updated value must survive reopen.
/// </summary>
public sealed class SingleFileWriteTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _scdbPath;

    public SingleFileWriteTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_Write_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    private IDatabase CreateScdb() => _factory.CreateWithOptions(_scdbPath, "pw", DatabaseOptions.CreateSingleFileDefault());

    private long FileSize => new FileInfo(_scdbPath).Length;

    [Fact]
    public void SameLengthUpdate_OverwritesInPlace_FileDoesNotGrow()
    {
        var db = CreateScdb();
        try
        {
            // Single-digit integer column: every update serializes to the same JSON byte length,
            // so the block is overwritten at its existing offset (no relocation / no growth).
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, val INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 0)");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 0)");

            long sizeAfterInsert = FileSize;
            Assert.True(sizeAfterInsert > 0);

            for (int i = 0; i <= 9; i++)
            {
                db.ExecuteSQL($"UPDATE t SET val = {i} WHERE id = 1");
            }

            Assert.Equal(sizeAfterInsert, FileSize);

            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal(9, Convert.ToInt32(row[0]["val"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_ValueSurvivesReopen()
    {
        var db = CreateScdb();
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, val INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 0)");
            db.ExecuteSQL("UPDATE t SET val = 42 WHERE id = 1");
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        var db2 = CreateScdb();
        try
        {
            var row = db2.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal(42, Convert.ToInt32(row[0]["val"]));
        }
        finally
        {
            (db2 as IDisposable)?.Dispose();
        }
    }
}
