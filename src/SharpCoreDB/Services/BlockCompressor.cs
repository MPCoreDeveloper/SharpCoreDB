// src/SharpCoreDB/Services/BlockCompressor.cs
namespace SharpCoreDB.Services;

using System;
using System.IO;
using System.IO.Compression;
using SharpCoreDB.Storage;

/// <summary>
/// Compression/decompression for SingleFile block payloads.
/// AOT-safe: uses only BCL streams, no reflection.
/// </summary>
internal static class BlockCompressor
{
    /// <summary>
    /// Compresses data using the specified compression mode.
    /// Returns the original data if mode is None.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, BlockCompressionMode mode)
    {
        if (mode == BlockCompressionMode.None) return data.ToArray();

        using var output = new MemoryStream(data.Length / 2);
        using (var compressor = CreateCompressor(output, mode))
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

    private static Stream CreateCompressor(Stream output, BlockCompressionMode mode) => mode switch
    {
        BlockCompressionMode.Brotli => new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: false),
        BlockCompressionMode.GZip => new GZipStream(output, CompressionLevel.Fastest, leaveOpen: false),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static Stream CreateDecompressor(Stream input, BlockCompressionMode mode) => mode switch
    {
        BlockCompressionMode.Brotli => new BrotliStream(input, CompressionMode.Decompress, leaveOpen: false),
        BlockCompressionMode.GZip => new GZipStream(input, CompressionMode.Decompress, leaveOpen: false),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}