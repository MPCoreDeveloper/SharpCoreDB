// <copyright file="BlockCompressor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Buffers;
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

#if NET11_0_OR_GREATER
        // Zstd uses the BCL's span-based encoder instead of the stream wrapper (plan §9 / round-3
        // research rec 8). This runs once per block above DatabaseOptions.CompressionThreshold on the
        // write path, and the stream shape allocated a MemoryStream, a ZstandardStream and their
        // internal buffers per call. Measured in a standalone probe on .NET 11 RC1 (2026-10-09):
        // 2.480 B per 4 KB block and 33.896 B per 64 KB block, against ~77 B / ~785 B — the returned
        // frame — for the encoder + pooled-destination shape below.
        //
        // The encoder is reused per thread through Reset(), which the same probe proved produces a
        // frame byte-identical to a fresh instance's. It is deliberately NOT claimed to be identical
        // to the stream wrapper's frame: at quality 5 the encoder's frame measured 77 B where the
        // stream's was 83 B on a 4 KB block, and 785 B where the stream's was 784 B on a 64 KB one —
        // so the sizes differ in either direction and the difference was measured, not decomposed.
        // Compatibility rests on both being valid Zstandard frames that Decompress reads, which is what
        // BlockCompressor_Zstd_BlockSizesAroundThreshold_RoundTrip and
        // BlockCompressor_Decompress_ReadsFramesWrittenByTheStreamWriter (tests/SharpCoreDB.Tests/
        // Storage/CompressionLevelTests.cs) pin.
        //
        // Failure of any premise falls back to the stream path below rather than to a wrong frame.
        if (mode == BlockCompressionMode.Zstd && TryCompressZstd(data, level, out byte[] frame))
        {
            return frame;
        }
#endif

        using var output = new MemoryStream(data.Length / 2);
        using (var compressor = CreateCompressor(output, mode, level))
        {
            compressor.Write(data);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Decompresses data using the specified compression mode.
    /// Returns the original data if mode is None.
    /// </summary>
    /// <remarks>
    /// Deliberately still stream-based, including for Zstd. A span-based decoder needs a destination
    /// size up front, and <c>ZstandardDecoder.TryGetMaxDecompressedLength</c> is not usable for the
    /// frames already on disk: for a stream-written frame it answers with the 128 KiB window bound
    /// rather than the block's content size (probe, RC.1: a 4 KB block written by this class's stream
    /// path reports 131.072). Reading is not the write path this plan measures, so the growth-on-demand
    /// MemoryStream stays until a reader-shaped measurement justifies the change.
    /// </remarks>
    public static byte[] Decompress(ReadOnlySpan<byte> data, BlockCompressionMode mode)
    {
        if (mode == BlockCompressionMode.None) return data.ToArray();

        using var input = new MemoryStream(data.ToArray());
        using var decompressor = CreateDecompressor(input, mode);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private static Stream CreateCompressor(Stream output, BlockCompressionMode mode, OptionalCompressionLevel level) => mode switch
    {
        BlockCompressionMode.Brotli => new BrotliStream(output, ToBcl(level), leaveOpen: false),
        BlockCompressionMode.GZip => new GZipStream(output, ToBcl(level), leaveOpen: false),
#if NET11_0_OR_GREATER
        BlockCompressionMode.Zstd => new ZstandardStream(output, ZstdOptions(level), leaveOpen: false),
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

#if NET11_0_OR_GREATER
    /// <summary>
    /// This thread's reusable Zstd encoder, or <c>null</c> when the span path has not run yet.
    /// </summary>
    /// <remarks>
    /// <c>Reset()</c> carries an instance to its next frame and keeps the options it was built with
    /// (proved on RC.1: a reset instance's frame is byte-identical to a fresh instance's), so the
    /// encoder is rebuilt only when a caller asks for a different <see cref="OptionalCompressionLevel"/>.
    /// Thread-static is the same single-writer-per-handle assumption the storage layer's cached file
    /// handles already make, and it keeps the reuse lock-free.
    /// </remarks>
    [ThreadStatic]
    private static ZstandardEncoder? _zstdEncoder;

    /// <summary>The level <see cref="_zstdEncoder"/> was constructed with, or <c>null</c> when unused.</summary>
    [ThreadStatic]
    private static OptionalCompressionLevel? _zstdEncoderLevel;

    /// <summary>
    /// Compresses <paramref name="data"/> with a reused encoder and a pooled destination.
    /// </summary>
    /// <returns>
    /// <c>false</c> — leaving <paramref name="frame"/> empty — when the encoder did not finish in one
    /// call, so <see cref="Compress"/> can take its stream path instead of publishing a frame that
    /// might be truncated.
    /// </returns>
    private static bool TryCompressZstd(ReadOnlySpan<byte> data, OptionalCompressionLevel level, out byte[] frame)
    {
        frame = [];

        // Empty input keeps the stream path: it produces the valid empty frame, and sizing a pooled
        // buffer for zero bytes buys nothing.
        if (data.IsEmpty) return false;

        int capacity = checked((int)ZstandardEncoder.GetMaxCompressedLength(data.Length));
        byte[] destination = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            ZstandardEncoder encoder = GetZstdEncoder(level);
            OperationStatus status = encoder.Compress(
                data,
                destination.AsSpan(0, capacity),
                out int consumed,
                out int written,
                isFinalBlock: true);

            if (status != OperationStatus.Done || consumed != data.Length)
            {
                // Unreachable in practice: GetMaxCompressedLength is an upper bound (probe measured
                // 4.174 B of capacity for a 4 KB block that produced 84 B), and isFinalBlock: true
                // ends the frame in one call. It exists so a future sizing change cannot become a
                // corrupt block on disk.
                return false;
            }

            frame = destination.AsSpan(0, written).ToArray();
            return true;
        }
        finally
        {
            // Returned even when the frame was published: the returned array is a copy.
            ArrayPool<byte>.Shared.Return(destination);
        }
    }

    /// <summary>
    /// Returns this thread's Zstd encoder for <paramref name="level"/>, reset for a new frame.
    /// </summary>
    private static ZstandardEncoder GetZstdEncoder(OptionalCompressionLevel level)
    {
        if (_zstdEncoder is null || _zstdEncoderLevel != level)
        {
            _zstdEncoder?.Dispose();
            _zstdEncoder = new ZstandardEncoder(ZstdOptions(level));
            _zstdEncoderLevel = level;
        }
        else
        {
            _zstdEncoder.Reset();
        }

        return _zstdEncoder;
    }

    /// <summary>
    /// Builds Zstandard compression options for the net11.0 native Zstandard path.
    /// <see cref="OptionalCompressionLevel"/> is mapped onto Zstandard's quality scale (1 = fastest,
    /// 22 = best ratio), and a frame checksum is appended so corruption is detected on read.
    /// </summary>
    private static ZstandardCompressionOptions ZstdOptions(OptionalCompressionLevel level) =>
        new()
        {
            Quality = level switch
            {
                OptionalCompressionLevel.Fastest => 1,
                OptionalCompressionLevel.SmallestSize => 19,
                _ => 5, // Optimal: balanced, above zstd's default level 3
            },
            AppendChecksum = true,
        };
#endif
}