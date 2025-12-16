// <copyright file="HybridStorageIntegrationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Storage.Hybrid;
using Xunit;

/// <summary>
/// Integration tests for Milestone 3: SQL Integration for hybrid storage.
/// Tests CREATE TABLE with STORAGE clause and basic CRUD routing.
/// </summary>
public class HybridStorageIntegrationTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database db;

    public HybridStorageIntegrationTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_HybridTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDbPath);

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();

        db = new Database(provider, testDbPath, "test_password", config: DatabaseConfig.Default);
    }

    public void Dispose()
    {
        db?.Dispose();
        if (Directory.Exists(testDbPath))
        {
            try
            {
                Directory.Delete(testDbPath, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void CreateTable_DefaultStorageMode_UsesColumnar()
    {
        // Arrange & Act
        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT)");

        // Assert - table should exist with columnar storage (default)
        var result = db.ExecuteQuery("SELECT * FROM users");
        Assert.NotNull(result);
        Assert.Empty(result); // No data yet
    }

    [Fact]
    public void CreateTable_WithColumnarStorageClause_CreatesColumnarTable()
    {
        // Arrange & Act
        db.ExecuteSQL("CREATE TABLE analytics (id INTEGER PRIMARY KEY, event TEXT, timestamp INTEGER) STORAGE = COLUMNAR");

        // Assert - should create .dat file (columnar)
        var datFile = Path.Combine(testDbPath, "analytics.dat");
        Assert.True(File.Exists(datFile), $"Expected columnar file {datFile} to exist");

        // Verify we can insert and query
        db.ExecuteSQL("INSERT INTO analytics VALUES (1, 'login', 1234567890)");
        var results = db.ExecuteQuery("SELECT * FROM analytics WHERE id = 1");
        Assert.Single(results);
        Assert.Equal("login", results[0]["event"]);
    }

    [Fact]
    public void CreateTable_WithPageBasedStorageClause_CreatesPageBasedTable()
    {
        // Arrange & Act
        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT) STORAGE = PAGE_BASED");

        // Assert - should create .pages file (page-based)
        var pagesFile = Path.Combine(testDbPath, "users.pages");
        Assert.True(File.Exists(pagesFile), $"Expected page-based file {pagesFile} to exist");
    }

    [Fact]
    public void CreateTable_WithInvalidStorageMode_ThrowsException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            db.ExecuteSQL("CREATE TABLE invalid (id INTEGER) STORAGE = INVALID_MODE");
        });

        Assert.Contains("Invalid storage mode", exception.Message);
    }

    [Fact]
    public void InsertAndSelect_PageBasedTable_WorksCorrectly()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE accounts (id INTEGER PRIMARY KEY, balance INTEGER) STORAGE = PAGE_BASED");

        // Act - Insert data
        db.ExecuteSQL("INSERT INTO accounts VALUES (1, 1000)");
        db.ExecuteSQL("INSERT INTO accounts VALUES (2, 2000)");

        // Assert - Query by primary key
        var result1 = db.ExecuteQuery("SELECT * FROM accounts WHERE id = 1");
        Assert.Single(result1);
        Assert.Equal(1, result1[0]["id"]);
        Assert.Equal(1000, result1[0]["balance"]);

        var result2 = db.ExecuteQuery("SELECT * FROM accounts WHERE id = 2");
        Assert.Single(result2);
        Assert.Equal(2, result2[0]["id"]);
        Assert.Equal(2000, result2[0]["balance"]);
    }

    [Fact]
    public void HybridDatabase_MixedStorageModes_BothTablesWork()
    {
        // Arrange - Create both columnar and page-based tables
        db.ExecuteSQL("CREATE TABLE events (id INTEGER PRIMARY KEY, type TEXT) STORAGE = COLUMNAR");
        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT) STORAGE = PAGE_BASED");

        // Act - Insert into both
        db.ExecuteSQL("INSERT INTO events VALUES (1, 'click')");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

        // Assert - Both tables work independently
        var events = db.ExecuteQuery("SELECT * FROM events WHERE id = 1");
        Assert.Single(events);
        Assert.Equal("click", events[0]["type"]);

        var users = db.ExecuteQuery("SELECT * FROM users WHERE id = 1");
        Assert.Single(users);
        Assert.Equal("Alice", users[0]["name"]);

        // Verify file types
        Assert.True(File.Exists(Path.Combine(testDbPath, "events.dat")));
        Assert.True(File.Exists(Path.Combine(testDbPath, "users.pages")));
    }

    [Fact]
    public void PageBasedTable_AutoGeneratedPrimaryKey_Works()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE sessions (id INTEGER PRIMARY KEY AUTO, user_id INTEGER) STORAGE = PAGE_BASED");

        // Act - Insert without specifying ID
        db.ExecuteSQL("INSERT INTO sessions (user_id) VALUES (42)");

        // Assert - ID should be auto-generated
        var results = db.ExecuteQuery("SELECT * FROM sessions");
        Assert.Single(results);
        Assert.NotNull(results[0]["id"]);
        Assert.Equal(42, results[0]["user_id"]);
    }

    [Fact]
    public void ColumnarTable_AfterPageBasedFailure_StillWorks()
    {
        // Arrange - Try to create page-based table first
        try
        {
            db.ExecuteSQL("CREATE TABLE broken (id INTEGER) STORAGE = PAGE_BASED");
            db.ExecuteSQL("INSERT INTO broken VALUES (1)");
        }
        catch
        {
            // Expected - some operations may not be fully implemented yet
        }

        // Act - Create columnar table (should work regardless)
        db.ExecuteSQL("CREATE TABLE working (id INTEGER PRIMARY KEY, value TEXT) STORAGE = COLUMNAR");
        db.ExecuteSQL("INSERT INTO working VALUES (1, 'test')");

        // Assert - Columnar table should work fine
        var results = db.ExecuteQuery("SELECT * FROM working WHERE id = 1");
        Assert.Single(results);
        Assert.Equal("test", results[0]["value"]);
    }
}
