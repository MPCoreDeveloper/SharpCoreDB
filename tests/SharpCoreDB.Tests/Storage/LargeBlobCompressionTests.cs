// tests/SharpCoreDB.Tests/Storage/LargeBlobCompressionTests.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// REGRESSION TESTS: Large Object (Blob) storage with block-level compression.
/// 
/// CONTEXT:
/// Large payloads are split across multiple 4KB/16KB pages. When compression is enabled,
/// each page is compressed independently. This test suite verifies that the engine
/// correctly chains, compresses, decompresses, and reassembles large payloads without
/// silent data corruption or boundary errors.
/// </summary>
public sealed class LargeBlobCompressionTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly List<string> _filesToCleanup = [];

    public LargeBlobCompressionTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"blob_test_{Guid.NewGuid():N}.scdb");
        _filesToCleanup.Add(_testDbPath);
    }

    [Theory]
    [InlineData(1024)]           // 1 KB  (Fits in single block)
    [InlineData(65536)]          // 64 KB (Spans ~4-16 blocks)
    [InlineData(1048576)]        // 1 MB  (Spans ~64-256 blocks)
    [InlineData(16777216)]       // 16 MB (Spans ~1024-4096 blocks)
    public async Task LargeBlob_Roundtrip_WithBrotliCompression_ShouldMatchHash(int sizeInBytes)
    {
        // Arrange - Generate a highly compressible payload (worst-case for block chaining bugs)
        var originalData = GenerateRepetitivePayload(sizeInBytes);
        var originalHash = SHA256.HashData(originalData);
        var blockName = $"blob_{sizeInBytes}";

        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);

        // Act - Write large blob
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync(blockName, originalData);
            await provider.FlushAsync();
        }

        // Act - Reopen and read large blob
        using var reopened = SingleFileStorageProvider.Open(_testDbPath, options);
        var readData = await reopened.ReadBlockAsync(blockName);

        // Assert
        Assert.NotNull(readData);
        Assert.Equal(sizeInBytes, readData.Length);
        
        var readHash = SHA256.HashData(readData);
        Assert.Equal(originalHash, readHash);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(65536)]
    [InlineData(1048576)]
    public async Task LargeBlob_Roundtrip_WithHighEntropyData_ShouldMatchHash(int sizeInBytes)
    {
        // Arrange - Generate random (incompressible) data. 
        // This tests the "compression makes it larger, so store uncompressed" fallback path.
        var originalData = new byte[sizeInBytes];
        RandomNumberGenerator.Fill(originalData);
        var originalHash = SHA256.HashData(originalData);
        var blockName = $"entropy_blob_{sizeInBytes}";

        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);

        // Act
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync(blockName, originalData);
            await provider.FlushAsync();
        }

        using var reopened = SingleFileStorageProvider.Open(_testDbPath, options);
        var readData = await reopened.ReadBlockAsync(blockName);

        // Assert
        Assert.NotNull(readData);
        var readHash = SHA256.HashData(readData);
        Assert.Equal(originalHash, readHash);
    }

    [Fact]
    public async Task LargeBlob_MultipleBlobsInSameFile_ShouldNotCorruptEachOther()
    {
        // Arrange - Write multiple large blobs of varying sizes to stress the block registry
        var blob1 = GenerateRepetitivePayload(512 * 1024);  // 512 KB
        var blob2 = GenerateRepetitivePayload(2 * 1024 * 1024); // 2 MB
        var blob3 = GenerateRepetitivePayload(128 * 1024);  // 128 KB

        var hash1 = SHA256.HashData(blob1);
        var hash2 = SHA256.HashData(blob2);
        var hash3 = SHA256.HashData(blob3);

        var options = CreateCompressedOptions(BlockCompressionMode.Brotli);

        // Act - Write all three
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync("blob_1", blob1);
            await provider.WriteBlockAsync("blob_2", blob2);
            await provider.WriteBlockAsync("blob_3", blob3);
            await provider.FlushAsync();
        }

        // Act - Read all three back in reverse order
        using var reopened = SingleFileStorageProvider.Open(_testDbPath, options);
        var read3 = await reopened.ReadBlockAsync("blob_3");
        var read2 = await reopened.ReadBlockAsync("blob_2");
        var read1 = await reopened.ReadBlockAsync("blob_1");

        // Assert
        Assert.NotNull(read1); Assert.NotNull(read2); Assert.NotNull(read3);
        Assert.Equal(hash1, SHA256.HashData(read1));
        Assert.Equal(hash2, SHA256.HashData(read2));
        Assert.Equal(hash3, SHA256.HashData(read3));
    }

    // ========================================
    // Helper Methods
    // ========================================

    private static DatabaseOptions CreateCompressedOptions(BlockCompressionMode mode)
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        // Note: If PageSize or EnableMemoryMapping are not valid properties on your version,
        // you can safely remove these two lines and rely on the defaults.
        options.PageSize = 4096; 
        options.EnableMemoryMapping = false;
        options.BlockCompression = mode;
        options.CompressionThreshold = 64; 
        return options;
    }

    /// <summary>
    /// Generates a highly compressible payload that mimics repetitive JSON telemetry.
    /// </summary>
    private static byte[] GenerateRepetitivePayload(int sizeInBytes)
    {
        var pattern = Encoding.UTF8.GetBytes("{\"svc\":\"edge-node\",\"metric\":\"cpu\",\"value\":42.5,\"ts\":\"2026-08-29T10:00:00Z\"}");
        var buffer = new byte[sizeInBytes];
        
        for (int i = 0; i < sizeInBytes; i += pattern.Length)
        {
            var copyLen = Math.Min(pattern.Length, sizeInBytes - i);
            Array.Copy(pattern, 0, buffer, i, copyLen);
        }
        
        return buffer;
    }

    public void Dispose()
    {
        foreach (var file in _filesToCleanup)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                if (File.Exists(file + ".wal")) File.Delete(file + ".wal");
                if (File.Exists(file + ".vacuum.tmp")) File.Delete(file + ".vacuum.tmp");
                if (File.Exists(file + ".vacuum.tmp.scdb")) File.Delete(file + ".vacuum.tmp.scdb");
                if (File.Exists(file + ".backup")) File.Delete(file + ".backup");
            }
            catch { }
        }
    }
}