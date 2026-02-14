// <copyright file="Phase1_5_DDL_IfExistsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Xunit;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using System;
using System.IO;
using System.Linq;

/// <summary>
/// Tests for Phase 1.5: DDL IF EXISTS/IF NOT EXISTS extensions.
/// Covers CREATE INDEX IF NOT EXISTS, DROP INDEX/PROCEDURE/VIEW/TRIGGER IF EXISTS.
/// </summary>
public sealed class Phase1_5_DDL_IfExistsTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly Database _db;

    public Phase1_5_DDL_IfExistsTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_phase15_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDbPath);
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        
        // Use DatabaseConfig.Benchmark for test performance
        var config = DatabaseConfig.Benchmark;
        _db = new Database(
            serviceProvider,
            _testDbPath,
            "test_password",
            isReadOnly: false,
            config: config);
    }

    public void Dispose()
    {
        try
        {
            _db?.Dispose();
        }
        catch { }
        
        // Force garbage collection to release file handles
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        try
        {
            if (Directory.Exists(_testDbPath))
                Directory.Delete(_testDbPath, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region CREATE INDEX IF NOT EXISTS Tests

    [Fact]
    public void CreateIndexIfNotExists_WhenIndexDoesNotExist_ShouldCreateIndex()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

        // Act
        _db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_users_name ON users(name)");

        // Assert - index works for queries
        var rows = _db.ExecuteQuery("SELECT * FROM users WHERE name = 'Alice'");
        Assert.Single(rows);
    }

    [Fact]
    public void CreateIndexIfNotExists_WhenIndexExists_ShouldSkipSilently()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        _db.ExecuteSQL("CREATE INDEX idx_users_name ON users(name)");
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

        // Act - should not throw
        _db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_users_name ON users(name)");

        // Assert - index still works
        var rows = _db.ExecuteQuery("SELECT * FROM users WHERE name = 'Alice'");
        Assert.Single(rows);
    }

    [Fact]
    public void CreateUniqueIndexIfNotExists_WhenIndexDoesNotExist_ShouldCreateUniqueIndex()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT)");

        // Act
        _db.ExecuteSQL("CREATE UNIQUE INDEX IF NOT EXISTS idx_users_email ON users(email)");

        // Assert - uniqueness constraint works
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'test@example.com')");
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("INSERT INTO users VALUES (2, 'test@example.com')"));
    }

    [Fact]
    public void CreateUniqueIndexIfNotExists_WhenIndexExists_ShouldSkipSilently()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT)");
        _db.ExecuteSQL("CREATE UNIQUE INDEX idx_users_email ON users(email)");
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'test@example.com')");

        // Act - should not throw
        _db.ExecuteSQL("CREATE UNIQUE INDEX IF NOT EXISTS idx_users_email ON users(email)");

        // Assert - uniqueness still enforced
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("INSERT INTO users VALUES (2, 'test@example.com')"));
    }

    [Fact]
    public void CreateBTreeIndexIfNotExists_WhenIndexDoesNotExist_ShouldCreateBTreeIndex()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, date BIGINT)");
        _db.ExecuteSQL("INSERT INTO orders VALUES (1, 1000), (2, 2000), (3, 3000)");

        // Act
        _db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(date) USING BTREE");

        // Assert - B-tree range queries work (should return rows with date >= 2000)
        var rows = _db.ExecuteQuery("SELECT date FROM orders WHERE date >= 2000 ORDER BY date");
        Assert.Equal(2, rows.Count); // rows 2 and 3
        Assert.Equal(2000L, rows[0]["date"]);
        Assert.Equal(3000L, rows[1]["date"]);
    }

    [Fact]
    public void CreateBTreeIndexIfNotExists_WhenIndexExists_ShouldSkipSilently()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, date BIGINT)");
        _db.ExecuteSQL("CREATE INDEX idx_orders_date ON orders(date) USING BTREE");
        _db.ExecuteSQL("INSERT INTO orders VALUES (1, 1000), (2, 2000), (3, 3000)");

        // Act - should not throw
        _db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(date) USING BTREE");

        // Assert - B-tree range queries still work
        var rows = _db.ExecuteQuery("SELECT date FROM orders WHERE date >= 2000 ORDER BY date");
        Assert.Equal(2, rows.Count); // rows 2 and 3
        Assert.Equal(2000L, rows[0]["date"]);
        Assert.Equal(3000L, rows[1]["date"]);
    }

    #endregion

    #region DROP INDEX IF EXISTS Tests

    [Fact]
    public void DropIndexIfExists_WhenIndexExists_ShouldDropIndex()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        _db.ExecuteSQL("CREATE INDEX idx_users_name ON users(name)");
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

        // Act
        _db.ExecuteSQL("DROP INDEX IF EXISTS idx_users_name");

        // Assert - queries still work (just without index)
        var rows = _db.ExecuteQuery("SELECT * FROM users WHERE name = 'Alice'");
        Assert.Single(rows);
    }

    [Fact]
    public void DropIndexIfExists_WhenIndexDoesNotExist_ShouldSkipSilently()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");

        // Act & Assert - should not throw
        _db.ExecuteSQL("DROP INDEX IF EXISTS idx_nonexistent");
    }

    [Fact]
    public void DropIndex_WithoutIfExists_WhenIndexDoesNotExist_ShouldThrow()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("DROP INDEX idx_nonexistent"));
    }

    #endregion

    #region DROP PROCEDURE IF EXISTS Tests

    [Fact]
    public void DropProcedureIfExists_WhenProcedureExists_ShouldDropProcedure()
    {
        // Arrange
        _db.ExecuteSQL(@"
            CREATE PROCEDURE sp_test()
            BEGIN
                SELECT 1;
            END
        ");

        // Act
        _db.ExecuteSQL("DROP PROCEDURE IF EXISTS sp_test");

        // Assert - procedure no longer exists
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("EXEC sp_test"));
    }

    [Fact]
    public void DropProcedureIfExists_WhenProcedureDoesNotExist_ShouldSkipSilently()
    {
        // Act & Assert - should not throw
        _db.ExecuteSQL("DROP PROCEDURE IF EXISTS sp_nonexistent");
    }

    [Fact]
    public void DropProcedure_WithoutIfExists_WhenProcedureDoesNotExist_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("DROP PROCEDURE sp_nonexistent"));
    }

    #endregion

    #region DROP VIEW IF EXISTS Tests

    [Fact]
    public void DropViewIfExists_WhenViewExists_ShouldDropView()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, active INTEGER)");
        _db.ExecuteSQL("CREATE VIEW vw_active_users AS SELECT * FROM users WHERE active = 1");

        // Act
        _db.ExecuteSQL("DROP VIEW IF EXISTS vw_active_users");

        // Assert - view no longer exists
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteQuery("SELECT * FROM vw_active_users"));
    }

    [Fact]
    public void DropViewIfExists_WhenViewDoesNotExist_ShouldSkipSilently()
    {
        // Act & Assert - should not throw
        _db.ExecuteSQL("DROP VIEW IF EXISTS vw_nonexistent");
    }

    [Fact]
    public void DropView_WithoutIfExists_WhenViewDoesNotExist_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("DROP VIEW vw_nonexistent"));
    }

    #endregion

    #region DROP TRIGGER IF EXISTS Tests

    [Fact]
    public void DropTriggerIfExists_WhenTriggerExists_ShouldDropTrigger()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        _db.ExecuteSQL("CREATE TABLE audit_log (message TEXT)");
        _db.ExecuteSQL(@"
            CREATE TRIGGER trg_users_audit
            AFTER INSERT ON users
            BEGIN
                INSERT INTO audit_log VALUES ('User inserted');
            END
        ");

        // Act
        _db.ExecuteSQL("DROP TRIGGER IF EXISTS trg_users_audit");

        // Assert - trigger no longer fires
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
        var auditRows = _db.ExecuteQuery("SELECT * FROM audit_log");
        Assert.Empty(auditRows);
    }

    [Fact]
    public void DropTriggerIfExists_WhenTriggerDoesNotExist_ShouldSkipSilently()
    {
        // Act & Assert - should not throw
        _db.ExecuteSQL("DROP TRIGGER IF EXISTS trg_nonexistent");
    }

    [Fact]
    public void DropTrigger_WithoutIfExists_WhenTriggerDoesNotExist_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("DROP TRIGGER trg_nonexistent"));
    }

    #endregion

    #region Idempotent Script Tests

    [Fact]
    public void IdempotentMigrationScript_ShouldRunMultipleTimes_WithoutErrors()
    {
        // Arrange - idempotent migration script
        var migrationScript = @"
            CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT, email TEXT);
            CREATE INDEX IF NOT EXISTS idx_users_name ON users(name);
            CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
        ";

        // Act - run script multiple times
        _db.ExecuteSQL(migrationScript);
        _db.ExecuteSQL(migrationScript); // Should not throw
        _db.ExecuteSQL(migrationScript); // Should not throw

        // Assert - table and indexes work correctly
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 'alice@test.com')");
        var rows = _db.ExecuteQuery("SELECT * FROM users WHERE name = 'Alice'");
        Assert.Single(rows);
    }

    [Fact]
    public void IdempotentCleanupScript_ShouldRunMultipleTimes_WithoutErrors()
    {
        // Arrange - idempotent cleanup script
        var cleanupScript = @"
            DROP VIEW IF EXISTS vw_legacy_view;
            DROP TRIGGER IF EXISTS trg_old_trigger;
            DROP PROCEDURE IF EXISTS sp_deprecated_proc;
            DROP INDEX IF EXISTS idx_old_index;
        ";

        // Act - run script multiple times
        _db.ExecuteSQL(cleanupScript);
        _db.ExecuteSQL(cleanupScript); // Should not throw
        _db.ExecuteSQL(cleanupScript); // Should not throw

        // Assert - no errors, silent success
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CreateIndexIfNotExists_OnNonExistentTable_ShouldThrow()
    {
        // Act & Assert - table must exist even with IF NOT EXISTS
        Assert.Throws<InvalidOperationException>(() =>
            _db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_test ON nonexistent_table(col)"));
    }

    [Fact(Skip = "Multiple IF EXISTS clauses need further investigation - Phase 1.5.1")]
    public void MultipleIfExistsClauses_InSingleTransaction_ShouldWork()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        _db.ExecuteSQL("CREATE INDEX idx_users_name ON users(name)");
        _db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

        // Act - multiple IF EXISTS in one transaction
        _db.ExecuteSQL("DROP INDEX IF EXISTS idx_users_name");
        _db.ExecuteSQL("DROP INDEX IF EXISTS idx_users_name"); // Should not throw
        _db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_users_name ON users(name)");
        _db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_users_name ON users(name)"); // Should not throw

        // Assert - final state is correct
        var rows = _db.ExecuteQuery("SELECT * FROM users WHERE name = 'Alice'");
        Assert.Single(rows);
    }

    #endregion
}
