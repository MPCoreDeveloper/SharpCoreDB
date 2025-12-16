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
    public void PageBasedEngine_Transaction_Commit()
    {
        using var engine = new PageBasedEngine(testDbPath);

        engine.BeginTransaction();

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var reference = engine.Insert("test_table", testData);

        engine.CommitAsync().GetAwaiter().GetResult();

        var readData = engine.Read("test_table", reference);

        Assert.NotNull(readData);
        Assert.Equal(testData, readData);
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
            null,
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
            storage,
            testDbPath);

        Assert.NotNull(engine);
        Assert.Equal(StorageEngineType.AppendOnly, engine.EngineType);
    }
}
