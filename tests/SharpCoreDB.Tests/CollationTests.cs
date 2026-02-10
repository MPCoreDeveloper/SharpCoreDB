// <copyright file="CollationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Unit tests for COLLATE support (Phase 3):
/// - DDL parsing of COLLATE clause
/// - Case-insensitive queries with NOCASE collation
/// - Collation-aware string comparisons
/// - Metadata persistence and retrieval
/// </summary>
public sealed class CollationTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database db;

    public CollationTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"collation_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        var config = DatabaseConfig.Benchmark;
        db = new Database(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                .BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSharpCoreDB()),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: config);
    }

    public void Dispose()
    {
        try
        {
            db.Dispose();
        }
        catch { }

        // Force garbage collection to release file handles
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void CreateTable_WithCollateNoCase_ShouldParseSuccessfully()
    {
        // Arrange & Act
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO, Name TEXT COLLATE NOCASE, Email TEXT COLLATE NOCASE)");

        // Assert
        var tables = db.GetTables();
        Assert.Contains(tables, t => t.Name == "Users");

        var columns = db.GetColumns("Users");
        var nameColumn = columns.FirstOrDefault(c => c.Name == "Name");
        var emailColumn = columns.FirstOrDefault(c => c.Name == "Email");

        Assert.NotNull(nameColumn);
        Assert.NotNull(emailColumn);
        Assert.Equal("NOCASE", nameColumn.Collation);
        Assert.Equal("NOCASE", emailColumn.Collation);
    }

    [Fact]
    public void CreateTable_WithCollateBinary_ShouldUseDefaultCollation()
    {
        // Arrange & Act
        db.ExecuteSQL("CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTO, Sku TEXT COLLATE BINARY)");

        // Assert
        var columns = db.GetColumns("Products");
        var skuColumn = columns.FirstOrDefault(c => c.Name == "Sku");

        Assert.NotNull(skuColumn);
        // Binary collation is the default, so it should be null in metadata
        Assert.Null(skuColumn.Collation);
    }

    [Fact]
    public void CreateTable_WithCollateRTrim_ShouldParseSuccessfully()
    {
        // Arrange & Act
        db.ExecuteSQL("CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTO, Code TEXT COLLATE RTRIM)");

        // Assert
        var columns = db.GetColumns("Items");
        var codeColumn = columns.FirstOrDefault(c => c.Name == "Code");

        Assert.NotNull(codeColumn);
        Assert.Equal("RTRIM", codeColumn.Collation);
    }

    [Fact]
    public void Select_WithNoCaseCollation_ShouldBeCaseInsensitive()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO, Name TEXT COLLATE NOCASE)");
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('Alice')");
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('Bob')");
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('CHARLIE')");

        // Act - Query with different case
        var results1 = db.ExecuteQuery("SELECT * FROM Users WHERE Name = 'alice'");
        var results2 = db.ExecuteQuery("SELECT * FROM Users WHERE Name = 'ALICE'");
        var results3 = db.ExecuteQuery("SELECT * FROM Users WHERE Name = 'Alice'");

        // Assert - All should return the same row
        Assert.Single(results1);
        Assert.Single(results2);
        Assert.Single(results3);
        Assert.Equal("Alice", results1[0]["Name"]);
    }

    [Fact]
    public void Select_WithBinaryCollation_ShouldBeCaseSensitive()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTO, Sku TEXT COLLATE BINARY)");
        db.ExecuteSQL("INSERT INTO Products (Sku) VALUES ('ABC123')");
        db.ExecuteSQL("INSERT INTO Products (Sku) VALUES ('abc123')");

        // Act
        var results1 = db.ExecuteQuery("SELECT * FROM Products WHERE Sku = 'ABC123'");
        var results2 = db.ExecuteQuery("SELECT * FROM Products WHERE Sku = 'abc123'");

        // Assert - Should return different rows
        Assert.Single(results1);
        Assert.Single(results2);
        Assert.Equal("ABC123", results1[0]["Sku"]);
        Assert.Equal("abc123", results2[0]["Sku"]);
    }

    [Fact]
    public void Select_WithRTrimCollation_ShouldIgnoreTrailingSpaces()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTO, Code TEXT COLLATE RTRIM)");
        db.ExecuteSQL("INSERT INTO Items (Code) VALUES ('CODE1')");

        // Act - Query with trailing spaces
        var results1 = db.ExecuteQuery("SELECT * FROM Items WHERE Code = 'CODE1   '");
        var results2 = db.ExecuteQuery("SELECT * FROM Items WHERE Code = 'CODE1'");

        // Assert - Both should return the row (RTRIM ignores trailing whitespace)
        Assert.Single(results1);
        Assert.Single(results2);
    }

    [Fact]
    public void MetadataPersistence_CollationsShouldSurviveReload()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO, Name TEXT COLLATE NOCASE, Email TEXT COLLATE NOCASE)");
        db.ExecuteSQL("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@example.com')");
        db.ForceSave();

        // Act - Dispose and reload database
        db.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var db2 = new Database(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                .BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSharpCoreDB()),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: DatabaseConfig.Benchmark);

        // Assert - Collations should be preserved
        var columns = db2.GetColumns("Users");
        var nameColumn = columns.FirstOrDefault(c => c.Name == "Name");
        var emailColumn = columns.FirstOrDefault(c => c.Name == "Email");

        Assert.NotNull(nameColumn);
        Assert.NotNull(emailColumn);
        Assert.Equal("NOCASE", nameColumn.Collation);
        Assert.Equal("NOCASE", emailColumn.Collation);

        // Verify case-insensitive query still works after reload
        var results = db2.ExecuteQuery("SELECT * FROM Users WHERE Name = 'ALICE'");
        Assert.Single(results);
        Assert.Equal("Alice", results[0]["Name"]);

        db2.Dispose();
    }

    [Fact]
    public void GetColumnCollation_ShouldReturnCorrectCollationType()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO, Name TEXT COLLATE NOCASE, Age INTEGER)");

        // Act
        var nameCollation = db.GetColumnCollation("Users", "Name");
        var ageCollation = db.GetColumnCollation("Users", "Age");
        var nonExistentCollation = db.GetColumnCollation("Users", "NonExistent");

        // Assert
        Assert.Equal(CollationType.NoCase, nameCollation);
        Assert.Equal(CollationType.Binary, ageCollation); // Default
        Assert.Equal(CollationType.Binary, nonExistentCollation); // Default for missing column
    }

    [Fact]
    public void BatchInsert_WithNoCaseCollation_ShouldWorkCorrectly()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO, Name TEXT COLLATE NOCASE)");

        var statements = new List<string>
        {
            "INSERT INTO Users (Name) VALUES ('Alice')",
            "INSERT INTO Users (Name) VALUES ('Bob')",
            "INSERT INTO Users (Name) VALUES ('CHARLIE')"
        };

        // Act
        db.ExecuteBatchSQL(statements);
        db.Flush();

        var results = db.ExecuteQuery("SELECT * FROM Users WHERE Name = 'charlie'");

        // Assert
        Assert.Single(results);
        Assert.Equal("CHARLIE", results[0]["Name"]);
    }

    [Fact]
    public void AlterTableAddColumn_WithCollate_ShouldParseSuccessfully()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO)");

        // Act
        db.ExecuteSQL("ALTER TABLE Users ADD COLUMN Name TEXT COLLATE NOCASE");

        // Assert
        var columns = db.GetColumns("Users");
        var nameColumn = columns.FirstOrDefault(c => c.Name == "Name");

        Assert.NotNull(nameColumn);
        Assert.Equal("NOCASE", nameColumn.Collation);
    }

    [Fact]
    public void HashIndex_WithNoCaseCollation_ShouldFindCaseInsensitive()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO, Name TEXT COLLATE NOCASE)");
        db.ExecuteSQL("CREATE INDEX idx_users_name ON Users(Name)");

        // Insert data
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('Alice')");
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('Bob')");
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('CHARLIE')");
        db.Flush();

        // Act - Query with different cases (should use index)
        var results1 = db.ExecuteQuery("SELECT * FROM Users WHERE Name = 'alice'");
        var results2 = db.ExecuteQuery("SELECT * FROM Users WHERE Name = 'ALICE'");
        var results3 = db.ExecuteQuery("SELECT * FROM Users WHERE Name = 'Alice'");

        // Assert - All should return the same row via index
        Assert.Single(results1);
        Assert.Single(results2);
        Assert.Single(results3);
        Assert.Equal("Alice", results1[0]["Name"]);
    }

    [Fact]
    public void HashIndex_WithBinaryCollation_ShouldFindCaseSensitive()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTO, Sku TEXT COLLATE BINARY)");
        db.ExecuteSQL("CREATE INDEX idx_products_sku ON Products(Sku)");

        // Insert data
        db.ExecuteSQL("INSERT INTO Products (Sku) VALUES ('ABC123')");
        db.ExecuteSQL("INSERT INTO Products (Sku) VALUES ('abc123')");
        db.Flush();

        // Act - Query with different cases (should use index)
        var results1 = db.ExecuteQuery("SELECT * FROM Products WHERE Sku = 'ABC123'");
        var results2 = db.ExecuteQuery("SELECT * FROM Products WHERE Sku = 'abc123'");

        // Assert - Should return different rows
        Assert.Single(results1);
        Assert.Single(results2);
        Assert.Equal("ABC123", results1[0]["Sku"]);
        Assert.Equal("abc123", results2[0]["Sku"]);
    }

    [Fact]
    public void PrimaryKeyIndex_WithNoCaseCollation_ShouldBeCaseInsensitive()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Username TEXT PRIMARY KEY COLLATE NOCASE, Email TEXT)");

        // Insert data
        db.ExecuteSQL("INSERT INTO Users (Username, Email) VALUES ('alice', 'alice@example.com')");
        db.ExecuteSQL("INSERT INTO Users (Username, Email) VALUES ('Bob', 'bob@example.com')");
        db.Flush();
        db.ForceSave();

        // Act - Query with different cases (should use primary key index)
        var results1 = db.ExecuteQuery("SELECT * FROM Users WHERE Username = 'ALICE'");
        var results2 = db.ExecuteQuery("SELECT * FROM Users WHERE Username = 'alice'");
        var results3 = db.ExecuteQuery("SELECT * FROM Users WHERE Username = 'Alice'");

        // Assert - All should return the same row via PK index
        Assert.Single(results1);
        Assert.Single(results2);
        Assert.Single(results3);
        Assert.Equal("alice", results1[0]["Username"]);
    }

    [Fact]
    public void PrimaryKeyIndex_WithNoCaseCollation_ShouldPreventDuplicates()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Username TEXT PRIMARY KEY COLLATE NOCASE, Email TEXT)");
        db.ExecuteSQL("INSERT INTO Users (Username, Email) VALUES ('alice', 'alice@example.com')");

        // Act & Assert - Try to insert duplicate with different case
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            db.ExecuteSQL("INSERT INTO Users (Username, Email) VALUES ('ALICE', 'alice2@example.com')");
        });

        Assert.Contains("Primary key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexRebuild_WithCollation_ShouldPreserveCollationBehavior()
    {
        // Arrange
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTO, Name TEXT COLLATE NOCASE)");
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('Alice')");
        db.ExecuteSQL("INSERT INTO Users (Name) VALUES ('Bob')");
        db.Flush();
        db.ForceSave();

        // Dispose and reload to trigger index rebuild
        db.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var db2 = new Database(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                .BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSharpCoreDB()),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: DatabaseConfig.Benchmark);

        // Act - Query with different case after rebuild
        var results = db2.ExecuteQuery("SELECT * FROM Users WHERE Name = 'ALICE'");

        // Assert - Should still find the row with case-insensitive match
        Assert.Single(results);
        Assert.Equal("Alice", results[0]["Name"]);

        db2.Dispose();
    }
}
