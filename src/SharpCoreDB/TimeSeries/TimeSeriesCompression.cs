// <copyright file="TimeSeriesCompression.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Unified time-series compression facade.
/// C# 14: Modern patterns, collection expressions.
/// 
/// ✅ SCDB Phase 8.1: Time-Series Compression
/// 
/// Provides high-level API for time-series compression.
/// Automatically selects best codec based on data patterns.
/// </summary>
public static class TimeSeriesCompression
{
    private static readonly DeltaOfDeltaCodec _dodCodec = new();
    private static readonly GorillaCodec _gorillaCodec = new();
    private static readonly XorFloatCodec _xorCodec = new();

    /// <summary>
    /// Compression type enumeration.
    /// </summary>
    public enum CompressionType : byte
    {
        /// <summary>Delta-of-delta (timestamps).</summary>
        DeltaOfDelta = 1,

        /// <summary>Gorilla (smooth float64).</summary>
        Gorilla = 2,

        /// <summary>XOR float (general float64).</summary>
        XorFloat = 3,

        /// <summary>No compression.</summary>
        None = 0,
    }

    /// <summary>
    /// Compressed data container.
    /// </summary>
    public sealed record CompressedData
    {
        /// <summary>Compression type used.</summary>
        public required CompressionType Type { get; init; }

        /// <summary>Compressed bytes.</summary>
        public required byte[] Data { get; init; }

        /// <summary>Original value count.</summary>
        public required int Count { get; init; }

        /// <summary>Original uncompressed size.</summary>
        public required int UncompressedSize { get; init; }

        /// <summary>Compression ratio.</summary>
        public double CompressionRatio => UncompressedSize > 0
            ? (double)UncompressedSize / Data.Length
            : 1.0;
    }

    /// <summary>
    /// Compresses timestamps using delta-of-delta encoding.
    /// Optimal for sorted, uniform-interval timestamps.
    /// </summary>
    /// <param name="timestamps">Sorted timestamps (Unix millis or ticks).</param>
    /// <returns>Compressed data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static CompressedData CompressTimestamps(ReadOnlySpan<long> timestamps)
    {
        if (timestamps.IsEmpty)
        {
            return new CompressedData
            {
                Type = CompressionType.None,
                Data = [],
                Count = 0,
                UncompressedSize = 0
            };
        }

        var compressed = _dodCodec.Compress(timestamps);

        return new CompressedData
        {
            Type = CompressionType.DeltaOfDelta,
            Data = compressed,
            Count = timestamps.Length,
            UncompressedSize = timestamps.Length * sizeof(long)
        };
    }

    /// <summary>
    /// Compresses float64 values using best available codec.
    /// Auto-selects between Gorilla and XOR based on data pattern.
    /// </summary>
    /// <param name="values">Float64 values to compress.</param>
    /// <param name="useGorilla">Force Gorilla codec (default: auto-detect).</param>
    /// <returns>Compressed data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static CompressedData CompressValues(ReadOnlySpan<double> values, bool? useGorilla = null)
    {
        if (values.IsEmpty)
        {
            return new CompressedData
            {
                Type = CompressionType.None,
                Data = [],
                Count = 0,
                UncompressedSize = 0
            };
        }

        // Auto-detect best codec
        bool shouldUseGorilla = useGorilla ?? ShouldUseGorilla(values);

        byte[] compressed;
        CompressionType type;

        if (shouldUseGorilla)
        {
            compressed = _gorillaCodec.Compress(values);
            type = CompressionType.Gorilla;
        }
        else
        {
            compressed = _xorCodec.Compress(values);
            type = CompressionType.XorFloat;
        }

        return new CompressedData
        {
            Type = type,
            Data = compressed,
            Count = values.Length,
            UncompressedSize = values.Length * sizeof(double)
        };
    }

    /// <summary>
    /// Decompresses timestamps.
    /// </summary>
    /// <param name="compressed">Compressed data.</param>
    /// <returns>Decompressed timestamps.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long[] DecompressTimestamps(CompressedData compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        if (compressed.Type != CompressionType.DeltaOfDelta)
        {
            throw new ArgumentException(
                $"Expected DeltaOfDelta compression, got {compressed.Type}",
                nameof(compressed)
            );
        }

        return _dodCodec.Decompress(compressed.Data, compressed.Count);
    }

    /// <summary>
    /// Decompresses float64 values.
    /// </summary>
    /// <param name="compressed">Compressed data.</param>
    /// <returns>Decompressed values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double[] DecompressValues(CompressedData compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        return compressed.Type switch
        {
            CompressionType.Gorilla => _gorillaCodec.Decompress(compressed.Data, compressed.Count),
            CompressionType.XorFloat => _xorCodec.Decompress(compressed.Data, compressed.Count),
            _ => throw new ArgumentException(
                $"Unsupported compression type: {compressed.Type}",
                nameof(compressed)
            )
        };
    }

    /// <summary>
    /// Heuristic to determine if Gorilla is better than XOR for given data.
    /// </summary>
    private static bool ShouldUseGorilla(ReadOnlySpan<double> values)
    {
        if (values.Length < 10)
            return true; // Default to Gorilla for small datasets

        // Sample first 10 values to check smoothness
        double avgDelta = 0;
        int sampleSize = Math.Min(10, values.Length - 1);

        for (int i = 0; i < sampleSize; i++)
        {
            avgDelta += Math.Abs(values[i + 1] - values[i]);
        }

        avgDelta /= sampleSize;

        // Check if values are "smooth" (small deltas relative to magnitude)
        double avgMagnitude = 0;
        for (int i = 0; i < sampleSize + 1; i++)
        {
            avgMagnitude += Math.Abs(values[i]);
        }

        avgMagnitude /= (sampleSize + 1);

        // If delta is < 10% of magnitude, use Gorilla (smooth data)
        return avgMagnitude > 0 && (avgDelta / avgMagnitude) < 0.1;
    }

    /// <summary>
    /// Gets compression statistics for benchmarking.
    /// </summary>
    public static CompressionStats GetCompressionStats(CompressedData compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        return new CompressionStats
        {
            Type = compressed.Type,
            OriginalSize = compressed.UncompressedSize,
            CompressedSize = compressed.Data.Length,
            CompressionRatio = compressed.CompressionRatio,
            SpaceSavings = compressed.UncompressedSize > 0
                ? 1.0 - ((double)compressed.Data.Length / compressed.UncompressedSize)
                : 0.0
        };
    }

    /// <summary>
    /// Compression statistics.
    /// </summary>
    public sealed record CompressionStats
    {
        /// <summary>Compression type.</summary>
        public required CompressionType Type { get; init; }

        /// <summary>Original size in bytes.</summary>
        public required int OriginalSize { get; init; }

        /// <summary>Compressed size in bytes.</summary>
        public required int CompressedSize { get; init; }

        /// <summary>Compression ratio (original/compressed).</summary>
        public required double CompressionRatio { get; init; }

        /// <summary>Space savings (0.0 - 1.0).</summary>
        public required double SpaceSavings { get; init; }
    }
}
