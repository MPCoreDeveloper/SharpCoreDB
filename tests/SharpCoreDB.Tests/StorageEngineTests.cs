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
using System.Text;
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

    /// <summary>
    /// Computes a deterministic 32-bit FNV-1a hash for table IDs.
    /// This matches the implementation in PageBasedEngine.ComputeStableTableId.
    /// </summary>
    private static uint ComputeStableTableId(string tableName)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;

        var normalized = tableName.ToUpperInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);

        uint hash = fnvOffset;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= fnvPrime;
        }

        return hash == 0 ? 1u : hash;
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

        // ✅ CRITICAL FIX: Use the same table ID calculation as PageBasedEngine
        // PageBasedEngine uses FNV-1a hash on uppercase tableName, NOT string.GetHashCode()
        var testTableName = "test_table";
        var testTableId = CalculateTableId(testTableName); // Use FNV-1a hash
        var expectedFilePath = Path.Combine(testDbPath, $"table_{testTableId}.pages");

        Console.WriteLine($"[TEST] Expected file path: {expectedFilePath}");

        using var engine = new PageBasedEngine(testDbPath);

        Console.WriteLine("[TEST] Beginning transaction");
        engine.BeginTransaction();

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        Console.WriteLine("[TEST] Inserting test data");
        var reference = engine.Insert(testTableName, testData);
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
        var readData = engine.Read(testTableName, reference);
        Console.WriteLine($"[TEST] Read data: {(readData != null ? string.Join(", ", readData) : "NULL")}");

        Assert.NotNull(readData);
        Assert.Equal(testData, readData);

        Console.WriteLine("[TEST] Test completed successfully");
    }

    // Helper method to calculate table ID using FNV-1a hash (same as PageBasedEngine)
    private static uint CalculateTableId(string tableName)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;

        var normalized = tableName.ToUpperInvariant();
        var bytes = System.Text.Encoding.UTF8.GetBytes(normalized);

        uint hash = fnvOffset;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= fnvPrime;
        }

        return hash == 0 ? 1u : hash;
    }

    [Fact]
    public async Task PageBasedEngine_BatchInsert_Commit_VerifyDiskPersistence()
    {
        Console.WriteLine("[TEST] Starting PageBasedEngine_BatchInsert_Commit_VerifyDiskPersistence");

        var testTableId = ComputeStableTableId("test_table");
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
        var storage = new Services.Storage(crypto, key, config, null);

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
        var storage = new Services.Storage(crypto, key, config, null);

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
        var storage = new Services.Storage(crypto, key, config, null);

        using var engine = StorageEngineFactory.CreateEngine(
            StorageEngineType.AppendOnly,
            config: null,
            storage,
            testDbPath);

        Assert.NotNull(engine);
        Assert.Equal(StorageEngineType.AppendOnly, engine.EngineType);
    }

    [Fact]
    public void PageBasedEngine_Update_ShrinkAndSameSize_KeepReference()
    {
        using var engine = new PageBasedEngine(testDbPath);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var reference = engine.Insert("test_table", data);

        // Same-size overwrite stays in place.
        long sameSizeRef = engine.Update("test_table", reference, new byte[] { 9, 9, 9, 9, 9 });
        Assert.Equal(reference, sameSizeRef);

        // Shrink stays in place.
        long shrinkRef = engine.Update("test_table", reference, new byte[] { 7 });
        Assert.Equal(reference, shrinkRef);
        Assert.Equal(new byte[] { 7 }, engine.Read("test_table", reference));
    }

    [Fact]
    public void PageBasedEngine_Update_WithinPageGrowth_KeepsReference()
    {
        using var engine = new PageBasedEngine(testDbPath);

        var small = new byte[100];
        var reference = engine.Insert("test_table", small);

        // Growth that still fits at the end of the page relocates within the page;
        // the slot index (and therefore the storage reference) is unchanged.
        var grown = new byte[3000];
        long newRef = engine.Update("test_table", reference, grown);

        Assert.Equal(reference, newRef);
        Assert.Equal(grown, engine.Read("test_table", reference));
    }

    [Fact]
    public void PageBasedEngine_Update_CrossPageGrowth_Relocates_ReturnsNewReference()
    {
        using var engine = new PageBasedEngine(testDbPath);

        // A page holds at most ~8156 bytes of record data. Fill most of it with one record,
        // then grow a second record beyond the remaining space to force a cross-page
        // relocation (the WP10 fix: Update returns the new storage reference).
        var small = new byte[100];
        var filler = new byte[8000];
        var smallRef = engine.Insert("test_table", small);
        var fillerRef = engine.Insert("test_table", filler);

        var grown = new byte[8000];
        long newRef = engine.Update("test_table", smallRef, grown);

        Assert.NotEqual(smallRef, newRef); // relocated to a new page

        var readGrown = engine.Read("test_table", newRef);
        Assert.NotNull(readGrown);
        Assert.Equal(grown, readGrown);

        // The old slot was marked deleted.
        Assert.Null(engine.Read("test_table", smallRef));

        // The neighbour record is untouched by the relocation.
        Assert.Equal(filler, engine.Read("test_table", fillerRef));
    }

    [Fact]
    public void AppendOnlyEngine_Update_ReturnsNewReference()
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config, null);

        using var engine = new AppendOnlyEngine(storage, testDbPath);

        var original = new byte[] { 1, 2, 3 };
        var reference = engine.Insert("test_table", original);

        // Append-only always writes a new version at a new offset.
        var updated = new byte[] { 4, 5, 6 };
        long newRef = engine.Update("test_table", reference, updated);

        Assert.NotEqual(reference, newRef);
        Assert.Equal(updated, engine.Read("test_table", newRef));
    }

    [Fact]
    public void AppendOnlyEngine_TryUpdateInPlace_SameLength_OverwritesInPlace()
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config, null);

        using var engine = new AppendOnlyEngine(storage, testDbPath);

        var original = new byte[] { 1, 2, 3 };
        var reference = engine.Insert("test_table", original);
        string dataFile = Path.Combine(testDbPath, "test_table.dat");
        long sizeBefore = new FileInfo(dataFile).Length;

        // Same stored length → in-place overwrite succeeds, the reference stays valid
        // (no new version appended) and the file does not grow.
        var updated = new byte[] { 9, 8, 7 };
        bool inPlace = engine.TryUpdateInPlace("test_table", reference, updated);

        Assert.True(inPlace);
        Assert.Equal(updated, engine.Read("test_table", reference));
        Assert.Equal(sizeBefore, new FileInfo(dataFile).Length);

        // A second in-place update over the same reference also succeeds.
        var updatedAgain = new byte[] { 6, 5, 4 };
        Assert.True(engine.TryUpdateInPlace("test_table", reference, updatedAgain));
        Assert.Equal(updatedAgain, engine.Read("test_table", reference));
        Assert.Equal(sizeBefore, new FileInfo(dataFile).Length);
    }

    [Fact]
    public void AppendOnlyEngine_TryUpdateInPlace_DifferentLength_ReturnsFalse()
    {
        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config, null);

        using var engine = new AppendOnlyEngine(storage, testDbPath);

        var original = new byte[] { 1, 2, 3 };
        var reference = engine.Insert("test_table", original);

        // Different length → cannot overwrite in place; the caller must fall back to Update.
        var longer = new byte[] { 1, 2, 3, 4 };
        Assert.False(engine.TryUpdateInPlace("test_table", reference, longer));
        Assert.Equal(original, engine.Read("test_table", reference));

        var shorter = new byte[] { 1 };
        Assert.False(engine.TryUpdateInPlace("test_table", reference, shorter));
        Assert.Equal(original, engine.Read("test_table", reference));

        // The append fallback still works and returns a new reference.
        long newRef = engine.Update("test_table", reference, longer);
        Assert.NotEqual(reference, newRef);
        Assert.Equal(longer, engine.Read("test_table", newRef));
    }
}
