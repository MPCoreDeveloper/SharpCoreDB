// <copyright file="FixedWidthPatchTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Fixed-width layout step (SQLite-style UPDATE): when the row's storage position is known
/// (PK B-tree or hash index), the columnar UPDATE path patches only the updated fields at their
/// actual offsets in the existing record instead of deserializing → mutating → re-serializing the
/// whole row. A fixed-size field keeps the record length unchanged, so the write is an in-place
/// overwrite (Issue #6) and the data file does not grow. Variable-length fields that change size
/// fall back to the append path (correctness unchanged).
/// </summary>
public sealed class FixedWidthPatchTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FixedWidthPatchTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_FixedWidth_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private long DataFileSize(string table) => new FileInfo(Path.Combine(_dirPath, $"{table}.dat")).Length;

    [Fact]
    public void Update_FixedFieldAfterVariableColumns_PatchesInPlace_FileDoesNotGrow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            // No primary key — positions come from the hash index. `score` (REAL) and `age`
            // (INTEGER) sit AFTER two variable-length TEXT columns, so the patch must discover
            // their real offsets by walking the record.
            db.ExecuteSQL("CREATE TABLE t (name TEXT, email TEXT, age INTEGER, score REAL, data TEXT)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User0', 'u0@test.com', 20, 0.0, 'payload-0')");
            db.ExecuteSQL("INSERT INTO t VALUES ('User1', 'u1@test.com', 30, 1.0, 'payload-1')");

            long sizeAfterInsert = DataFileSize("t");
            Assert.True(sizeAfterInsert > 0);

            // 50 in-place updates of fixed-size fields (length never changes → no append, no growth).
            // InvariantCulture: interpolated doubles must use '.' so the SQL parser reads the
            // decimal point (the dev machine locale uses ',' otherwise).
            for (int i = 0; i < 50; i++)
            {
                var scoreValue = (0.5 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
                db.ExecuteSQL($"UPDATE t SET score = {scoreValue} WHERE name = 'User0'");
            }

            db.ExecuteSQL("UPDATE t SET age = 42 WHERE name = 'User1'");

            Assert.Equal(sizeAfterInsert, DataFileSize("t"));

            var row0 = db.ExecuteQuery("SELECT * FROM t WHERE name = 'User0'");
            Assert.Single(row0);
            Assert.Equal(49.5, Convert.ToDouble(row0[0]["score"]));
            Assert.Equal(20, Convert.ToInt32(row0[0]["age"]));

            var row1 = db.ExecuteQuery("SELECT * FROM t WHERE name = 'User1'");
            Assert.Single(row1);
            Assert.Equal(42, Convert.ToInt32(row1[0]["age"]));
            Assert.Equal(1.0, Convert.ToDouble(row1[0]["score"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_ByPrimaryKey_FixedField_PatchesInPlace_FileDoesNotGrow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, email TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'User0', 'u0@test.com', 0.0)");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'User1', 'u1@test.com', 1.0)");

            long sizeAfterInsert = DataFileSize("t");

            db.ExecuteSQL("UPDATE t SET score = 77.5 WHERE id = 1");

            Assert.Equal(sizeAfterInsert, DataFileSize("t"));
            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal(77.5, Convert.ToDouble(row[0]["score"]));
            Assert.Equal("User0", row[0]["name"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_VariableFieldGrows_FallsBackToAppend_ValueCorrect()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'short')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'other')");

            // name grows: the patch cannot fit the field, so the update must fall back to the
            // append path — the value must still be correct and the row readable.
            db.ExecuteSQL("UPDATE t SET name = 'this is a much longer name value' WHERE id = 1");

            var row = db.ExecuteQuery("SELECT * FROM t WHERE id = 1");
            Assert.Single(row);
            Assert.Equal("this is a much longer name value", row[0]["name"]);
            Assert.Equal(2, db.ExecuteQuery("SELECT * FROM t").Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Update_CompoundWhere_FallsBackToSelectInternal_StillCorrect()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, age INTEGER, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User0', 20, 1.0)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User0', 30, 2.0)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User1', 40, 3.0)");

            // Compound WHERE: must NOT resolve through the hash index only — every matching row
            // gets the same score.
            db.ExecuteSQL("UPDATE t SET score = 9.0 WHERE name = 'User0' AND age > 25");

            var rows = db.ExecuteQuery("SELECT * FROM t ORDER BY age");
            Assert.Equal(1.0, Convert.ToDouble(rows[0]["score"]));
            Assert.Equal(9.0, Convert.ToDouble(rows[1]["score"]));
            Assert.Equal(3.0, Convert.ToDouble(rows[2]["score"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteBatchSQL_Update_FixedFieldAfterVariableColumns_Correct()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, email TEXT, age INTEGER, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            for (int i = 0; i < 4; i++)
            {
                db.ExecuteSQL($"INSERT INTO t VALUES ('User{i}', 'u{i}@test.com', {10 + i}, {i}.0)");
            }

            db.ExecuteBatchSQL([
                "UPDATE t SET score = 50.5 WHERE name = 'User1'",
                "UPDATE t SET age = 99 WHERE name = 'User2'"
            ]);

            // ORDER BY age after the updates: User0=10, User1=11, User3=13, User2=99.
            var rows = db.ExecuteQuery("SELECT * FROM t ORDER BY age");
            Assert.Equal(50.5, Convert.ToDouble(rows[1]["score"]));
            Assert.Equal(99, Convert.ToInt32(rows[3]["age"]));
            Assert.Equal(2.0, Convert.ToDouble(rows[3]["score"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
