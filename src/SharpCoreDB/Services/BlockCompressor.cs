// <copyright file="BlockCompressor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.IO;
using System.IO.Compression;
using SharpCoreDB.Storage;
using SharpCoreDB.Compression;

/// <summary>
/// Compression/decompression for SingleFile block payloads.
/// AOT-safe: uses only BCL streams, no reflection.
/// </summary>
internal static class BlockCompressor
{
    /// <summary>
    /// Compresses data using the specified compression mode and level.
    /// Returns the original data if mode is None.
    /// </summary>
    public static byte[] Compress(
        ReadOnlySpan<byte> data, 
        BlockCompressionMode mode, 
        OptionalCompressionLevel level = OptionalCompressionLevel.Optimal)
    {
        if (mode == BlockCompressionMode.None) return data.ToArray();

        using var output = new MemoryStream(data.Length / 2);
        var compressionLevel = ToBcl(level);
        using (var compressor = CreateCompressor(output, mode, compressionLevel))
        {
            compressor.Write(data);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Decompresses data using the specified compression mode.
    /// Returns the original data if mode is None.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data, BlockCompressionMode mode)
    {
        if (mode == BlockCompressionMode.None) return data.ToArray();

        using var input = new MemoryStream(data.ToArray());
        using var decompressor = CreateDecompressor(input, mode);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private static Stream CreateCompressor(Stream output, BlockCompressionMode mode, CompressionLevel level) => mode switch
    {
        BlockCompressionMode.Brotli => new BrotliStream(output, level, leaveOpen: false),
        BlockCompressionMode.GZip => new GZipStream(output, level, leaveOpen: false),
#if NET11_0_OR_GREATER
        BlockCompressionMode.Zstd => new ZstandardStream(output, level, leaveOpen: false),
#else
        BlockCompressionMode.Zstd => throw new PlatformNotSupportedException(
            "Zstd compression requires .NET 11 or later. Current runtime: " + Environment.Version),
#endif
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static Stream CreateDecompressor(Stream input, BlockCompressionMode mode) => mode switch
    {
        BlockCompressionMode.Brotli => new BrotliStream(input, CompressionMode.Decompress, leaveOpen: false),
        BlockCompressionMode.GZip => new GZipStream(input, CompressionMode.Decompress, leaveOpen: false),
#if NET11_0_OR_GREATER
        BlockCompressionMode.Zstd => new ZstandardStream(input, CompressionMode.Decompress, leaveOpen: false),
#else
        BlockCompressionMode.Zstd => throw new PlatformNotSupportedException(
            "Zstd decompression requires .NET 11 or later. Current runtime: " + Environment.Version),
#endif
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    /// <summary>
    /// Maps OptionalCompressionLevel to BCL CompressionLevel.
    /// </summary>
    private static CompressionLevel ToBcl(OptionalCompressionLevel level) =>
        level switch
        {
            OptionalCompressionLevel.Fastest => CompressionLevel.Fastest,
            OptionalCompressionLevel.SmallestSize => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };
}
