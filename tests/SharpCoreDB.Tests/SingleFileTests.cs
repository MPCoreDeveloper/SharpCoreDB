// <copyright file="SingleFileTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Xunit;
using SharpCoreDB;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Tests;

/// <summary>
/// Basic tests for single-file (.scdb) database functionality.
/// </summary>
public class SingleFileTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly DatabaseFactory _factory;

    public SingleFileTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void CreateSingleFileDatabase_Success()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();

        // Act
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Assert
        Assert.NotNull(db);
        Assert.Equal(StorageMode.SingleFile, db.StorageMode);
        Assert.True(File.Exists(_testFilePath));

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task SingleFileDatabase_Vacuum_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act
        var result = await db.VacuumAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(VacuumMode.Quick, result.Mode);

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void SingleFileDatabase_GetStorageStatistics_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act
        var stats = db.GetStorageStatistics();

        // Assert
        Assert.True(stats.TotalSize > 0);
        Assert.True(stats.BlockCount >= 0);

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task SingleFileDatabase_ExecuteQuery_StorageStats_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act
        var results = db.ExecuteQuery("SELECT * FROM STORAGE");

        // Assert
        Assert.Single(results);
        Assert.Contains("TotalSize", results[0]);
        Assert.Contains("BlockCount", results[0]);

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void SingleFileDatabase_Flush_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act - should not throw
        db.Flush();

        // Assert - no exception thrown

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void SingleFileDatabase_PrepareSelect_ReturnsCompiledStatement()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE prepared_users (id INTEGER, name TEXT)");

            // Act
            var stmt = db.Prepare("SELECT * FROM prepared_users WHERE id = 1");

            // Assert
            Assert.True(stmt.IsCompiled);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task SingleFileDatabase_ExecutePreparedAsync_InsertWithParameters_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            db.ExecuteSQL("CREATE TABLE prepared_insert (id INTEGER, name TEXT)");
            var stmt = db.Prepare("INSERT INTO prepared_insert VALUES (?, ?)");

            // Act
            await db.ExecutePreparedAsync(stmt, new Dictionary<string, object?>
            {
                ["0"] = 1,
                ["1"] = "Alice"
            }, cancellationToken);

            var results = db.ExecuteQuery("SELECT * FROM prepared_insert WHERE id = 1");

            // Assert
            Assert.Single(results);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFileDatabase_ExecuteCompiledQuery_WithPreparedSelect_ReturnsRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE compiled_users (id INTEGER, name TEXT)");
            db.ExecuteBatchSQL([
                "INSERT INTO compiled_users VALUES (1, 'Alice')",
                "INSERT INTO compiled_users VALUES (2, 'Bob')"
            ]);
            db.Flush();
            db.ForceSave();

            var stmt = db.Prepare("SELECT * FROM compiled_users WHERE id = 2");

            // Act
            var results = db.ExecuteCompiledQuery(stmt);

            // Assert
            Assert.Single(results);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFileDatabase_ExecuteCompiled_WithParameterizedPlan_ReturnsRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE compiled_params (id INTEGER, name TEXT)");
            db.ExecuteBatchSQL([
                "INSERT INTO compiled_params VALUES (1, 'Alice')",
                "INSERT INTO compiled_params VALUES (2, 'Bob')"
            ]);
            db.Flush();
            db.ForceSave();

            var stmt = db.Prepare("SELECT * FROM compiled_params WHERE id = ?");

            // Act
            var results = db.ExecuteCompiled(stmt.CompiledPlan!, new Dictionary<string, object?>
            {
                ["0"] = 1
            });

            // Assert
            Assert.Single(results);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Tests for issue #227: quoted identifiers (e.g. "TableName", "ColumnName") in SingleFileDatabase SQL parser.
    /// FluentMigrator generates SQL with double-quoted identifiers; the parser must handle them correctly.
    /// </summary>
    [Fact]
    public void ExecuteSQL_CreateTable_WithQuotedTableName_CreatesTableSuccessfully()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            // Act — FluentMigrator generates quoted identifiers like this
            db.ExecuteSQL(@"CREATE TABLE IF NOT EXISTS ""__SharpMigrations"" (""Version"" INTEGER NOT NULL, PRIMARY KEY (""Version""))");

            // Assert — table should exist without throwing
            Assert.True(db.TryGetTable("__SharpMigrations", out _));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteSQL_CreateTable_WithQuotedTableName_StripsQuotesFromColumnNames()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            // Act
            db.ExecuteSQL(@"CREATE TABLE IF NOT EXISTS ""migrations"" (""Version"" INTEGER NOT NULL, ""AppliedOn"" TEXT NOT NULL, PRIMARY KEY (""Version""))");
            db.ExecuteSQL(@"INSERT INTO ""migrations"" (""Version"", ""AppliedOn"") VALUES (1, '2026-01-01')");

            // Assert — columns should be stored without quotes so SELECT works
            var rows = db.ExecuteQuery(@"SELECT * FROM ""migrations""");
            Assert.Single(rows);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteSQL_CreateTable_WithTableLevelPrimaryKeyAndQuotedColumn_DetectsPrimaryKey()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            // Act — table-level PRIMARY KEY with quoted column, as generated by FluentMigrator
            db.ExecuteSQL(@"CREATE TABLE ""pk_test"" (""Id"" INTEGER NOT NULL, ""Name"" TEXT, PRIMARY KEY (""Id""))");
            db.ExecuteSQL(@"INSERT INTO ""pk_test"" (""Id"", ""Name"") VALUES (42, 'hello')");

            var rows = db.ExecuteQuery(@"SELECT * FROM ""pk_test""");
            Assert.Single(rows);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteSQL_DropTable_WithQuotedTableName_DropsSuccessfully()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL(@"CREATE TABLE ""drop_me"" (""Id"" INTEGER NOT NULL)");

            // Act
            db.ExecuteSQL(@"DROP TABLE IF EXISTS ""drop_me""");

            // Assert
            Assert.False(db.TryGetTable("drop_me", out _));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
