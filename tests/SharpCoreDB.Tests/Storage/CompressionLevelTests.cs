// tests\SharpCoreDB.Tests\Storage\CompressionLevelTests.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Text;
using SharpCoreDB;
using SharpCoreDB.Compression;
using SharpCoreDB.Services;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// Fast, isolated unit tests for compression presets.
///
/// These tests intentionally avoid creating a full database because the database
/// create/save/dispose lifecycle is heavy and has a known hang risk in test contexts.
/// Full end-to-end combination validation belongs in the separate POC harness.
/// </summary>
public class CompressionLevelTests
{
    // ==========================================================
    // DatabaseOptions defaults
    // ==========================================================

    [Fact]
    public void MetadataCompressionLevel_DefaultsToFastest()
    {
        var options = new DatabaseOptions();

        Assert.Equal(OptionalCompressionLevel.Fastest, options.MetadataCompressionLevel);
    }

    [Fact]
    public void BlockCompressionLevel_DefaultsToOptimal()
    {
        var options = new DatabaseOptions();

        Assert.Equal(OptionalCompressionLevel.Optimal, options.BlockCompressionLevel);
    }

    [Fact]
    public void CompressionLevels_AreSettableAndReadable()
    {
        var options = new DatabaseOptions
        {
            MetadataCompressionLevel = OptionalCompressionLevel.SmallestSize,
            BlockCompressionLevel = OptionalCompressionLevel.Fastest
        };

        Assert.Equal(OptionalCompressionLevel.SmallestSize, options.MetadataCompressionLevel);
        Assert.Equal(OptionalCompressionLevel.Fastest, options.BlockCompressionLevel);
    }

    // ==========================================================
    // BlockCompressor behavior
    // ==========================================================

    [Fact]
    public void BlockCompressor_Brotli_HigherEffortDoesNotIncreaseSize()
    {
        var data = RealisticCompressiblePayload();

        var fastest = BlockCompressor.Compress(data, BlockCompressionMode.Brotli, OptionalCompressionLevel.Fastest);
        var optimal = BlockCompressor.Compress(data, BlockCompressionMode.Brotli, OptionalCompressionLevel.Optimal);
        var smallest = BlockCompressor.Compress(data, BlockCompressionMode.Brotli, OptionalCompressionLevel.SmallestSize);

        Assert.True(fastest.Length >= optimal.Length,
            $"Fastest ({fastest.Length}) should be >= Optimal ({optimal.Length})");

        Assert.True(optimal.Length >= smallest.Length,
            $"Optimal ({optimal.Length}) should be >= SmallestSize ({smallest.Length})");

        Assert.True(smallest.Length < data.Length,
            $"SmallestSize ({smallest.Length}) should be smaller than raw ({data.Length})");
    }

    [Fact]
    public void BlockCompressor_GZip_HigherEffortDoesNotIncreaseSize()
    {
        var data = RealisticCompressiblePayload();

        var fastest = BlockCompressor.Compress(data, BlockCompressionMode.GZip, OptionalCompressionLevel.Fastest);
        var smallest = BlockCompressor.Compress(data, BlockCompressionMode.GZip, OptionalCompressionLevel.SmallestSize);

        Assert.True(fastest.Length >= smallest.Length,
            $"Fastest ({fastest.Length}) should be >= SmallestSize ({smallest.Length})");

        Assert.True(smallest.Length < data.Length,
            $"SmallestSize ({smallest.Length}) should be smaller than raw ({data.Length})");
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void BlockCompressor_Zstd_HigherEffortDoesNotIncreaseSize()
    {
        var data = RealisticCompressiblePayload();

        var fastest = BlockCompressor.Compress(data, BlockCompressionMode.Zstd, OptionalCompressionLevel.Fastest);
        var optimal = BlockCompressor.Compress(data, BlockCompressionMode.Zstd, OptionalCompressionLevel.Optimal);
        var smallest = BlockCompressor.Compress(data, BlockCompressionMode.Zstd, OptionalCompressionLevel.SmallestSize);

        Assert.True(fastest.Length >= optimal.Length,
            $"Fastest ({fastest.Length}) should be >= Optimal ({optimal.Length})");
        Assert.True(optimal.Length >= smallest.Length,
            $"Optimal ({optimal.Length}) should be >= SmallestSize ({smallest.Length})");
        Assert.True(smallest.Length < data.Length,
            $"SmallestSize ({smallest.Length}) should be smaller than raw ({data.Length})");
    }

    [Theory]
    [InlineData(OptionalCompressionLevel.Fastest)]
    [InlineData(OptionalCompressionLevel.Optimal)]
    [InlineData(OptionalCompressionLevel.SmallestSize)]
    public void BlockCompressor_Zstd_Roundtrip_PreservesData(OptionalCompressionLevel level)
    {
        var data = RealisticCompressiblePayload();

        var compressed = BlockCompressor.Compress(data, BlockCompressionMode.Zstd, level);
        var restored = BlockCompressor.Decompress(compressed, BlockCompressionMode.Zstd);

        Assert.Equal(data, restored);
    }
#endif

    [Theory]
    [InlineData(OptionalCompressionLevel.Fastest)]
    [InlineData(OptionalCompressionLevel.Optimal)]
    [InlineData(OptionalCompressionLevel.SmallestSize)]
    public void BlockCompressor_Brotli_Roundtrip_PreservesData(OptionalCompressionLevel level)
    {
        var data = RealisticCompressiblePayload();

        var compressed = BlockCompressor.Compress(data, BlockCompressionMode.Brotli, level);
        var restored = BlockCompressor.Decompress(compressed, BlockCompressionMode.Brotli);

        Assert.Equal(data, restored);
    }

    [Theory]
    [InlineData(OptionalCompressionLevel.Fastest)]
    [InlineData(OptionalCompressionLevel.Optimal)]
    [InlineData(OptionalCompressionLevel.SmallestSize)]
    public void BlockCompressor_GZip_Roundtrip_PreservesData(OptionalCompressionLevel level)
    {
        var data = RealisticCompressiblePayload();

        var compressed = BlockCompressor.Compress(data, BlockCompressionMode.GZip, level);
        var restored = BlockCompressor.Decompress(compressed, BlockCompressionMode.GZip);

        Assert.Equal(data, restored);
    }

    [Fact]
    public void BlockCompressor_NoneMode_ReturnsDataUnchanged()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var result = BlockCompressor.Compress(data, BlockCompressionMode.None, OptionalCompressionLevel.Optimal);

        Assert.Equal(data, result);
    }

    [Fact]
    public void BlockCompressor_Compress_DefaultParameterIsOptimal()
    {
        var data = RealisticCompressiblePayload();

        var withDefault = BlockCompressor.Compress(data, BlockCompressionMode.Brotli);
        var withOptimal = BlockCompressor.Compress(data, BlockCompressionMode.Brotli, OptionalCompressionLevel.Optimal);

        Assert.Equal(withOptimal.Length, withDefault.Length);
    }

    // ==========================================================
    // Metadata compression helpers
    // ==========================================================

    [Theory]
    [InlineData(OptionalCompressionLevel.Fastest)]
    [InlineData(OptionalCompressionLevel.Optimal)]
    [InlineData(OptionalCompressionLevel.SmallestSize)]
    public void MetadataCompression_Roundtrips(OptionalCompressionLevel level)
    {
        var raw = RealisticMetadataPayload();

        var compressed = Database.CompressMetadata(raw, level);

        Assert.NotNull(compressed);
        Assert.True(compressed.Length > 4, "Compressed metadata should contain magic header plus payload.");

        Assert.Equal((byte)'B', compressed[0]);
        Assert.Equal((byte)'R', compressed[1]);
        Assert.Equal((byte)'O', compressed[2]);
        Assert.Equal((byte)'T', compressed[3]);

        var restored = Database.DecompressMetadataIfNeeded(compressed);

        Assert.Equal(raw, restored);
    }

    [Fact]
    public void MetadataCompression_ProducesSmallerOutput_ForLargeRepetitiveMetadata()
    {
        var raw = RealisticMetadataPayload();

        var fastest = Database.CompressMetadata(raw, OptionalCompressionLevel.Fastest);
        var optimal = Database.CompressMetadata(raw, OptionalCompressionLevel.Optimal);
        var smallest = Database.CompressMetadata(raw, OptionalCompressionLevel.SmallestSize);

        Assert.True(fastest.Length < raw.Length,
            $"Fastest metadata ({fastest.Length}) should be smaller than raw ({raw.Length})");

        Assert.True(optimal.Length < raw.Length,
            $"Optimal metadata ({optimal.Length}) should be smaller than raw ({raw.Length})");

        Assert.True(smallest.Length < raw.Length,
            $"SmallestSize metadata ({smallest.Length}) should be smaller than raw ({raw.Length})");
    }

    [Fact]
    public void MetadataDecompression_RawJson_PassesThrough()
    {
        var raw = Encoding.UTF8.GetBytes("{\"Tables\":[]}");

        var result = Database.DecompressMetadataIfNeeded(raw);

        Assert.Equal(raw, result);
    }

    [Fact]
    public void MetadataCompressionLevels_ProduceDifferentSizes()
    {
        // Generate a realistic metadata payload (100 tables with columns, indexes, etc.)
        var metadataJson = GenerateRealisticMetadata(100);
        var rawBytes = Encoding.UTF8.GetBytes(metadataJson);

        var fastest = Database.CompressMetadata(rawBytes, OptionalCompressionLevel.Fastest);
        var optimal = Database.CompressMetadata(rawBytes, OptionalCompressionLevel.Optimal);
        var smallest = Database.CompressMetadata(rawBytes, OptionalCompressionLevel.SmallestSize);

        // All three should be smaller than raw
        Assert.True(fastest.Length < rawBytes.Length,
            $"Fastest ({fastest.Length}) should be smaller than raw ({rawBytes.Length})");
        Assert.True(optimal.Length < rawBytes.Length,
            $"Optimal ({optimal.Length}) should be smaller than raw ({rawBytes.Length})");
        Assert.True(smallest.Length < rawBytes.Length,
            $"SmallestSize ({smallest.Length}) should be smaller than raw ({rawBytes.Length})");

        // SmallestSize should be <= Optimal <= Fastest (with tolerance for Brotli variance)
        Assert.True(fastest.Length >= optimal.Length,
            $"Fastest ({fastest.Length}) should be >= Optimal ({optimal.Length})");
        Assert.True(optimal.Length >= smallest.Length,
            $"Optimal ({optimal.Length}) should be >= SmallestSize ({smallest.Length})");
    }

    private static string GenerateRealisticMetadata(int tableCount)
    {
        var sb = new StringBuilder();
        sb.Append("{\"Tables\":[");

        for (int i = 0; i < tableCount; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append($"{{\"Name\":\"table_{i}\",\"Columns\":[\"id\",\"name\",\"email\",\"created_at\"],\"ColumnTypes\":[\"INTEGER\",\"TEXT\",\"TEXT\",\"TEXT\"],\"PrimaryKeyIndex\":0,\"DataFile\":\"\",\"StorageMode\":0,\"IsAuto\":[true,false,false,false],\"IsNotNull\":[true,false,false,false],\"DefaultValues\":[null,null,null,null],\"UniqueConstraints\":[],\"ForeignKeys\":[],\"ColumnCollations\":[0,0,0,0],\"AutoIncrementCounters\":[0]}}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    // ==========================================================
    // Storage provider read-path decompression (regression test)
    // ==========================================================
    // Bug: GetReadStream and GetReadSpan returned raw compressed bytes when
    // encryption was disabled but compression was enabled. This caused
    // SingleFileTable.EnsureCacheLoaded to parse Brotli bytes as JSON after
    // a database reopen (cold cache). The zero-copy guard only checked for
    // encryption, not the Compressed flag.

    [Theory]
    [InlineData(BlockCompressionMode.Brotli)]
    [InlineData(BlockCompressionMode.GZip)]
#if NET11_0_OR_GREATER
    [InlineData(BlockCompressionMode.Zstd)]
#endif
    public void GetReadStream_CompressedBlock_NoEncryption_ReturnsDecompressedData(BlockCompressionMode mode)
    {
        var path = Path.Combine(Path.GetTempPath(), $"readstream_{mode}_{Guid.NewGuid():N}.scdb");
        var originalData = RealisticCompressiblePayload();

        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            BlockCompression = mode,
            BlockCompressionLevel = OptionalCompressionLevel.Optimal,
            CompressionThreshold = 256,
            EnableEncryption = false,  // Key: no encryption, only compression
            EnableMemoryMapping = false,
            CreateImmediately = true
        };

        // Write block
        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            provider.WriteBlockAsync("test:block", originalData).GetAwaiter().GetResult();
            provider.FlushAsync().GetAwaiter().GetResult();
        }

        // Reopen (simulates cold cache after restart)
        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            // ReadBlockAsync — control path (known to work)
            var viaReadBlock = provider.ReadBlockAsync("test:block").GetAwaiter().GetResult();
            Assert.NotNull(viaReadBlock);
            Assert.Equal(originalData, viaReadBlock);

            // GetReadStream — the bug path
            using var stream = provider.GetReadStream("test:block");
            Assert.NotNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var viaStream = ms.ToArray();
            Assert.Equal(originalData, viaStream);
        }

        Cleanup(path);
    }

    [Theory]
    [InlineData(BlockCompressionMode.Brotli)]
    [InlineData(BlockCompressionMode.GZip)]
#if NET11_0_OR_GREATER
    [InlineData(BlockCompressionMode.Zstd)]
#endif
    public void GetReadSpan_CompressedBlock_NoEncryption_ReturnsDecompressedData(BlockCompressionMode mode)
    {
        var path = Path.Combine(Path.GetTempPath(), $"readspan_{mode}_{Guid.NewGuid():N}.scdb");
        var originalData = RealisticCompressiblePayload();

        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            BlockCompression = mode,
            BlockCompressionLevel = OptionalCompressionLevel.Optimal,
            CompressionThreshold = 256,
            EnableEncryption = false,  // Key: no encryption, only compression
            EnableMemoryMapping = false,
            CreateImmediately = true
        };

        // Write block
        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            provider.WriteBlockAsync("test:block", originalData).GetAwaiter().GetResult();
            provider.FlushAsync().GetAwaiter().GetResult();
        }

        // Reopen (simulates cold cache after restart)
        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            // GetReadSpan — the other bug path
            var span = provider.GetReadSpan("test:block");
            Assert.False(span.IsEmpty, "GetReadSpan returned empty for compressed block");
            Assert.Equal(originalData, span.ToArray());
        }

        Cleanup(path);
    }

    [Fact]
    public void GetReadStream_MultipleWrites_FlagUpdatesCorrectly()
    {
        // Regression test: First write below threshold (no compression),
        // second write above threshold (compressed). The Compressed flag
        // must be updated on the second write.
        var path = Path.Combine(Path.GetTempPath(), $"multiwrite_{Guid.NewGuid():N}.scdb");

        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            BlockCompression = BlockCompressionMode.Brotli,
            BlockCompressionLevel = OptionalCompressionLevel.Optimal,
            CompressionThreshold = 256,
            EnableEncryption = false,
            EnableMemoryMapping = false,
            CreateImmediately = true
        };

        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            // First write: small data, below threshold, NOT compressed
            var smallData = new byte[] { 1, 2, 3, 4, 5 };
            provider.WriteBlockAsync("test:block", smallData).GetAwaiter().GetResult();

            // Second write: large data, above threshold, IS compressed
            var largeData = RealisticCompressiblePayload();
            provider.WriteBlockAsync("test:block", largeData).GetAwaiter().GetResult();

            provider.FlushAsync().GetAwaiter().GetResult();
        }

        // Reopen and verify the block is correctly decompressed
        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            var largeData = RealisticCompressiblePayload();

            // ReadBlockAsync — control path
            var viaReadBlock = provider.ReadBlockAsync("test:block").GetAwaiter().GetResult();
            Assert.NotNull(viaReadBlock);
            Assert.Equal(largeData, viaReadBlock);

            // GetReadStream — the bug path
            using var stream = provider.GetReadStream("test:block");
            Assert.NotNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Assert.Equal(largeData, ms.ToArray());
        }

        Cleanup(path);
    }

    [Fact]
    public void GetReadStream_UncompressedBlock_NoEncryption_StillWorks()
    {
        // Regression guard: ensure the fix doesn't break the uncompressed path
        var path = Path.Combine(Path.GetTempPath(), $"readstream_none_{Guid.NewGuid():N}.scdb");
        var originalData = new byte[] { 72, 101, 108, 108, 111 }; // "Hello"

        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            BlockCompression = BlockCompressionMode.None,
            EnableEncryption = false,
            EnableMemoryMapping = false,
            CreateImmediately = true
        };

        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            provider.WriteBlockAsync("test:block", originalData).GetAwaiter().GetResult();
            provider.FlushAsync().GetAwaiter().GetResult();
        }

        using (var provider = SingleFileStorageProvider.Open(path, options))
        {
            using var stream = provider.GetReadStream("test:block");
            Assert.NotNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Assert.Equal(originalData, ms.ToArray());
        }

        Cleanup(path);
    }

    // ==========================================================
    // Helpers
    // ==========================================================

    private static void Cleanup(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        try { if (File.Exists(path + ".wal")) File.Delete(path + ".wal"); } catch { }
        try { if (File.Exists(path + ".backup")) File.Delete(path + ".backup"); } catch { }
    }

    private static byte[] RealisticCompressiblePayload()
    {
        var line = "INFO 2026-08-31T12:34:56.789Z [Thread-42] User login successful for user_id=987654321 from IP=192.168.1.100 session=abc123def456\n";

        var sb = new StringBuilder(line.Length * 2000);

        for (var i = 0; i < 2000; i++)
        {
            sb.Append(line);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] RealisticMetadataPayload()
    {
        var line = "{\"Tables\":[{\"Name\":\"telemetry\",\"Columns\":[\"id\",\"signal\",\"payload\",\"ts\"],\"PrimaryKeyIndex\":0}]}\n";

        var sb = new StringBuilder(line.Length * 1000);

        for (var i = 0; i < 1000; i++)
        {
            sb.Append(line);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
