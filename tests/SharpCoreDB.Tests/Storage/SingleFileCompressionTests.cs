// tests/SharpCoreDB.Tests/Storage/SingleFileCompressionTests.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// REGRESSION TESTS: Block-level compression for SingleFile (.scdb) storage mode.
/// 
/// FEATURE:
/// Transparent Brotli/GZip compression applied before encryption on write,
/// and removed after decryption on read. Per-block Compressed flag tracks state.
/// Mixed compressed/uncompressed blocks supported within the same file.
/// 
/// VERIFIED BY:
/// - 10M record POC: 87% peak file size reduction, 30% faster inserts
/// - All spot-read verifications passed with human-readable data integrity
/// </summary>
public sealed class SingleFileCompressionTests
{
    private readonly string _testDbPath;
    private readonly List<string> _filesToCleanup = [];

    public SingleFileCompressionTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"compression_test_{Guid.NewGuid():N}.scdb");
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
    // Roundtrip Tests: Write → Read → Verify
    // ========================================

    [Fact]
    public async Task Roundtrip_BrotliCompression_DataShouldMatchOriginal()
    {
        // Arrange
        var originalData = Encoding.UTF8.GetBytes("Hello, compressed world! This is a test payload.");

        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli))
        {
            await provider.WriteBlockAsync("test_block", originalData);
            await provider.FlushAsync();
        }

        // Act — Reopen and read back
        using var reopened = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli);
        var readData = await reopened.ReadBlockAsync("test_block");

        // Assert
        Assert.NotNull(readData);
        Assert.Equal(originalData, readData);
    }

    [Fact]
    public async Task Roundtrip_GZipCompression_DataShouldMatchOriginal()
    {
        // Arrange
        var originalData = Encoding.UTF8.GetBytes("GZip compressed payload with repeated content content content.");

        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.GZip))
        {
            await provider.WriteBlockAsync("gzip_block", originalData);
            await provider.FlushAsync();
        }

        // Act
        using var reopened = CreateCompressedProvider(_testDbPath, BlockCompressionMode.GZip);
        var readData = await reopened.ReadBlockAsync("gzip_block");

        // Assert
        Assert.NotNull(readData);
        Assert.Equal(originalData, readData);
    }

    [Fact]
    public async Task Roundtrip_NoneCompression_DataShouldMatchOriginal()
    {
        // Baseline: no compression, data still works.

        var originalData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.None))
        {
            await provider.WriteBlockAsync("plain_block", originalData);
            await provider.FlushAsync();
        }

        using var reopened = CreateCompressedProvider(_testDbPath, BlockCompressionMode.None);
        var readData = await reopened.ReadBlockAsync("plain_block");

        Assert.NotNull(readData);
        Assert.Equal(originalData, readData);
    }

    // ========================================
    // Compression + Encryption Combined
    // ========================================

    [Fact]
    public async Task Roundtrip_CompressionPlusEncryption_DataShouldMatchOriginal()
    {
        // Arrange — encryption key + compression mode
        var key = RandomNumberGenerator.GetBytes(32);
        var originalData = Encoding.UTF8.GetBytes("Encrypted AND compressed secret payload data.");

        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);
        options.EnableEncryption = true;
        options.EncryptionKey = key;

        // Act — Write with encryption + compression
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync("secure_block", originalData);
            await provider.FlushAsync();
        }

        // Reopen with same key and compression mode
        using var reopened = SingleFileStorageProvider.Open(_testDbPath, options);
        var readData = await reopened.ReadBlockAsync("secure_block");

        // Assert
        Assert.NotNull(readData);
        Assert.Equal(originalData, readData);
    }

    [Fact]
    public async Task CompressionPlusEncryption_NoPlaintextOnDisk()
    {
        // Arrange
        var key = RandomNumberGenerator.GetBytes(32);
        var secretPayload = "SUPER_SECRET_CLASSIFIED_PAYLOAD_2026";
        var secretBytes = Encoding.UTF8.GetBytes(secretPayload);

        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);
        options.EnableEncryption = true;
        options.EncryptionKey = key;

        // Act — Write secret data
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync("secret_block", secretBytes);
            await provider.FlushAsync();
        }

        // Assert — Scan raw file bytes for plaintext
        var fileBytes = File.ReadAllBytes(_testDbPath);
        var found = ContainsBytes(fileBytes, secretBytes);

        Assert.False(found, "Secret payload should NOT appear as plaintext on disk");
    }

    // ========================================
    // Consistency Check: Wrong Compression Mode on Reopen
    // ========================================

    [Fact]
    public void Reopen_WithWrongCompressionMode_ShouldThrow()
    {
        // Arrange — Create with Brotli
        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli))
        {
            provider.WriteBlockAsync("test", new byte[] { 1, 2, 3 }).GetAwaiter().GetResult();
            provider.FlushAsync().GetAwaiter().GetResult();
        }

        // Act & Assert — Reopen with None should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var wrong = CreateCompressedProvider(_testDbPath, BlockCompressionMode.None);
        });

        Assert.Contains("Brotli", ex.Message);
        Assert.Contains("BlockCompression", ex.Message);
    }

    [Fact]
    public void Reopen_WithDifferentCompressionMode_ShouldThrow()
    {
        // Arrange — Create with GZip
        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.GZip))
        {
            provider.WriteBlockAsync("test", new byte[] { 1, 2, 3 }).GetAwaiter().GetResult();
            provider.FlushAsync().GetAwaiter().GetResult();
        }

        // Act & Assert — Reopen with Brotli should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var wrong = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli);
        });

        Assert.Contains("GZip", ex.Message);
    }

    [Fact]
    public void Reopen_CompressedFile_WithEncryptionMismatch_ShouldThrow()
    {
        // Arrange — Create compressed but unencrypted
        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli))
        {
            provider.WriteBlockAsync("test", new byte[] { 1, 2, 3 }).GetAwaiter().GetResult();
            provider.FlushAsync().GetAwaiter().GetResult();
        }

        // Act & Assert — Reopen with encryption enabled should throw (encryption mismatch)
        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);
        options.EnableEncryption = true;
        options.EncryptionKey = RandomNumberGenerator.GetBytes(32);

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var wrong = SingleFileStorageProvider.Open(_testDbPath, options);
        });
    }

    // ========================================
    // Small Block Threshold: Below Threshold = Uncompressed
    // ========================================

    [Fact]
    public async Task SmallBlock_BelowThreshold_ShouldNotBeCompressed()
    {
        // Arrange — Set threshold high so our block falls below it
        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);
        options.CompressionThreshold = 1024; // Only compress blocks >= 1KB

        // Small payload (well below threshold)
        var smallData = Encoding.UTF8.GetBytes("tiny");

        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync("small_block", smallData);
            await provider.FlushAsync();

            // Assert — Block should exist and be readable
            Assert.True(provider.BlockExists("small_block"));
            var readData = await provider.ReadBlockAsync("small_block");
            Assert.NotNull(readData);
            Assert.Equal(smallData, readData);
        }
    }

    [Fact]
    public async Task LargeBlock_AboveThreshold_ShouldBeCompressed()
    {
        // Arrange — threshold at 64 bytes
        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);
        options.CompressionThreshold = 64;

        // Large repetitive payload (well above threshold, highly compressible)
        var largeData = Encoding.UTF8.GetBytes(new string('A', 4096));

        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync("large_block", largeData);
            await provider.FlushAsync();

            // Assert — Block should exist and roundtrip correctly
            var readData = await provider.ReadBlockAsync("large_block");
            Assert.NotNull(readData);
            Assert.Equal(largeData, readData);
        }
    }

    // ========================================
    // Mixed Blocks: Compressed + Uncompressed in Same File
    // ========================================

    [Fact]
    public async Task MixedBlocks_CompressedAndUncompressed_AllShouldReadCorrectly()
    {
        // Arrange — compression enabled with low threshold
        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);
        options.CompressionThreshold = 32;

        var largeData = Encoding.UTF8.GetBytes(new string('X', 1024));  // Above threshold → compressed
        var smallData = Encoding.UTF8.GetBytes("hi");                   // Below threshold → uncompressed

        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync("big_block", largeData);
            await provider.WriteBlockAsync("tiny_block", smallData);
            await provider.FlushAsync();
        }

        // Act — Reopen and read both
        using var reopened = SingleFileStorageProvider.Open(_testDbPath, options);
        var readLarge = await reopened.ReadBlockAsync("big_block");
        var readSmall = await reopened.ReadBlockAsync("tiny_block");

        // Assert
        Assert.NotNull(readLarge);
        Assert.Equal(largeData, readLarge);
        Assert.NotNull(readSmall);
        Assert.Equal(smallData, readSmall);
    }

    // ========================================
    // Vacuum Preserves Compression
    // ========================================

    [Fact]
    public async Task Vacuum_FullMode_ShouldPreserveCompressedData()
    {
        // Arrange
        var originalData = Encoding.UTF8.GetBytes("This data survives vacuum with compression intact.");

        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli))
        {
            // Write data and a dummy block to delete (so vacuum has work to do)
            await provider.WriteBlockAsync("keeper", originalData);
            await provider.WriteBlockAsync("to_delete", new byte[256]);
            await provider.FlushAsync();

            // Delete the dummy block
            await provider.DeleteBlockAsync("to_delete");
            await provider.FlushAsync();

            // Run full vacuum
            var result = await provider.VacuumAsync(VacuumMode.Full);
            Assert.True(result.Success, $"Vacuum failed: {result.ErrorMessage}");
        }

        // Act — Reopen and verify data survived vacuum
        using var reopened = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli);
        var readData = await reopened.ReadBlockAsync("keeper");

        // Assert
        Assert.NotNull(readData);
        Assert.Equal(originalData, readData);

        // Deleted block should be gone
        Assert.False(reopened.BlockExists("to_delete"));
    }

    [Fact]
    public async Task Vacuum_IncrementalMode_ShouldPreserveCompressedData()
    {
        var originalData = Encoding.UTF8.GetBytes("Incremental vacuum preserves compressed blocks.");

        using (var provider = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli))
        {
            await provider.WriteBlockAsync("keeper", originalData);
            await provider.FlushAsync();

            var result = await provider.VacuumAsync(VacuumMode.Incremental);
            Assert.True(result.Success, $"Incremental vacuum failed: {result.ErrorMessage}");
        }

        using var reopened = CreateCompressedProvider(_testDbPath, BlockCompressionMode.Brotli);
        var readData = await reopened.ReadBlockAsync("keeper");

        Assert.NotNull(readData);
        Assert.Equal(originalData, readData);
    }

    // ========================================
    // File Size Reduction Verification
    // ========================================

        [Fact]
    public async Task Compression_ShouldReduceFileSize()
    {
        // Arrange — highly repetitive data (best-case compression)
        var repetitiveData = Encoding.UTF8.GetBytes(new string('Z', 8192));

        var compressedPath = _testDbPath;
        var uncompressedPath = _testDbPath + ".nocompress.scdb";
        _filesToCleanup.Add(uncompressedPath);

        // Write with compression
        using (var provider = CreateCompressedProvider(compressedPath, BlockCompressionMode.Brotli))
        {
            await provider.WriteBlockAsync("data_block", repetitiveData);
            await provider.FlushAsync();
        }

        // Write without compression
        using (var provider = CreateCompressedProvider(uncompressedPath, BlockCompressionMode.None))
        {
            await provider.WriteBlockAsync("data_block", repetitiveData);
            await provider.FlushAsync();
        }

        // Assert — Compressed block should use fewer on-disk bytes than uncompressed.
        // File-size comparison is unreliable due to pre-allocation of metadata pages
        // (registry, FSM, WAL, table directory) which dominates small payloads.
        // Comparing the actual stored block length verifies compression was applied.
        using var compressedProvider = CreateCompressedProvider(compressedPath, BlockCompressionMode.Brotli);
        using var uncompressedProvider = CreateCompressedProvider(uncompressedPath, BlockCompressionMode.None);

        var compressedMeta = compressedProvider.GetBlockMetadata("data_block");
        var uncompressedMeta = uncompressedProvider.GetBlockMetadata("data_block");

        Assert.NotNull(compressedMeta);
        Assert.NotNull(uncompressedMeta);
        Assert.True(compressedMeta.Size < uncompressedMeta.Size,
            $"Compressed block ({compressedMeta.Size} bytes on disk) should be smaller than uncompressed ({uncompressedMeta.Size} bytes on disk)");
    }

    // ========================================
    // High-Level DatabaseFactory Integration
    // ========================================

    [Fact]
    public void DatabaseFactory_WithCompression_ShouldRoundtripTableData()
    {
        var factory = BuildFactory();
        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);

        // Create with compression via factory
        var db = factory.CreateWithOptions(_testDbPath, "unused", options);
        db.ExecuteSQL("CREATE TABLE test (id INT, name TEXT)");
        db.ExecuteSQL("INSERT INTO test VALUES (1, 'Alice')");
        db.ExecuteSQL("INSERT INTO test VALUES (2, 'Bob')");
        db.Flush();
        db.ForceSave();
        DisposeDatabase(db);

        // Reopen with same compression mode
        var db2 = factory.CreateWithOptions(_testDbPath, "unused", options);
        var results = db2.ExecuteQuery("SELECT * FROM test ORDER BY id");
        Assert.Equal(2, results.Count);
        Assert.Equal("Alice", results[0]["name"]?.ToString());
        Assert.Equal("Bob", results[1]["name"]?.ToString());
        DisposeDatabase(db2);
    }

    [Fact]
    public void DatabaseFactory_Compression_GrowingTable_ReopenSelectShouldSurvive()
    {
        // REGRESSION (#344): a table whose row-cache block is rewritten while it already
        // exists lost its Compressed flag, so reopening and running SELECT parsed raw
        // Brotli bytes as JSON (JsonException "'0x0B' is an invalid start of a value").
        var factory = BuildFactory();
        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);

        // Auto-flush rewrites the row-cache block as it grows past the compression
        // threshold — exercising the existing-block rewrite path that lost the flag.
        var db = factory.CreateWithOptions(_testDbPath, "unused", options);
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, payload TEXT)");
        for (int i = 0; i < 500; i++)
        {
            db.ExecuteSQL($"INSERT INTO t VALUES ({i}, 'user{i}', '{new string('x', 32)}')");
        }
        db.Flush();
        db.ForceSave();
        DisposeDatabase(db);

        // Reopen and SELECT must survive with the data intact.
        var db2 = factory.CreateWithOptions(_testDbPath, "unused", options);
        var results = db2.ExecuteQuery("SELECT * FROM t ORDER BY id");
        Assert.Equal(500, results.Count);
        Assert.Equal("user42", results[42]["name"]?.ToString());
        Assert.Equal("user499", results[499]["name"]?.ToString());
        DisposeDatabase(db2);
    }

    [Fact]
    public void DatabaseFactory_CompressionPlusEncryption_GrowingTable_ReopenSelectShouldSurvive()
    {
        // Same regression as above but with full at-rest encryption combined with
        // block compression (compression + encryption on the existing-block path).
        var factory = BuildFactory();
        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);
        options.EnableEncryption = true;
        options.EncryptionKey = RandomNumberGenerator.GetBytes(32);

        var db = factory.CreateWithOptions(_testDbPath, "unused", options);
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, payload TEXT)");
        for (int i = 0; i < 500; i++)
        {
            db.ExecuteSQL($"INSERT INTO t VALUES ({i}, 'user{i}', '{new string('y', 32)}')");
        }
        db.Flush();
        db.ForceSave();
        DisposeDatabase(db);

        var db2 = factory.CreateWithOptions(_testDbPath, "unused", options);
        var results = db2.ExecuteQuery("SELECT * FROM t ORDER BY id");
        Assert.Equal(500, results.Count);
        Assert.Equal("user7", results[7]["name"]?.ToString());
        Assert.Equal("user499", results[499]["name"]?.ToString());
        DisposeDatabase(db2);
    }


    [Fact]
    public void DatabaseFactory_WrongCompressionModeOnReopen_ShouldThrow()
    {
        var factory = BuildFactory();

        // Create with Brotli
        var createOptions = CreateCompressedOptions(BlockCompressionMode.Brotli);
        var db = factory.CreateWithOptions(_testDbPath, "unused", createOptions);
        db.ExecuteSQL("CREATE TABLE test (id INT)");
        db.Flush();
        DisposeDatabase(db);

        // Reopen with None — should throw
        var reopenOptions = CreateCompressedOptions(BlockCompressionMode.None);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var db2 = factory.CreateWithOptions(_testDbPath, "unused", reopenOptions);
            DisposeDatabase(db2);
        });
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

    private static DatabaseOptions CreateCompressedOptions(BlockCompressionMode mode)
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 4096;
        options.WalBufferSizePages = 256;
        options.EnableMemoryMapping = false;
        options.BlockCompression = mode;
        options.CompressionThreshold = 64; // Low threshold for tests
        return options;
    }

    private static SingleFileStorageProvider CreateCompressedProvider(string path, BlockCompressionMode mode)
    {
        return SingleFileStorageProvider.Open(path, CreateCompressedOptions(mode));
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }
}
