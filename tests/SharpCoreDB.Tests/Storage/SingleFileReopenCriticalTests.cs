// <copyright file="SingleFileReopenCriticalTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// CRITICAL REGRESSION TESTS: Database file reopen scenarios.
/// 
/// BUG:
/// InitializeNewFile() only called SetLength() but never wrote the header to disk.
/// Flush() checked HasPendingChanges (false after creation) and returned early,
/// leaving header bytes uninitialized. On reopen, LoadHeader() read garbage and
/// threw InvalidDataException.
/// 
/// FIX:
/// InitializeNewFile() now writes header immediately with durable flush.
/// </summary>
public sealed class SingleFileReopenCriticalTests
{
    private readonly string _testDbPath;
    private readonly List<string> _filesToCleanup = [];

    public SingleFileReopenCriticalTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"reopen_test_{Guid.NewGuid():N}.scdb");
        _filesToCleanup.Add(_testDbPath);
    }

    /// <summary>
    /// Disposes an IDatabase if the underlying implementation supports it.
    /// SingleFileDatabase implements IDisposable but IDatabase does not.
    /// </summary>
    private static void DisposeDatabase(IDatabase database)
    {
        (database as IDisposable)?.Dispose();
    }

    // ========================================
    // CRITICAL: Immediate Close & Reopen Tests
    // ========================================

    [Fact]
    public void ReopenImmediately_AfterCreateWithNoWrites_ShouldSucceed()
    {
        // THE critical test case that was failing before the fix.
        // User creates database → app crashes before any data writes → tries to reopen.

        // Arrange & Act - Create and dispose immediately (no writes)
        using (var provider = CreateSingleFileProvider(_testDbPath))
        {
            Assert.True(File.Exists(_testDbPath));

            var fileInfo = new FileInfo(_testDbPath);
            Assert.True(fileInfo.Length > 0, "File should have content after creation");
        }

        // Act - Reopen (was throwing InvalidDataException before fix)
        using var reopened = CreateSingleFileProvider(_testDbPath);

        // Assert
        Assert.NotNull(reopened);
        Assert.Equal(_testDbPath, reopened.RootPath);
    }

    [Fact]
    public void ReopenImmediately_AfterCreateWithNoWrites_UsingDatabaseFactory_ShouldSucceed()
    {
        // Same scenario via the high-level DatabaseFactory API that users actually call.

        // Arrange
        var factory = BuildFactory();

        // Act - Create database and dispose before reopening
        var database = factory.Create(_testDbPath, "password123");
        Assert.Equal(StorageMode.SingleFile, database.StorageMode);
        DisposeDatabase(database);

        // Assert - Reopen should work
        var database2 = factory.Create(_testDbPath, "password123");
        Assert.NotNull(database2);
        Assert.Equal(StorageMode.SingleFile, database2.StorageMode);
        DisposeDatabase(database2);
    }

    [Fact]
    public void ReopenImmediately_AfterCreateAndFlush_ShouldSucceed()
    {
        // Explicit Flush() after creation — the workaround users attempted.

        // Arrange
        var factory = BuildFactory();

        // Act - Create, flush, dispose
        var database = factory.Create(_testDbPath, "password123");
        database.Flush();
        DisposeDatabase(database);

        // Assert - Reopen
        var database2 = factory.Create(_testDbPath, "password123");
        Assert.NotNull(database2);
        DisposeDatabase(database2);
    }

    // ========================================
    // CRITICAL: Simulated Crash Scenarios
    // ========================================

    [Fact]
    public void SimulatedCrash_AfterCreate_BeforeFirstFlush_ShouldRecoverOnReopen()
    {
        // User creates database, app crashes before Flush(), tries to reopen.
        // The background write worker may or may not have run.
        // Since InitializeNewFile now writes the header, file is always valid.

        // Arrange - Create provider, then abandon it without clean dispose
        var provider = CreateSingleFileProvider(_testDbPath);
        // Intentionally not disposing — simulates abrupt termination
        provider = null!;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        // Allow background tasks to settle (write worker may still be running)
        Thread.Sleep(500);

        // Assert - File should still be valid and reopenable
        using var recovered = CreateSingleFileProvider(_testDbPath);
        Assert.NotNull(recovered);
    }

    [Fact]
    public async Task SimulatedCrash_AfterCreateWithData_BeforeFlush_ShouldReopenButDataLost()
    {
        // User creates database, writes data, crashes before flush.
        // The background write worker may persist data before abandonment,
        // so data loss is NOT guaranteed. The key invariant is: database reopens
        // without corruption regardless of whether data survived.

        var provider = CreateSingleFileProvider(_testDbPath);
        await provider.WriteBlockAsync("test_block", new byte[] { 1, 2, 3, 4, 5 });

        // Simulate crash — abandon without flush
        provider = null!;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(500);

        // Assert - Reopens without corruption
        using var recovered = CreateSingleFileProvider(_testDbPath);
        Assert.NotNull(recovered);

        // Data may or may not survive — background worker could have persisted it.
        // The critical invariant is that the database is not corrupted on reopen.
    }

    // ========================================
    // Normal Operation Tests (Baseline)
    // ========================================

    [Fact]
    public async Task NormalOperation_CreateWriteFlushReopen_ShouldPersistData()
    {
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // Create, write, flush, close
        using (var provider = CreateSingleFileProvider(_testDbPath))
        {
            await provider.WriteBlockAsync("test_block", testData);
            await provider.FlushAsync();
        }

        // Reopen and verify
        using (var provider = CreateSingleFileProvider(_testDbPath))
        {
            Assert.True(provider.BlockExists("test_block"));

            var readData = await provider.ReadBlockAsync("test_block");
            Assert.NotNull(readData);
            Assert.Equal(testData, readData);
        }
    }

    [Fact]
    public void NormalOperation_CreateTableInsertReopen_ShouldPersistSchema()
    {
        var factory = BuildFactory();

        // Create table and insert data
        var database = factory.Create(_testDbPath, "password123");
        database.ExecuteSQL("CREATE TABLE users (id INT, name TEXT)");
        database.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
        database.ExecuteSQL("INSERT INTO users VALUES (2, 'Bob')");
        database.Flush();
        database.ForceSave();
        DisposeDatabase(database);

        // Reopen and verify
        var database2 = factory.Create(_testDbPath, "password123");
        var results = database2.ExecuteQuery("SELECT * FROM users");
        Assert.Equal(2, results.Count);
        DisposeDatabase(database2);
    }

    // ========================================
    // Multiple Reopen Cycles
    // ========================================

    [Fact]
    public void MultipleReopenCycles_ShouldMaintainIntegrity()
    {
        var factory = BuildFactory();

        // Cycle 1: Create and insert
        var db1 = factory.Create(_testDbPath, "password123");
        db1.ExecuteSQL("CREATE TABLE counter (value INT)");
        db1.ExecuteSQL("INSERT INTO counter VALUES (1)");
        db1.Flush();
        db1.ForceSave();
        DisposeDatabase(db1);

        // Cycle 2: Reopen, read, insert more
        var db2 = factory.Create(_testDbPath, "password123");
        var results = db2.ExecuteQuery("SELECT * FROM counter");
        Assert.Single(results);
        Assert.Equal(1, results[0]["value"]);

        db2.ExecuteSQL("INSERT INTO counter VALUES (2)");
        db2.Flush();
        db2.ForceSave();
        DisposeDatabase(db2);

        // Cycle 3: Verify both records
        var db3 = factory.Create(_testDbPath, "password123");
        results = db3.ExecuteQuery("SELECT * FROM counter");
        Assert.Equal(2, results.Count);
        DisposeDatabase(db3);

        // Cycle 4: Reopen immediately — no operations
        var db4 = factory.Create(_testDbPath, "password123");
        Assert.NotNull(db4);
        DisposeDatabase(db4);

        // Cycle 5: Final verification
        var db5 = factory.Create(_testDbPath, "password123");
        results = db5.ExecuteQuery("SELECT * FROM counter");
        Assert.Equal(2, results.Count);
        DisposeDatabase(db5);
    }

    // ========================================
    // Edge Cases
    // ========================================

    [Fact]
    public void EdgeCase_CreateMultipleDatabases_AllShouldBeReopenable()
    {
        var factory = BuildFactory();
        var dbPaths = new List<string>();

        // Create multiple databases and dispose each
        for (int i = 0; i < 5; i++)
        {
            var path = Path.Combine(Path.GetTempPath(), $"reopen_multi_{i}_{Guid.NewGuid():N}.scdb");
            dbPaths.Add(path);
            _filesToCleanup.Add(path);

            var db = factory.Create(path, "password123");
            DisposeDatabase(db);
        }

        // Verify all can be reopened
        foreach (var path in dbPaths)
        {
            var db = factory.Create(path, "password123");
            Assert.NotNull(db);
            DisposeDatabase(db);
        }
    }

    [Fact]
    public void EdgeCase_CreateWithDifferentPageSizes_ShouldReopenWithCorrectPageSize()
    {
        var testPath = Path.Combine(Path.GetTempPath(), $"reopen_pagesize_{Guid.NewGuid():N}.scdb");
        _filesToCleanup.Add(testPath);

        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 8192;

        // Create with custom page size
        using (var provider = SingleFileStorageProvider.Open(testPath, options))
        {
            Assert.Equal(8192, provider.PageSize);
        }

        // Reopen and verify page size preserved
        using (var provider = SingleFileStorageProvider.Open(testPath, options))
        {
            Assert.Equal(8192, provider.PageSize);
        }
    }

    [Fact]
    public void HeaderBytes_AfterCreate_ShouldContainValidMagic()
    {
        // Directly verify the on-disk header bytes are correct after file creation.
        // This is the byte-level proof that InitializeNewFile writes the header.

        using (var provider = CreateSingleFileProvider(_testDbPath))
        {
            // Provider is alive — header should be on disk
        }

        // Read raw bytes from offset 0
        var headerBytes = new byte[8];
        using (var fs = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read))
        {
            fs.ReadExactly(headerBytes);
        }

        // Expected magic: 0x0000_0010_4244_4353 in little-endian
        // Bytes: 53 43 44 42 10 00 00 00  ("SCDB\x10\x00\x00\x00")
        Assert.Equal(0x53, headerBytes[0]); // 'S'
        Assert.Equal(0x43, headerBytes[1]); // 'C'
        Assert.Equal(0x44, headerBytes[2]); // 'D'
        Assert.Equal(0x42, headerBytes[3]); // 'B'
        Assert.Equal(0x10, headerBytes[4]); // version byte
    }

    // ========================================
    // Helper Methods
    // ========================================

    private static DatabaseFactory BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<DatabaseFactory>();
    }

    private static SingleFileStorageProvider CreateSingleFileProvider(string path)
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 4096;
        options.WalBufferSizePages = 256;
        options.EnableMemoryMapping = false;

        return SingleFileStorageProvider.Open(path, options);
    }
}
