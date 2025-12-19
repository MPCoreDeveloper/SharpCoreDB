// <copyright file="PageBasedSelectTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Core;
using SharpCoreDB.DataStructures;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tests for PageBased storage SELECT operations.
/// Verifies that full table scans, WHERE clauses, UPDATE, and DELETE work correctly.
/// </summary>
public class PageBasedSelectTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database db;

    public PageBasedSelectTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), "TestPageBased_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDbPath);
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        
        db = new Database(provider, testDbPath, "test_password", config: DatabaseConfig.Default);
    }

    public void Dispose()
    {
        try
        {
            db?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
        
        // Give OS time to release file locks
        System.Threading.Thread.Sleep(100);
        
        if (Directory.Exists(testDbPath))
        {
            try
            {
                Directory.Delete(testDbPath, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void PageBased_SelectAll_ReturnsAllRecords()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name STRING, Age INTEGER) STORAGE = PAGE_BASED");
        db.ExecuteSQL("INSERT INTO Users VALUES (1, 'Alice', 25)");
        db.ExecuteSQL("INSERT INTO Users VALUES (2, 'Bob', 30)");
        db.ExecuteSQL("INSERT INTO Users VALUES (3, 'Charlie', 35)");

        // Act
        var results = db.ExecuteQuery("SELECT * FROM Users");

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r["Name"].ToString() == "Alice");
        Assert.Contains(results, r => r["Name"].ToString() == "Bob");
        Assert.Contains(results, r => r["Name"].ToString() == "Charlie");
    }

    [Fact]
    public void PageBased_SelectWithPrimaryKey_ReturnsCorrectRecord()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name STRING, Age INTEGER) STORAGE = PAGE_BASED");
        db.ExecuteSQL("INSERT INTO Users VALUES (1, 'Alice', 25)");
        db.ExecuteSQL("INSERT INTO Users VALUES (2, 'Bob', 30)");
        db.ExecuteSQL("INSERT INTO Users VALUES (3, 'Charlie', 35)");

        // Act
        var results = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 2");

        // Assert
        Assert.Single(results);
        Assert.Equal("Bob", results[0]["Name"].ToString());
        Assert.Equal(30, results[0]["Age"]);
    }

    [Fact]
    public void PageBased_SelectWithWhereClause_FiltersCorrectly()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name STRING, Age INTEGER) STORAGE = PAGE_BASED");
        db.ExecuteSQL("INSERT INTO Users VALUES (1, 'Alice', 25)");
        db.ExecuteSQL("INSERT INTO Users VALUES (2, 'Bob', 30)");
        db.ExecuteSQL("INSERT INTO Users VALUES (3, 'Charlie', 35)");
        db.ExecuteSQL("INSERT INTO Users VALUES (4, 'David', 40)");

        // Act
        var results = db.ExecuteQuery("SELECT * FROM Users WHERE Age > 30");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r["Name"].ToString() == "Charlie");
        Assert.Contains(results, r => r["Name"].ToString() == "David");
        Assert.DoesNotContain(results, r => r["Name"].ToString() == "Alice");
        Assert.DoesNotContain(results, r => r["Name"].ToString() == "Bob");
    }

    [Fact]
    public void PageBased_Update_ModifiesRecord()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name STRING, Age INTEGER) STORAGE = PAGE_BASED");
        db.ExecuteSQL("INSERT INTO Users VALUES (1, 'Alice', 25)");
        db.ExecuteSQL("INSERT INTO Users VALUES (2, 'Bob', 30)");

        // Act
        db.ExecuteSQL("UPDATE Users SET Age = 31 WHERE Id = 2");
        var results = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 2");

        // Assert
        Assert.Single(results);
        Assert.Equal(31, results[0]["Age"]);
    }

    [Fact]
    public void PageBased_Delete_RemovesRecord()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name STRING, Age INTEGER) STORAGE = PAGE_BASED");
        db.ExecuteSQL("INSERT INTO Users VALUES (1, 'Alice', 25)");
        db.ExecuteSQL("INSERT INTO Users VALUES (2, 'Bob', 30)");
        db.ExecuteSQL("INSERT INTO Users VALUES (3, 'Charlie', 35)");

        // Act
        db.ExecuteSQL("DELETE FROM Users WHERE Id = 2");
        var results = db.ExecuteQuery("SELECT * FROM Users");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r["Name"].ToString() == "Bob");
    }

    [Fact]
    public void PageBased_BatchInsert_AllRecordsRetrievable()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name STRING, Age INTEGER) STORAGE = PAGE_BASED");
        
        // Insert 100 records
        for (int i = 1; i <= 100; i++)
        {
            db.ExecuteSQL($"INSERT INTO Users VALUES ({i}, 'User{i}', {20 + (i % 50)})");
        }

        // Act
        var results = db.ExecuteQuery("SELECT * FROM Users");

        // Assert
        Assert.Equal(100, results.Count);
        
        // Verify first and last
        Assert.Contains(results, r => r["Name"].ToString() == "User1");
        Assert.Contains(results, r => r["Name"].ToString() == "User100");
    }
}
