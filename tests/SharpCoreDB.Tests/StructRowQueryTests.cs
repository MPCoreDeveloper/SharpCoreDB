// <copyright file="StructRowQueryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests for the v2 zero-allocation <see cref="IDatabase.ExecuteQueryStruct"/> fast-path API.
/// </summary>
public class StructRowQueryTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseFactory _factory;

    public StructRowQueryTests()
    {
        // Create a unique test database path for each test instance.
        _testDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_Test_{Guid.NewGuid()}");

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        // Clean up test database after each test.
        if (Directory.Exists(_testDbPath))
        {
            Directory.Delete(_testDbPath, true);
        }
    }

    [Fact]
    public void ExecuteQueryStruct_PointLookup_ReturnsMatchingRow()
    {
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE docs (name TEXT, email TEXT, age INTEGER, score REAL, data TEXT)");
        db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");
        db.ExecuteSQL("INSERT INTO docs VALUES ('User0', 'u0@x.com', 30, 1.5, 'payload0')");
        db.ExecuteSQL("INSERT INTO docs VALUES ('User1', 'u1@x.com', 31, 2.5, 'payload1')");

        var rows = db.ExecuteQueryStruct(
            "SELECT * FROM docs WHERE name = @name",
            new Dictionary<string, object?> { ["@name"] = "User1" }).ToList();

        Assert.Single(rows);
        var row = rows[0];
        var columns = row.GetColumnNames();
        int nameIdx = Array.IndexOf(columns, "name");
        int emailIdx = Array.IndexOf(columns, "email");
        Assert.True(nameIdx >= 0, "name column should be present in StructRow schema.");
        Assert.True(emailIdx >= 0, "email column should be present in StructRow schema.");
        Assert.Equal("User1", row.GetValueBoxed(nameIdx));
        Assert.Equal("u1@x.com", row.GetValueBoxed(emailIdx));
    }

    [Fact]
    public void ExecuteQueryStruct_LiteralWhere_ReturnsMatchingRow()
    {
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE docs (name TEXT, email TEXT, age INTEGER, score REAL, data TEXT)");
        db.ExecuteSQL("INSERT INTO docs VALUES ('Alice', 'a@x.com', 30, 1.5, 'p0')");
        db.ExecuteSQL("INSERT INTO docs VALUES ('Bob', 'b@x.com', 31, 2.5, 'p1')");

        var rows = db.ExecuteQueryStruct("SELECT * FROM docs WHERE name = 'Bob'").ToList();

        Assert.Single(rows);
        var row = rows[0];
        var columns = row.GetColumnNames();
        int nameIdx = Array.IndexOf(columns, "name");
        Assert.Equal("Bob", row.GetValueBoxed(nameIdx));
    }

    [Fact]
    public void ExecuteQueryStruct_NoWhere_ReturnsAllRows()
    {
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE docs (name TEXT, email TEXT, age INTEGER, score REAL, data TEXT)");
        db.ExecuteSQL("INSERT INTO docs VALUES ('User0', 'u0@x.com', 30, 1.5, 'p0')");
        db.ExecuteSQL("INSERT INTO docs VALUES ('User1', 'u1@x.com', 31, 2.5, 'p1')");
        db.ExecuteSQL("INSERT INTO docs VALUES ('User2', 'u2@x.com', 32, 3.5, 'p2')");

        var rows = db.ExecuteQueryStruct("SELECT * FROM docs").ToList();

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ExecuteQueryStruct_NoMatch_ReturnsEmpty()
    {
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE docs (name TEXT, email TEXT, age INTEGER, score REAL, data TEXT)");
        db.ExecuteSQL("INSERT INTO docs VALUES ('User0', 'u0@x.com', 30, 1.5, 'p0')");

        var rows = db.ExecuteQueryStruct(
            "SELECT * FROM docs WHERE name = @name",
            new Dictionary<string, object?> { ["@name"] = "Missing" }).ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public void ExecuteQueryStruct_NumericWhere_FixedOffset_ReturnsMatchingRows()
    {
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE t (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'Alice')");
        db.ExecuteSQL("INSERT INTO t VALUES (2, 'Bob')");
        db.ExecuteSQL("INSERT INTO t VALUES (3, 'Carol')");

        // id is a fixed-width INTEGER at a constant per-record offset → SIMD batch filter path.
        var rows = db.ExecuteQueryStruct("SELECT * FROM t WHERE id = 2").ToList();

        Assert.Single(rows);
        var columns = rows[0].GetColumnNames();
        int idIdx = Array.IndexOf(columns, "id");
        int nameIdx = Array.IndexOf(columns, "name");
        Assert.Equal(2, rows[0].GetValueBoxed(idIdx));
        Assert.Equal("Bob", rows[0].GetValueBoxed(nameIdx));
    }

    [Fact]
    public void ExecuteQueryStruct_NumericWhere_NoMatch_ReturnsEmpty()
    {
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE t (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'Alice')");
        db.ExecuteSQL("INSERT INTO t VALUES (2, 'Bob')");

        var rows = db.ExecuteQueryStruct("SELECT * FROM t WHERE id = 999").ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public void ExecuteQueryStruct_ComplexQuery_ThrowsNotSupported()
    {
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE docs (name TEXT, email TEXT, age INTEGER)");
        db.ExecuteSQL("INSERT INTO docs VALUES ('User0', 'u0@x.com', 30)");

        Assert.Throws<NotSupportedException>(() =>
            db.ExecuteQueryStruct("SELECT * FROM docs WHERE age > 25").ToList());
    }
}
