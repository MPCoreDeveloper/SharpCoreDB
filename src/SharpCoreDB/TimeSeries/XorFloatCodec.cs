// <copyright file="XorFloatCodec.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.IO;

/// <summary>
/// Simplified XOR-based float compression.
/// C# 14: Modern patterns, aggressive optimization.
/// 
/// ✅ SCDB Phase 8.1: Time-Series Compression
/// 
/// Algorithm:
/// - Simpler than Gorilla, good for all patterns
/// - Store first value as-is
/// - XOR each value with previous
/// - Variable-length encoding based on XOR result
/// 
/// Performance:
/// - Similar values: 2-8x compression
/// - Random values: 1-1.5x compression
/// - Fallback when Gorilla isn't optimal
/// </summary>
public sealed class XorFloatCodec
{
    /// <summary>
    /// Compresses float64 array using XOR encoding.
    /// </summary>
    public byte[] Compress(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
            return [];

        if (values.Length == 1)
        {
            var result = new byte[sizeof(double)];
            BitConverter.TryWriteBytes(result, values[0]);
            return result;
        }

        using var stream = new MemoryStream(values.Length * 8);
        using var writer = new BitWriter(stream);

        // Write first value
        ulong prevBits = BitConverter.DoubleToUInt64Bits(values[0]);
        writer.WriteBits(prevBits, 64);

        for (int i = 1; i < values.Length; i++)
        {
            ulong currentBits = BitConverter.DoubleToUInt64Bits(values[i]);
            ulong xor = currentBits ^ prevBits;

            if (xor == 0)
            {
                // Same value: 1 bit
                writer.WriteBit(false);
            }
            else
            {
                writer.WriteBit(true);

                // Count significant bits
                int leadingZeros = CountLeadingZeros(xor);
                int trailingZeros = CountTrailingZeros(xor);
                int meaningfulBits = 64 - leadingZeros - trailingZeros;

                // Write control: 6 bits for leading zeros, 6 bits for length
                writer.WriteBits((ulong)leadingZeros, 6);
                writer.WriteBits((ulong)meaningfulBits, 6);

                // Write meaningful bits
                ulong meaningfulValue = (xor >> trailingZeros) & ((1UL << meaningfulBits) - 1);
                writer.WriteBits(meaningfulValue, meaningfulBits);
            }

            prevBits = currentBits;
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Decompresses XOR-encoded float64 array.
    /// </summary>
    public double[] Decompress(ReadOnlySpan<byte> compressed, int count)
    {
        if (count <= 0)
            return [];

        if (count == 1)
        {
            return [BitConverter.ToDouble(compressed)];
        }

        var result = new double[count];
        using var stream = new MemoryStream(compressed.ToArray());
        using var reader = new BitReader(stream);

        // Read first value
        ulong prevBits = reader.ReadBits(64);
        result[0] = BitConverter.UInt64BitsToDouble(prevBits);

        for (int i = 1; i < count; i++)
        {
            if (!reader.ReadBit())
            {
                // Same value
                result[i] = result[i - 1];
                continue;
            }

            // Read control
            int leadingZeros = (int)reader.ReadBits(6);
            int meaningfulBits = (int)reader.ReadBits(6);
            int trailingZeros = 64 - leadingZeros - meaningfulBits;

            // Read meaningful bits
            ulong meaningfulValue = reader.ReadBits(meaningfulBits);
            ulong xor = meaningfulValue << trailingZeros;

            ulong currentBits = prevBits ^ xor;
            result[i] = BitConverter.UInt64BitsToDouble(currentBits);
            prevBits = currentBits;
        }

        return result;
    }

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
