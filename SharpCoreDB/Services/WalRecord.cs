// <copyright file="WalRecord.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

/// <summary>
/// Represents a WAL record with header containing length and checksum.
/// Format: [4-byte length][4-byte checksum][data bytes].
/// </summary>
public readonly struct WalRecord
{
    private const int HeaderSize = 8; // 4 bytes length + 4 bytes checksum

    /// <summary>
    /// Gets the data payload of this WAL record.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Gets the checksum of the data.
    /// </summary>
    public uint Checksum { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WalRecord"/> struct.
    /// </summary>
    /// <param name="data">The data payload.</param>
    public WalRecord(ReadOnlyMemory<byte> data)
    {
        Data = data;
        Checksum = ComputeChecksum(data.Span);
    }

    /// <summary>
    /// Computes CRC32 checksum for data integrity validation.
    /// Uses standard CRC32 algorithm with polynomial 0xEDB88320.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        return Crc32.Compute(data);
    }

    /// <summary>
    /// Writes this record to a span with header.
    /// Format: [length:4][checksum:4][data].
    /// </summary>
    /// <param name="destination">Destination span (must be at least HeaderSize + Data.Length).</param>
    /// <returns>Number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize + Data.Length)
        {
            throw new ArgumentException("Destination buffer too small", nameof(destination));
        }

        // Write length (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(destination, Data.Length);

        // Write checksum (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Checksum);

        // Write data
        Data.Span.CopyTo(destination[HeaderSize..]);

        return HeaderSize + Data.Length;
    }

    /// <summary>
    /// Reads a WAL record from a span.
    /// </summary>
    /// <param name="source">Source span containing record.</param>
    /// <param name="record">The parsed record.</param>
    /// <param name="bytesRead">Number of bytes consumed.</param>
    /// <returns>True if record was successfully parsed and validated.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryReadFrom(ReadOnlySpan<byte> source, out WalRecord record, out int bytesRead)
    {
        record = default;
        bytesRead = 0;

        // Need at least header
        if (source.Length < HeaderSize)
        {
            return false;
        }

        // Read length
        int length = BinaryPrimitives.ReadInt32LittleEndian(source);
        if (length < 0 || length > 100_000_000) // Sanity check: max 100MB per record
        {
            return false;
        }

        // Check if we have full record
        int totalSize = HeaderSize + length;
        if (source.Length < totalSize)
        {
            return false;
        }

        // Read checksum
        uint storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);

        // Extract data
        ReadOnlySpan<byte> data = source.Slice(HeaderSize, length);

        // Validate checksum
        uint computedChecksum = ComputeChecksum(data);
        if (computedChecksum != storedChecksum)
        {
            return false; // Corrupted record
        }

        // Create record
        byte[] dataArray = data.ToArray();
        record = new WalRecord(dataArray);
        bytesRead = totalSize;
        return true;
    }

    /// <summary>
    /// Gets the total size of this record including header.
    /// </summary>
    public int TotalSize => HeaderSize + Data.Length;
}

/// <summary>
/// CRC32 checksum implementation for data integrity validation.
/// Uses standard CRC32 algorithm with polynomial 0xEDB88320.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = GenerateTable();

    private static uint[] GenerateTable()
    {
        const uint Polynomial = 0xEDB88320;
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ Polynomial;
                }
                else
                {
                    crc >>= 1;
                }
            }
            table[i] = crc;
        }

        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in data)
        {
            byte index = (byte)((crc ^ b) & 0xFF);
            crc = (crc >> 8) ^ Table[index];
        }

        return ~crc;
    }
}
