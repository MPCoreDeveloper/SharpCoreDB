// <copyright file="DeltaOfDeltaCodec.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Buffers;
using System.IO;

/// <summary>
/// Delta-of-delta timestamp compression codec.
/// Based on Facebook's Gorilla paper (2015).
/// C# 14: Modern patterns, aggressive optimization.
/// 
/// ✅ SCDB Phase 8.1: Time-Series Compression
/// 
/// Algorithm:
/// - First value: stored as-is (64 bits)
/// - First delta: stored as-is (64 bits with sign)
/// - Subsequent values: store delta-of-delta using variable-length encoding:
///   - DoD = 0: 1 bit ('0')
///   - DoD in [-63, 64]: 2 + 7 bits ('10' + 7-bit value)
///   - DoD in [-255, 256]: 3 + 9 bits ('110' + 9-bit value)
///   - DoD in [-2047, 2048]: 4 + 12 bits ('1110' + 12-bit value)
///   - Otherwise: 5 + 32 bits ('1111' + 32-bit value)
/// 
/// Performance:
/// - Uniform intervals: ~1 bit per timestamp (16-64x compression)
/// - Near-uniform: ~2-8 bits per timestamp (8-32x compression)
/// - Random: ~32-40 bits per timestamp (1.6-2x compression)
/// </summary>
public sealed class DeltaOfDeltaCodec
{
    /// <summary>
    /// Compresses an array of timestamps using delta-of-delta encoding.
    /// </summary>
    /// <param name="timestamps">Sorted timestamps to compress (Unix milliseconds or ticks).</param>
    /// <returns>Compressed byte array.</returns>
    public byte[] Compress(ReadOnlySpan<long> timestamps)
    {
        if (timestamps.IsEmpty)
            return [];

        if (timestamps.Length == 1)
        {
            // Single timestamp: just store it
            var result = new byte[sizeof(long)];
            BitConverter.TryWriteBytes(result, timestamps[0]);
            return result;
        }

        // Estimate output size (worst case: ~5 bytes per timestamp)
        using var stream = new MemoryStream(timestamps.Length * 5);
        using var writer = new BitWriter(stream);

        // Write first timestamp (64 bits)
        writer.WriteBits((ulong)timestamps[0], 64);

        // Write first delta (64 bits with sign bit)
        long firstDelta = timestamps[1] - timestamps[0];
        writer.WriteBits((ulong)firstDelta, 64);

        // Write subsequent deltas-of-deltas
        long prevDelta = firstDelta;

        for (int i = 2; i < timestamps.Length; i++)
        {
            long delta = timestamps[i] - timestamps[i - 1];
            long deltaOfDelta = delta - prevDelta;

            WriteDoD(writer, deltaOfDelta);

            prevDelta = delta;
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Decompresses delta-of-delta encoded timestamps.
    /// </summary>
    /// <param name="compressed">Compressed byte array.</param>
    /// <param name="count">Number of timestamps to decompress.</param>
    /// <returns>Decompressed timestamps.</returns>
    public long[] Decompress(ReadOnlySpan<byte> compressed, int count)
    {
        if (count <= 0)
            return [];

        if (count == 1)
        {
            // Single timestamp
            return [BitConverter.ToInt64(compressed)];
        }

        var result = new long[count];
        using var stream = new MemoryStream(compressed.ToArray());
        using var reader = new BitReader(stream);

        // Read first timestamp
        result[0] = (long)reader.ReadBits(64);

        if (count == 1)
            return result;

        // Read first delta
        long firstDelta = (long)reader.ReadBits(64);
        result[1] = result[0] + firstDelta;

        // Read subsequent deltas-of-deltas
        long prevDelta = firstDelta;

        for (int i = 2; i < count; i++)
        {
            long deltaOfDelta = ReadDoD(reader);
            long delta = prevDelta + deltaOfDelta;
            result[i] = result[i - 1] + delta;

            prevDelta = delta;
        }

        return result;
    }

    // Private helper methods

    private static void WriteDoD(BitWriter writer, long dod)
    {
        if (dod == 0)
        {
            // DoD = 0: write '0' (1 bit)
            writer.WriteBit(false);
        }
        else if (dod >= -63 && dod <= 64)
        {
            // DoD in [-63, 64]: write '10' + 7 bits
            writer.WriteBit(true);
            writer.WriteBit(false);
            writer.WriteBits((ulong)(dod & 0x7F), 7);
        }
        else if (dod >= -255 && dod <= 256)
        {
            // DoD in [-255, 256]: write '110' + 9 bits
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBit(false);
            writer.WriteBits((ulong)(dod & 0x1FF), 9);
        }
        else if (dod >= -2047 && dod <= 2048)
        {
            // DoD in [-2047, 2048]: write '1110' + 12 bits
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBit(false);
            writer.WriteBits((ulong)(dod & 0xFFF), 12);
        }
        else
        {
            // Otherwise: write '1111' + 32 bits
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBit(true);
            writer.WriteBits((ulong)((int)dod), 32);
        }
    }

    private static long ReadDoD(BitReader reader)
    {
        if (!reader.ReadBit())
        {
            // '0' -> DoD = 0
            return 0;
        }

        if (!reader.ReadBit())
        {
            // '10' -> read 7 bits
            long value = (long)reader.ReadBits(7);
            // Sign-extend from 7 bits
            if ((value & 0x40) != 0)
                value |= unchecked((long)0xFFFFFFFFFFFFFF80);
            return value;
        }

        if (!reader.ReadBit())
        {
            // '110' -> read 9 bits
            long value = (long)reader.ReadBits(9);
            // Sign-extend from 9 bits
            if ((value & 0x100) != 0)
                value |= unchecked((long)0xFFFFFFFFFFFFFE00);
            return value;
        }

        if (!reader.ReadBit())
        {
            // '1110' -> read 12 bits
            long value = (long)reader.ReadBits(12);
            // Sign-extend from 12 bits
            if ((value & 0x800) != 0)
                value |= unchecked((long)0xFFFFFFFFFFFFF000);
            return value;
        }

        // '1111' -> read 32 bits
        return (int)reader.ReadBits(32);
    }
}

/// <summary>
/// Bit-level writer for compression.
/// </summary>
internal sealed class BitWriter : IDisposable
{
    private readonly Stream _stream;
    private byte _currentByte;
    private int _bitPosition;

    public BitWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public void WriteBit(bool bit)
    {
        if (bit)
        {
            _currentByte |= (byte)(1 << (7 - _bitPosition));
        }

        _bitPosition++;

        if (_bitPosition == 8)
        {
            _stream.WriteByte(_currentByte);
            _currentByte = 0;
            _bitPosition = 0;
        }
    }

    public void WriteBits(ulong value, int bits)
    {
        for (int i = bits - 1; i >= 0; i--)
        {
            WriteBit(((value >> i) & 1) != 0);
        }
    }

    public void Flush()
    {
        if (_bitPosition > 0)
        {
            _stream.WriteByte(_currentByte);
            _currentByte = 0;
            _bitPosition = 0;
        }
    }

    public void Dispose()
    {
        Flush();
    }
}

/// <summary>
/// Bit-level reader for decompression.
/// </summary>
internal sealed class BitReader : IDisposable
{
    private readonly Stream _stream;
    private byte _currentByte;
    private int _bitPosition = 8;

    public BitReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public bool ReadBit()
    {
        if (_bitPosition == 8)
        {
            int b = _stream.ReadByte();
            if (b == -1)
                throw new EndOfStreamException();

            _currentByte = (byte)b;
            _bitPosition = 0;
        }

        bool bit = ((_currentByte >> (7 - _bitPosition)) & 1) != 0;
        _bitPosition++;
        return bit;
    }

    public ulong ReadBits(int bits)
    {
        ulong value = 0;

        for (int i = 0; i < bits; i++)
        {
            value = (value << 1) | (ReadBit() ? 1UL : 0UL);
        }

        return value;
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
