// <copyright file="SqlInPlaceUpdateTests.cs" company="MPCoreDeveloper">
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
/// Issue #6: in-place UPDATE — a fixed-width (or unchanged-length) row update overwrites the
/// record in its existing storage slot instead of appending a new version, so the data file does
/// not grow and no stale version is left for compaction. Variable-width updates that change the
/// stored length fall back to the append path (correctness must be unchanged).
/// </summary>
public sealed class SqlInPlaceUpdateTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public SqlInPlaceUpdateTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_InPlaceUpd_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private long DataFileSize(string table) => new FileInfo(Path.Combine(_dirPath, $"{table}.dat")).Length;

    [Fact]
    public void SqlUpdate_FixedWidth_OverwritesInPlace_FileDoesNotGrow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE fw (id INTEGER PRIMARY KEY, val INTEGER, flag BOOLEAN)");
            db.ExecuteSQL("INSERT INTO fw VALUES (1, 100, 1)");
            db.ExecuteSQL("INSERT INTO fw VALUES (2, 200, 0)");

            long sizeAfterInsert = DataFileSize("fw");
            Assert.True(sizeAfterInsert > 0);

            // 50 in-place updates: a fixed-width row serializes to the same length every time,
            // so every update overwrites the existing slot — the file must not grow.
            for (int i = 0; i < 50; i++)
            {
                db.ExecuteSQL($"UPDATE fw SET val = {100 + i} WHERE id = 1");
            }

            Assert.Equal(sizeAfterInsert, DataFileSize("fw"));

            // Results are correct and the table still has exactly 2 rows.
            var rows = db.ExecuteQuery("SELECT * FROM fw WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal(149, rows[0]["val"]);
            Assert.Equal(2, db.ExecuteQuery("SELECT * FROM fw").Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlUpdate_Parameterized_FixedWidth_OverwritesInPlace()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE pw (id INTEGER PRIMARY KEY, val INTEGER)");
            db.ExecuteSQL("INSERT INTO pw VALUES (1, 10)");
            long sizeAfterInsert = DataFileSize("pw");

            for (int i = 0; i < 20; i++)
            {
                db.ExecuteSQL("UPDATE pw SET val = @p WHERE id = @id",
                    new Dictionary<string, object?> { ["@p"] = 10 + i, ["@id"] = 1 });
            }

            Assert.Equal(sizeAfterInsert, DataFileSize("pw"));
            Assert.Equal(29, db.ExecuteQuery("SELECT * FROM pw WHERE id = 1")[0]["val"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlUpdate_VariableWidth_GrowsWhenStoredLengthChanges_StillCorrect()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE vw (id INTEGER PRIMARY KEY, name TEXT, val INTEGER)");
            db.ExecuteSQL("INSERT INTO vw VALUES (1, 'short', 1)");
            long sizeAfterInsert = DataFileSize("vw");

            // Growing the string changes the stored record length → append fallback (file grows).
            db.ExecuteSQL("UPDATE vw SET name = 'a much longer name that no longer fits' WHERE id = 1");
            Assert.True(DataFileSize("vw") > sizeAfterInsert);

            var rows = db.ExecuteQuery("SELECT * FROM vw WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal("a much longer name that no longer fits", rows[0]["name"]);

            // Updating a fixed-width column with an UNCHANGED string length → in-place again.
            long sizeAfterGrow = DataFileSize("vw");
            db.ExecuteSQL("UPDATE vw SET val = 42 WHERE id = 1");
            Assert.Equal(sizeAfterGrow, DataFileSize("vw"));
            Assert.Equal(42, db.ExecuteQuery("SELECT * FROM vw WHERE id = 1")[0]["val"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
