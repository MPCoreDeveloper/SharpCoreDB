// <copyright file="SingleFileTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Xunit;
using SharpCoreDB;
using System.IO;

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
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        _factory = new DatabaseFactory(null);
#pragma warning restore CS8625
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
}
