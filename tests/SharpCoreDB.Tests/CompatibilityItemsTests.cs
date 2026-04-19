// <copyright file="CompatibilityItemsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Tests covering the SQLite/PostgreSQL compatibility checklist items.
/// Each test corresponds to a checklist entry in SQLITE_POSTGRESQL_COMPATIBILITY_TODO.md.
/// </summary>
public class CompatibilityItemsTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseFactory _factory;
    private bool _disposed;

    public CompatibilityItemsTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_Compat_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Directory.Exists(_testDbPath))
                Directory.Delete(_testDbPath, recursive: true);
        }
        catch { /* ignore cleanup failures */ }
        GC.SuppressFinalize(this);
    }

    private IDatabase CreateDb()
        => _factory.Create(_testDbPath + "_" + Guid.NewGuid(), "pw");

    // ─── W3-1 · CHECK constraint enforcement ───────────────────────────────

    [Fact]
    public void CheckConstraint_InsertViolation_ThrowsException()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE products (id INT PRIMARY KEY, price REAL CHECK (price > 0))");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("INSERT INTO products VALUES (1, -5.0)"));
        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckConstraint_InsertValid_Succeeds()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE products (id INT PRIMARY KEY, price REAL CHECK (price > 0))");

        // Act
        db.ExecuteSQL("INSERT INTO products VALUES (1, 9.99)");
        var rows = db.ExecuteQuery("SELECT * FROM products WHERE id = 1");

        // Assert
        Assert.Single(rows);
    }

    [Fact]
    public void CheckConstraint_UpdateViolation_ThrowsException()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE products (id INT PRIMARY KEY, price REAL CHECK (price > 0))");
        db.ExecuteSQL("INSERT INTO products VALUES (1, 9.99)");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => db.ExecuteSQL("UPDATE products SET price = -1.0 WHERE id = 1"));
        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckConstraint_UpdateValid_Succeeds()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE products (id INT PRIMARY KEY, price REAL CHECK (price > 0))");
        db.ExecuteSQL("INSERT INTO products VALUES (1, 9.99)");

        // Act
        db.ExecuteSQL("UPDATE products SET price = 19.99 WHERE id = 1");
        var rows = db.ExecuteQuery("SELECT * FROM products WHERE id = 1");

        // Assert
        Assert.Single(rows);
        Assert.Equal(19.99, Convert.ToDouble(rows[0]["price"]), precision: 2);
    }

    // ─── W3-3 · RETURNING clause ───────────────────────────────────────────

    [Fact]
    public void Returning_InsertWithId_ReturnsInsertedRow()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE items (id INT PRIMARY KEY, name TEXT)");

        // Act
        var result = db.ExecuteQuery("INSERT INTO items VALUES (42, 'Widget') RETURNING id, name");

        // Assert
        Assert.Single(result);
        Assert.Equal(42, Convert.ToInt32(result[0]["id"]));
        Assert.Equal("Widget", result[0]["name"]);
    }

    [Fact]
    public void Returning_UpdateWithStar_ReturnsUpdatedRow()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE items (id INT PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("INSERT INTO items VALUES (1, 'OldName')");

        // Act
        var result = db.ExecuteQuery("UPDATE items SET name = 'NewName' WHERE id = 1 RETURNING *");

        // Assert
        Assert.Single(result);
        Assert.Equal("NewName", result[0]["name"]);
    }

    [Fact]
    public void Returning_DeleteReturnsDeletedRow()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE items (id INT PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("INSERT INTO items VALUES (1, 'Doomed')");

        // Act
        var result = db.ExecuteQuery("DELETE FROM items WHERE id = 1 RETURNING id, name");

        // Assert
        Assert.Single(result);
        Assert.Equal(1, Convert.ToInt32(result[0]["id"]));
    }

    // ─── W3-4 · CHANGES() / TOTAL_CHANGES() / LAST_INSERT_ROWID() ─────────

    [Fact]
    public void Changes_AfterInsert_ReturnsExpectedCount()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE t (id INT PRIMARY KEY, v TEXT)");
        db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");

        // Act
        var result = db.ExecuteQuery("SELECT CHANGES() AS c, TOTAL_CHANGES() AS t");

        // Assert
        Assert.Single(result);
        Assert.Equal(1, Convert.ToInt32(result[0]["c"]));
        Assert.True(Convert.ToInt32(result[0]["t"]) >= 1);
    }

    [Fact]
    public void LastInsertRowId_AfterInsert_ReturnsRowId()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE t (id INT PRIMARY KEY, v TEXT)");
        db.ExecuteSQL("INSERT INTO t VALUES (5, 'hello')");

        // Act
        var result = db.ExecuteQuery("SELECT LAST_INSERT_ROWID() AS rid");

        // Assert
        Assert.Single(result);
        Assert.True(Convert.ToInt64(result[0]["rid"]) > 0);
    }

    // ─── W3-5 · DEFAULT expression evaluation ─────────────────────────────

    [Fact]
    public void DefaultExpression_InsertWithOmittedDefaultColumn_UsesDefaultValue()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE logs (id INT PRIMARY KEY, msg TEXT, created_at TEXT DEFAULT (strftime('%Y-%m-%d', 'now')))");

        // Act
        db.ExecuteSQL("INSERT INTO logs (id, msg) VALUES (1, 'hello')");
        var rows = db.ExecuteQuery("SELECT * FROM logs WHERE id = 1");

        // Assert
        Assert.Single(rows);
        Assert.NotNull(rows[0]["created_at"]);
    }

    // ─── W4-2 · INSERT INTO … SELECT ──────────────────────────────────────

    [Fact]
    public void InsertIntoSelect_CopiesRowsFromSource()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE src (id INT PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE TABLE dst (id INT PRIMARY KEY, name TEXT)");
        db.ExecuteBatchSQL([
            "INSERT INTO src VALUES (1, 'Alice')",
            "INSERT INTO src VALUES (2, 'Bob')",
        ]);

        // Act
        db.ExecuteSQL("INSERT INTO dst SELECT id, name FROM src");
        var rows = db.ExecuteQuery("SELECT * FROM dst ORDER BY id");

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal("Bob", rows[1]["name"]);
    }

    // ─── W4-3 · Date/time functions ───────────────────────────────────────

    [Fact]
    public void JulianDay_ReturnsNumericValue()
    {
        // Arrange
        var db = CreateDb();

        // Act
        var result = db.ExecuteQuery("SELECT JULIANDAY('2000-01-01') AS jd");

        // Assert
        Assert.Single(result);
        var jd = Convert.ToDouble(result[0]["jd"]);
        Assert.InRange(jd, 2451544.0, 2451546.0);
    }

    [Fact]
    public void UnixEpoch_ReturnsSeconds()
    {
        // Arrange
        var db = CreateDb();

        // Act
        var result = db.ExecuteQuery("SELECT UNIXEPOCH('2000-01-01T00:00:00') AS ts");

        // Assert
        Assert.Single(result);
        var ts = Convert.ToInt64(result[0]["ts"]);
        Assert.Equal(946684800L, ts);
    }

    [Fact]
    public void Strftime_StartOfMonth_ReturnsFirstDay()
    {
        // Arrange
        var db = CreateDb();

        // Act
        var result = db.ExecuteQuery("SELECT strftime('%Y-%m-%d', '2024-07-15', 'start of month') AS d");

        // Assert
        Assert.Single(result);
        Assert.Equal("2024-07-01", result[0]["d"]);
    }

    // ─── W4-4 · NOT REGEXP ────────────────────────────────────────────────

    [Fact]
    public void NotRegexp_FiltersNonMatchingRows()
    {
        // Arrange
        var db = CreateDb();
        db.ExecuteSQL("CREATE TABLE words (w TEXT)");
        db.ExecuteBatchSQL([
            "INSERT INTO words VALUES ('apple')",
            "INSERT INTO words VALUES ('banana')",
            "INSERT INTO words VALUES ('cherry')",
        ]);

        // Act
        var rows = db.ExecuteQuery("SELECT w FROM words WHERE w NOT REGEXP '^b'");

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r["w"].ToString() == "banana");
    }

    // ─── W2-5 · WITH RECURSIVE ────────────────────────────────────────────

    [Fact]
    public void WithRecursive_CountingSequence_ReturnsNumbers()
    {
        // Arrange
        var db = CreateDb();

        // Act
        var rows = db.ExecuteQuery(
            "WITH RECURSIVE cnt(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM cnt WHERE n < 5) SELECT n FROM cnt");

        // Assert
        Assert.Equal(5, rows.Count);
        Assert.Equal(1, Convert.ToInt32(rows[0]["n"]));
        Assert.Equal(5, Convert.ToInt32(rows[4]["n"]));
    }
}
