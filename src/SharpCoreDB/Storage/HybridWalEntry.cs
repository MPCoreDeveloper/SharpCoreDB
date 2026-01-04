// <copyright file="HybridWalEntry.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

using System;
using System.Buffers.Binary;
using SharpCoreDB.Services;

/// <summary>
/// Unified WAL (Write-Ahead Log) entry structure for hybrid storage.
/// Supports both columnar and page-based storage modes with a single format.
/// 
/// Entry Layout (fixed header + variable data):
/// ┌─────────────────────────────────────────┐
/// │ LSN (8 bytes)                           │ Log Sequence Number
/// │ Transaction ID (8 bytes)                │ Transaction identifier
/// │ Timestamp (8 bytes)                     │ Unix timestamp (milliseconds)
/// │ Operation Type (1 byte)                 │ See WalOperationType enum
/// │ Table ID (4 bytes)                      │ Target table ID
/// │ Page ID (8 bytes)                       │ Page ID (0 for columnar)
/// │ Data Length (4 bytes)                   │ Length of payload
/// │ Data (variable)                         │ Operation-specific payload
/// │ CRC32 (4 bytes)                         │ Checksum for integrity
/// └─────────────────────────────────────────┘
/// 
/// Total header size: 45 bytes + variable data
/// </summary>
public class HybridWalEntry
{
    private const int HEADER_SIZE = 45;
    private const int CRC_SIZE = 4;

    /// <summary>
    /// Gets or sets the Log Sequence Number - monotonically increasing identifier.
    /// </summary>
    public ulong LSN { get; set; }

    /// <summary>
    /// Gets or sets the transaction ID that created this entry.
    /// </summary>
    public ulong TransactionId { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this entry was created (Unix milliseconds).
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the operation type (INSERT, UPDATE, DELETE, CHECKPOINT, etc.).
    /// </summary>
    public WalOperationType OperationType { get; set; }

    /// <summary>
    /// Gets or sets the target table ID.
    /// </summary>
    public uint TableId { get; set; }

    /// <summary>
    /// Gets or sets the page ID for page-based operations (0 for columnar operations).
    /// </summary>
    public ulong PageId { get; set; }

    /// <summary>
    /// Gets or sets the operation-specific data payload.
    /// For INSERT: serialized record data
    /// For UPDATE: old + new record data or delta
    /// For DELETE: record identifier
    /// For CHECKPOINT: checkpoint metadata
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the CRC32 checksum for integrity verification.
    /// </summary>
    public uint Checksum { get; set; }

    /// <summary>
    /// Serializes this WAL entry to a byte array.
    /// </summary>
    /// <returns>Byte array representation of the WAL entry.</returns>
    public byte[] ToBytes()
    {
        var totalSize = HEADER_SIZE + Data.Length + CRC_SIZE;
        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();

        int offset = 0;

        // Write LSN (8 bytes)
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), LSN);
        offset += 8;

        // Write Transaction ID (8 bytes)
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), TransactionId);
        offset += 8;

        // Write Timestamp (8 bytes)
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, 8), Timestamp);
        offset += 8;

        // Write Operation Type (1 byte)
        buffer[offset] = (byte)OperationType;
        offset += 1;

        // Write Table ID (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), TableId);
        offset += 4;

        // Write Page ID (8 bytes)
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), PageId);
        offset += 8;

        // Write Data Length (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), Data.Length);
        offset += 4;

        // Write Data (variable)
        if (Data.Length > 0)
        {
            Data.CopyTo(span.Slice(offset));
            offset += Data.Length;
        }

        // Calculate and write CRC32 (4 bytes) using System.IO.Hashing alternative
        var dataForChecksum = span.Slice(0, offset);
        var crc = ComputeCrc32(dataForChecksum);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), crc);

        Checksum = crc;

        return buffer;
    }

    /// <summary>
    /// Deserializes a WAL entry from a byte array.
    /// </summary>
    /// <param name="buffer">Byte array containing the WAL entry.</param>
    /// <returns>Deserialized WAL entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown if checksum validation fails.</exception>
    public static HybridWalEntry FromBytes(byte[] buffer)
    {
        if (buffer.Length < HEADER_SIZE + CRC_SIZE)
        {
            throw new InvalidOperationException(
                $"WAL entry too small: {buffer.Length} bytes, minimum required: {HEADER_SIZE + CRC_SIZE}");
        }

        var span = buffer.AsSpan();
        int offset = 0;

        // Read LSN
        var lsn = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Transaction ID
        var txId = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Timestamp
        var timestamp = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Operation Type
        var opType = (WalOperationType)buffer[offset];
        offset += 1;

        // Read Table ID
        var tableId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        // Read Page ID
        var pageId = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Data Length
        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        if (dataLength < 0 || offset + dataLength + CRC_SIZE > buffer.Length)
        {
            throw new InvalidOperationException(
                $"Invalid data length in WAL entry: {dataLength}, buffer size: {buffer.Length}");
        }

        // Read Data
        var data = new byte[dataLength];
        if (dataLength > 0)
        {
            span.Slice(offset, dataLength).CopyTo(data);
            offset += dataLength;
        }

        // Read and verify CRC32
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        var dataForChecksum = span.Slice(0, offset);
        var calculatedCrc = ComputeCrc32(dataForChecksum);

        if (storedCrc != calculatedCrc)
        {
            throw new InvalidOperationException(
                $"WAL entry checksum mismatch. Expected: 0x{storedCrc:X8}, Calculated: 0x{calculatedCrc:X8}");
        }

        return new HybridWalEntry
        {
            LSN = lsn,
            TransactionId = txId,
            Timestamp = timestamp,
            OperationType = opType,
            TableId = tableId,
            PageId = pageId,
            Data = data,
            Checksum = storedCrc
        };
    }

    /// <summary>
    /// Computes CRC32 checksum for the given data using the project's CRC32 implementation.
    /// </summary>
    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        return Crc32.Compute(data);
    }

    /// <summary>
    /// Gets the total size of this WAL entry in bytes.
    /// </summary>
    public int TotalSize => HEADER_SIZE + Data.Length + CRC_SIZE;

    /// <summary>
    /// Creates a WAL entry for a columnar INSERT operation.
    /// </summary>
    public static HybridWalEntry CreateColumnarInsert(ulong lsn, ulong txId, uint tableId, byte[] recordData)
    {
        return new HybridWalEntry
        {
            LSN = lsn,
            TransactionId = txId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            OperationType = WalOperationType.InsertColumnar,
            TableId = tableId,
            PageId = 0, // Columnar doesn't use pages
            Data = recordData
        };
    }

    /// <summary>
    /// Creates a WAL entry for a page-based INSERT operation.
    /// </summary>
    public static HybridWalEntry CreatePageInsert(ulong lsn, ulong txId, uint tableId, ulong pageId, byte[] recordData)
    {
        return new HybridWalEntry
        {
            LSN = lsn,
            TransactionId = txId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            OperationType = WalOperationType.InsertPage,
            TableId = tableId,
            PageId = pageId,
            Data = recordData
        };
    }

    /// <summary>
    /// Creates a WAL entry for a page-based in-place UPDATE operation.
    /// </summary>
    public static HybridWalEntry CreatePageUpdate(ulong lsn, ulong txId, uint tableId, ulong pageId, byte[] updateData)
    {
        return new HybridWalEntry
        {
            LSN = lsn,
            TransactionId = txId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            OperationType = WalOperationType.UpdatePageInPlace,
            TableId = tableId,
            PageId = pageId,
            Data = updateData
        };
    }

    /// <summary>
    /// Creates a WAL entry for a DELETE operation.
    /// </summary>
    public static HybridWalEntry CreateDelete(ulong lsn, ulong txId, uint tableId, ulong pageId, byte[] deleteData, bool isPageBased)
    {
        return new HybridWalEntry
        {
            LSN = lsn,
            TransactionId = txId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            OperationType = isPageBased ? WalOperationType.DeletePage : WalOperationType.DeleteColumnar,
            TableId = tableId,
            PageId = pageId,
            Data = deleteData
        };
    }

    /// <summary>
    /// Creates a CHECKPOINT WAL entry.
    /// </summary>
    public static HybridWalEntry CreateCheckpoint(ulong lsn, ulong txId, byte[] checkpointData)
    {
        return new HybridWalEntry
        {
            LSN = lsn,
            TransactionId = txId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            OperationType = WalOperationType.Checkpoint,
            TableId = 0,
            PageId = 0,
            Data = checkpointData
        };
    }

    /// <summary>
    /// Returns a string representation of this WAL entry for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"WAL[LSN={LSN}, TxID={TransactionId}, Op={OperationType}, Table={TableId}, " +
               $"Page={PageId}, DataLen={Data.Length}, CRC=0x{Checksum:X8}]";
    }
}
