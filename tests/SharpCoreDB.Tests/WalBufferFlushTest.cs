// <copyright file="WalBufferFlushTest.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Test to validate that WAL buffer flush works correctly.
/// Tests the fix for the 141/200 rows bug where rows 101-200 were stuck in buffer.
/// </summary>
public sealed class WalBufferFlushTest : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _testDbPath;

    public WalBufferFlushTest()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wal_flush_test_{Guid.NewGuid():N}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        _factory = serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
                Directory.Delete(_testDbPath, true);
        }
        catch { /* Best effort cleanup */ }
    }

    /// <summary>
    /// THE CRITICAL TEST: Insert 200 rows and verify ALL are visible after validation query.
    /// This tests that FlushBatchWalBuffer() properly drains rows 101-200 from the buffer.
    /// </summary>
    [Fact]
    public void InsertBatch200Rows_AllRowsMustBePersisted()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE test_data (id INTEGER PRIMARY KEY, value TEXT)");

        // Act: Insert 200 rows in a loop (simulates batch buffer scenario)
        for (int i = 1; i <= 200; i++)
        {
            db.ExecuteSQL($"INSERT INTO test_data VALUES ({i}, 'value_{i}')");
        }

        // Critical: This SELECT will trigger Flush() which calls FlushBatchWalBuffer()
        // If the fix works, rows 101-200 should now be visible
        var results = db.ExecuteQuery("SELECT COUNT(*) as cnt FROM test_data");

        // Assert
        Assert.NotEmpty(results);
        var count = Convert.ToInt64(results[0]["cnt"]);
        
        // ✅ THE FIX VALIDATES HERE: Should be 200, not 141
        Assert.Equal(200L, count);
    }

    /// <summary>
    /// Verify that individual rows 101-200 are actually persisted.
    /// </summary>
    [Fact]
    public void InsertBatch200Rows_RowsAfter100MustExist()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE test_data (id INTEGER PRIMARY KEY, value TEXT)");

        // Act: Insert 200 rows
        for (int i = 1; i <= 200; i++)
        {
            db.ExecuteSQL($"INSERT INTO test_data VALUES ({i}, 'value_{i}')");
        }

        // Verify row 101 (first row of second batch)
        var row101 = db.ExecuteQuery("SELECT * FROM test_data WHERE id = 101");
        Assert.Single(row101);
        Assert.Equal("value_101", row101[0]["value"]);

        // Verify row 150 (middle of second batch)
        var row150 = db.ExecuteQuery("SELECT * FROM test_data WHERE id = 150");
        Assert.Single(row150);
        Assert.Equal("value_150", row150[0]["value"]);

        // Verify row 200 (last row of second batch)
        var row200 = db.ExecuteQuery("SELECT * FROM test_data WHERE id = 200");
        Assert.Single(row200);
        Assert.Equal("value_200", row200[0]["value"]);
    }
}
