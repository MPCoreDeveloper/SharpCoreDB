// <copyright file="GorillaCodec.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.IO;

/// <summary>
/// Gorilla compression codec for float64 values.
/// Based on Facebook's Gorilla paper (2015).
/// C# 14: Modern patterns, aggressive optimization.
/// 
/// ✅ SCDB Phase 8.1: Time-Series Compression
/// 
/// Algorithm:
/// - First value: stored as-is (64 bits)
/// - Subsequent values: XOR with previous value
///   - If XOR = 0: write '0' (1 bit) - value unchanged
///   - If XOR has same leading/trailing zeros: write '10' + meaningful bits
///   - Otherwise: write '11' + 5-bit leading zeros + 6-bit length + meaningful bits
/// 
/// Performance:
/// - Smooth metrics (temperature, CPU): 5-20x compression
/// - Step changes: 2-5x compression
/// - Random values: 1-2x compression
/// 
/// Key insight: Similar consecutive float values have many common bits!
/// </summary>
public sealed class GorillaCodec
{
    /// <summary>
    /// Compresses an array of float64 values using Gorilla algorithm.
    /// </summary>
    /// <param name="values">Float64 values to compress.</param>
    /// <returns>Compressed byte array.</returns>
    public byte[] Compress(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
            return [];

        if (values.Length == 1)
        {
            // Single value: just store it
            var result = new byte[sizeof(double)];
            BitConverter.TryWriteBytes(result, values[0]);
            return result;
        }

        // Estimate output size
        using var stream = new MemoryStream(values.Length * 8);
        using var writer = new BitWriter(stream);

        // Write first value (64 bits)
        ulong firstBits = BitConverter.DoubleToUInt64Bits(values[0]);
        writer.WriteBits(firstBits, 64);

        ulong prevBits = firstBits;
        int prevLeadingZeros = -1; // Initialize to -1 to force first XOR to use '11'
        int prevTrailingZeros = 0;

        for (int i = 1; i < values.Length; i++)
        {
            ulong currentBits = BitConverter.DoubleToUInt64Bits(values[i]);
            ulong xor = currentBits ^ prevBits;

            if (xor == 0)
            {
                // Value unchanged: write '0'
                writer.WriteBit(false);
            }
            else
            {
                writer.WriteBit(true);

                int leadingZeros = CountLeadingZeros(xor);
                int trailingZeros = CountTrailingZeros(xor);
                int meaningfulBits = 64 - leadingZeros - trailingZeros;

                // Ensure meaningful bits is at least 1 (xor != 0 guaranteed by earlier check)
                if (meaningfulBits <= 0)
                {
                    meaningfulBits = 64;
                    leadingZeros = 0;
                    trailingZeros = 0;
                }

                if (prevLeadingZeros >= 0 && leadingZeros >= prevLeadingZeros && trailingZeros >= prevTrailingZeros)
                {
                    // Same or more leading/trailing zeros: write '0' + meaningful bits
                    writer.WriteBit(false);

                    // Use previous control block
                    int blockSize = 64 - prevLeadingZeros - prevTrailingZeros;
                    ulong meaningfulValue = (xor >> prevTrailingZeros);
                    if (blockSize < 64)
                    {
                        meaningfulValue &= (1UL << blockSize) - 1;
                    }
                    writer.WriteBits(meaningfulValue, blockSize);
                }
                else
                {
                    // Different leading/trailing zeros: write '1' + control block + meaningful bits
                    writer.WriteBit(true);

                    // Write control block: 5 bits for leading zeros, 6 bits for meaningful bits length
                    // Note: 6 bits can represent 0-63, so we store (meaningfulBits - 1) to represent 1-64
                    writer.WriteBits((ulong)leadingZeros, 5);
                    writer.WriteBits((ulong)(meaningfulBits - 1), 6);

                    // Write meaningful bits
                    ulong meaningfulValue = (xor >> trailingZeros);
                    if (meaningfulBits < 64)
                    {
                        meaningfulValue &= (1UL << meaningfulBits) - 1;
                    }
                    writer.WriteBits(meaningfulValue, meaningfulBits);

                    prevLeadingZeros = leadingZeros;
                    prevTrailingZeros = trailingZeros;
                }
            }

            prevBits = currentBits;
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Decompresses Gorilla-encoded float64 values.
    /// </summary>
    /// <param name="compressed">Compressed byte array.</param>
    /// <param name="count">Number of values to decompress.</param>
    /// <returns>Decompressed float64 values.</returns>
    public double[] Decompress(ReadOnlySpan<byte> compressed, int count)
    {
        if (count <= 0)
            return [];

        if (count == 1)
        {
            // Single value
            return [BitConverter.ToDouble(compressed)];
        }

        var result = new double[count];
        using var stream = new MemoryStream(compressed.ToArray());
        using var reader = new BitReader(stream);

        // Read first value
        ulong prevBits = reader.ReadBits(64);
        result[0] = BitConverter.UInt64BitsToDouble(prevBits);

        int prevLeadingZeros = 0;
        int prevTrailingZeros = 0;

        for (int i = 1; i < count; i++)
        {
            if (!reader.ReadBit())
            {
                // '0' -> value unchanged
                result[i] = result[i - 1];
                prevBits = BitConverter.DoubleToUInt64Bits(result[i - 1]);
                continue;
            }

            ulong xor;

            if (!reader.ReadBit())
            {
                // '10' -> use previous control block
                int blockSize = 64 - prevLeadingZeros - prevTrailingZeros;
                ulong meaningfulValue = reader.ReadBits(blockSize);
                xor = meaningfulValue << prevTrailingZeros;
            }
            else
            {
                // '11' -> new control block
                int leadingZeros = (int)reader.ReadBits(5);
                int meaningfulBits = (int)reader.ReadBits(6) + 1; // Add 1 to get 1-64 range
                int trailingZeros = 64 - leadingZeros - meaningfulBits;

                ulong meaningfulValue = reader.ReadBits(meaningfulBits);
                xor = meaningfulValue << trailingZeros;

                prevLeadingZeros = leadingZeros;
                prevTrailingZeros = trailingZeros;
            }

            ulong currentBits = prevBits ^ xor;
            result[i] = BitConverter.UInt64BitsToDouble(currentBits);
            prevBits = currentBits;
        }

        return result;
    }

    // Helper methods

    private static int CountLeadingZeros(ulong value)
    {
        if (value == 0)
            return 64;

        int count = 0;
        while ((value & 0x8000000000000000UL) == 0)
        {
            count++;
            value <<= 1;
        }

        return count;
    }

    private static int CountTrailingZeros(ulong value)
    {
        if (value == 0)
            return 64;

        int count = 0;
        while ((value & 1) == 0)
        {
            count++;
            value >>= 1;
        }

        return count;
    }
}
