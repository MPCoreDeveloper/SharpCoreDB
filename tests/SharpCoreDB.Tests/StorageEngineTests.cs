// <copyright file="StorageEngineTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Engines;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Tests for storage engine implementations.
/// </summary>
public class StorageEngineTests : IDisposable
{
    private readonly string testDbPath;

    public StorageEngineTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void PageBasedEngine_Insert_Read_Roundtrip()
    {
        using var engine = new PageBasedEngine(testDbPath);

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var reference = engine.Insert("test_table", testData);

        var readData = engine.Read("test_table", reference);

        Assert.NotNull(readData);
        Assert.Equal(testData, readData);
    }

    [Fact]
    public void PageBasedEngine_Update_InPlace()
    {
        using var engine = new PageBasedEngine(testDbPath);

        var originalData = new byte[] { 1, 2, 3, 4, 5 };
        var reference = engine.Insert("test_table", originalData);

        var newData = new byte[] { 10, 20, 30, 40, 50 };
        engine.Update("test_table", reference, newData);

        var readData = engine.Read("test_table", reference);

        Assert.NotNull(readData);
        Assert.Equal(newData, readData);
    }

    [Fact]
    public void PageBasedEngine_Delete_ReturnsNull()
    {
        using var engine = new PageBasedEngine(testDbPath);

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var reference = engine.Insert("test_table", testData);

        engine.Delete("test_table", reference);

        var readData = engine.Read("test_table", reference);

        Assert.Null(readData);
    }

    [Fact]
    public void PageBasedEngine_BatchInsert()
    {
        using var engine = new PageBasedEngine(testDbPath);

        var dataBlocks = new List<byte[]>
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 }
        };

        var references = engine.InsertBatch("test_table", dataBlocks);

        Assert.Equal(3, references.Length);

        for (int i = 0; i < dataBlocks.Count; i++)
        {
            var readData = engine.Read("test_table", references[i]);
            Assert.NotNull(readData);
            Assert.Equal(dataBlocks[i], readData);
        }
    }

    [Fact]
    public async Task PageBasedEngine_Transaction_Commit()
    {
        using var engine = new PageBasedEngine(testDbPath);

        engine.BeginTransaction();

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var reference = engine.Insert("test_table", testData);

        await engine.CommitAsync();

        var readData = engine.Read("test_table", reference);

        Assert.NotNull(readData);
        Assert.Equal(testData, readData);
    }

    [Fact]
    public async Task PageBasedEngine_Transaction_Commit_VerifyDiskPersistence()
    {
        Console.WriteLine("[TEST] Starting PageBasedEngine_Transaction_Commit_VerifyDiskPersistence");

        var testTableId = (uint)"test_table".GetHashCode();
        var expectedFilePath = Path.Combine(testDbPath, $"table_{testTableId}.pages");

        Console.WriteLine($"[TEST] Expected file path: {expectedFilePath}");

        using var engine = new PageBasedEngine(testDbPath);

        Console.WriteLine("[TEST] Beginning transaction");
        engine.BeginTransaction();

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        Console.WriteLine("[TEST] Inserting test data");
        var reference = engine.Insert("test_table", testData);
        Console.WriteLine($"[TEST] Insert returned storage reference: {reference}");

        Console.WriteLine("[TEST] Committing transaction");
        await engine.CommitAsync();
        Console.WriteLine("[TEST] Transaction committed");

        // CRITICAL: Verify that the .pages file was created on disk
        Console.WriteLine($"[TEST] Checking if file exists: {expectedFilePath}");
        var fileExists = File.Exists(expectedFilePath);
        Console.WriteLine($"[TEST] File exists: {fileExists}");

        if (fileExists)
        {
            var fileInfo = new FileInfo(expectedFilePath);
            Console.WriteLine($"[TEST] File size: {fileInfo.Length} bytes");
        }

        Assert.True(fileExists, $"Data file must exist after commit! Expected: {expectedFilePath}");

        // Also verify we can read the data back
        Console.WriteLine("[TEST] Reading data back");
        var readData = engine.Read("test_table", reference);
        Console.WriteLine($"[TEST] Read data: {(readData != null ? string.Join(", ", readData) : "NULL")}");

        Assert.NotNull(readData);
        Assert.Equal(testData, readData);

        Console.WriteLine("[TEST] Test completed successfully");
    }

    [Fact]
    public async Task PageBasedEngine_BatchInsert_Commit_VerifyDiskPersistence()
    {
        Console.WriteLine("[TEST] Starting PageBasedEngine_BatchInsert_Commit_VerifyDiskPersistence");

        var testTableId = (uint)"test_table".GetHashCode();
        var expectedFilePath = Path.Combine(testDbPath, $"table_{testTableId}.pages");

        Console.WriteLine($"[TEST] Expected file path: {expectedFilePath}");

        using var engine = new PageBasedEngine(testDbPath);

        Console.WriteLine("[TEST] Beginning transaction");
        engine.BeginTransaction();

        var dataBlocks = new List<byte[]>
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 }
        };

        Console.WriteLine($"[TEST] Inserting batch of {dataBlocks.Count} records");
        var references = engine.InsertBatch("test_table", dataBlocks);
        Console.WriteLine($"[TEST] Batch insert returned {references.Length} storage references");

        Console.WriteLine("[TEST] Committing transaction");
        await engine.CommitAsync();
        Console.WriteLine("[TEST] Transaction committed");

        // CRITICAL: Verify that the .pages file was created on disk
        Console.WriteLine($"[TEST] Checking if file exists: {expectedFilePath}");
        var fileExists = File.Exists(expectedFilePath);
        Console.WriteLine($"[TEST] File exists: {fileExists}");

        if (fileExists)
        {
            var fileInfo = new FileInfo(expectedFilePath);
            Console.WriteLine($"[TEST] File size: {fileInfo.Length} bytes");
        }

        Assert.True(fileExists, $"Data file must exist after commit! Expected: {expectedFilePath}");

        // Verify we can read all data back
        for (int i = 0; i < dataBlocks.Count; i++)
        {
            Console.WriteLine($"[TEST] Reading record {i + 1}");
            var readData = engine.Read("test_table", references[i]);
            Console.WriteLine($"[TEST] Read data: {(readData != null ? string.Join(", ", readData) : "NULL")}");

            Assert.NotNull(readData);
            Assert.Equal(dataBlocks[i], readData);
        }

        Console.WriteLine("[TEST] Test completed successfully");
    }

    [Fact]
    public void AppendOnlyEngine_Insert_Read_Roundtrip()
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Storage(crypto, key, config);

        using var engine = new AppendOnlyEngine(storage, testDbPath);

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var reference = engine.Insert("test_table", testData);

        var readData = engine.Read("test_table", reference);

        Assert.NotNull(readData);
        Assert.Equal(testData, readData);
    }

    [Fact]
    public void AppendOnlyEngine_BatchInsert()
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Storage(crypto, key, config);

        using var engine = new AppendOnlyEngine(storage, testDbPath);

        var dataBlocks = new List<byte[]>
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 }
        };

        var references = engine.InsertBatch("test_table", dataBlocks);

        Assert.Equal(3, references.Length);

        for (int i = 0; i < dataBlocks.Count; i++)
        {
            var readData = engine.Read("test_table", references[i]);
            Assert.NotNull(readData);
            Assert.Equal(dataBlocks[i], readData);
        }
    }

    [Fact]
    public void PageBasedEngine_Metrics_Tracking()
    {
        using var engine = new PageBasedEngine(testDbPath);

        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // Perform operations
        var ref1 = engine.Insert("test_table", testData);
        var ref2 = engine.Insert("test_table", testData);
        engine.Update("test_table", ref1, testData);
        engine.Delete("test_table", ref2);
        _ = engine.Read("test_table", ref1);

        var metrics = engine.GetMetrics();

        Assert.Equal(2, metrics.TotalInserts);
        Assert.Equal(1, metrics.TotalUpdates);
        Assert.Equal(1, metrics.TotalDeletes);
        Assert.Equal(1, metrics.TotalReads);
        Assert.True(metrics.AvgInsertTimeMicros >= 0);
    }

    [Fact]
    public void StorageEngineFactory_CreatePageBased()
    {
        using var engine = StorageEngineFactory.CreateEngine(
            StorageEngineType.PageBased,
            config: null,
            storage: null,
            testDbPath);

        Assert.NotNull(engine);
        Assert.Equal(StorageEngineType.PageBased, engine.EngineType);
    }

    [Fact]
    public void StorageEngineFactory_CreateAppendOnly()
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Storage(crypto, key, config);

        using var engine = StorageEngineFactory.CreateEngine(
            StorageEngineType.AppendOnly,
            config: null,
            storage,
            testDbPath);

        Assert.NotNull(engine);
        Assert.Equal(StorageEngineType.AppendOnly, engine.EngineType);
    }
}
