// <copyright file="CheckpointMetadata.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Metadata for a database checkpoint.
/// Checkpoints mark a point in the WAL where all previous operations
/// have been flushed to disk, allowing WAL truncation and faster recovery.
/// 
/// Checkpoint Metadata Format:
/// ┌─────────────────────────────────────────┐
/// │ Checkpoint LSN (8 bytes)                │ LSN of this checkpoint
/// │ Previous Checkpoint LSN (8 bytes)       │ LSN of prior checkpoint (for chaining)
/// │ Timestamp (8 bytes)                     │ Unix timestamp (milliseconds)
/// │ Dirty Pages Flushed Count (4 bytes)    │ Number of dirty pages written
/// │ Columnar Tables Count (4 bytes)        │ Number of columnar tables
/// │ Page-Based Tables Count (4 bytes)      │ Number of page-based tables
/// │ Total WAL Size (8 bytes)                │ Total WAL file size at checkpoint
/// │ Dirty Page IDs (variable)               │ List of flushed page IDs
/// │ Table Checksums (variable)              │ Per-table CRC32 checksums
/// └─────────────────────────────────────────┘
/// </summary>
public class CheckpointMetadata
{
    /// <summary>
    /// Gets or sets the LSN of this checkpoint.
    /// Recovery starts from this LSN.
    /// </summary>
    public ulong CheckpointLSN { get; set; }

    /// <summary>
    /// Gets or sets the LSN of the previous checkpoint (for checkpoint chaining).
    /// Used to find the last valid checkpoint if current one is corrupted.
    /// </summary>
    public ulong PreviousCheckpointLSN { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this checkpoint was created.
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the list of dirty page IDs that were flushed during this checkpoint.
    /// Used during recovery to know which pages are guaranteed on disk.
    /// </summary>
    public List<ulong> DirtyPageIdsFlushed { get; set; } = new();

    /// <summary>
    /// Gets or sets the per-table checksums for data integrity verification.
    /// Key: Table ID, Value: CRC32 checksum of table data.
    /// </summary>
    public Dictionary<uint, uint> TableChecksums { get; set; } = new();

    /// <summary>
    /// Gets or sets the total WAL file size at the time of checkpoint.
    /// Used to determine when WAL truncation is beneficial.
    /// </summary>
    public long TotalWalSize { get; set; }

    /// <summary>
    /// Gets the number of dirty pages that were flushed.
    /// </summary>
    public int DirtyPagesFlushed => DirtyPageIdsFlushed.Count;

    /// <summary>
    /// Gets the number of columnar tables at checkpoint time.
    /// </summary>
    public int ColumnarTablesCount { get; set; }

    /// <summary>
    /// Gets the number of page-based tables at checkpoint time.
    /// </summary>
    public int PageBasedTablesCount { get; set; }

    /// <summary>
    /// Serializes this checkpoint metadata to a byte array.
    /// </summary>
    /// <returns>Byte array representation of the checkpoint metadata.</returns>
    public byte[] ToBytes()
    {
        // Calculate total size
        int headerSize = 44; // Fixed header fields
        int dirtyPagesSize = 4 + (DirtyPageIdsFlushed.Count * 8); // Count + page IDs
        int checksumsSize = 4 + (TableChecksums.Count * 12); // Count + (tableId + checksum) pairs
        int totalSize = headerSize + dirtyPagesSize + checksumsSize;

        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();
        int offset = 0;

        // Write Checkpoint LSN (8 bytes)
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), CheckpointLSN);
        offset += 8;

        // Write Previous Checkpoint LSN (8 bytes)
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), PreviousCheckpointLSN);
        offset += 8;

        // Write Timestamp (8 bytes)
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, 8), Timestamp);
        offset += 8;

        // Write Dirty Pages Flushed Count (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), DirtyPageIdsFlushed.Count);
        offset += 4;

        // Write Columnar Tables Count (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), ColumnarTablesCount);
        offset += 4;

        // Write Page-Based Tables Count (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), PageBasedTablesCount);
        offset += 4;

        // Write Total WAL Size (8 bytes)
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, 8), TotalWalSize);
        offset += 8;

        // Write Dirty Page IDs count (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), DirtyPageIdsFlushed.Count);
        offset += 4;

        // Write Dirty Page IDs (8 bytes each)
        foreach (var pageId in DirtyPageIdsFlushed)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), pageId);
            offset += 8;
        }

        // Write Table Checksums count (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), TableChecksums.Count);
        offset += 4;

        // Write Table Checksums (12 bytes each: tableId + checksum)
        foreach (var (tableId, checksum) in TableChecksums)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), tableId);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), checksum);
            offset += 4;
            offset += 4; // Reserved for future use
        }

        return buffer;
    }

    /// <summary>
    /// Deserializes checkpoint metadata from a byte array.
    /// </summary>
    /// <param name="buffer">Byte array containing the checkpoint metadata.</param>
    /// <returns>Deserialized checkpoint metadata.</returns>
    /// <exception cref="InvalidOperationException">Thrown if buffer is invalid.</exception>
    public static CheckpointMetadata FromBytes(byte[] buffer)
    {
        if (buffer.Length < 44)
        {
            throw new InvalidOperationException(
                $"Checkpoint metadata too small: {buffer.Length} bytes, minimum required: 44");
        }

        var span = buffer.AsSpan();
        int offset = 0;

        var metadata = new CheckpointMetadata();

        // Read Checkpoint LSN
        metadata.CheckpointLSN = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Previous Checkpoint LSN
        metadata.PreviousCheckpointLSN = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Timestamp
        metadata.Timestamp = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Dirty Pages Flushed Count
        _ = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4)); // Legacy field, not used
        offset += 4;

        // Read Columnar Tables Count
        metadata.ColumnarTablesCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        // Read Page-Based Tables Count
        metadata.PageBasedTablesCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        // Read Total WAL Size
        metadata.TotalWalSize = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        // Read Dirty Page IDs count
        var dirtyPageIdsCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        if (dirtyPageIdsCount < 0 || dirtyPageIdsCount > 1000000)
        {
            throw new InvalidOperationException(
                $"Invalid dirty pages count: {dirtyPageIdsCount}");
        }

        // Read Dirty Page IDs
        for (int i = 0; i < dirtyPageIdsCount; i++)
        {
            if (offset + 8 > buffer.Length)
            {
                throw new InvalidOperationException("Buffer too small for dirty page IDs");
            }

            var pageId = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
            metadata.DirtyPageIdsFlushed.Add(pageId);
            offset += 8;
        }

        // Read Table Checksums count
        if (offset + 4 > buffer.Length)
        {
            // Optional field - might not exist in older formats
            return metadata;
        }

        var checksumsCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        if (checksumsCount < 0 || checksumsCount > 10000)
        {
            throw new InvalidOperationException(
                $"Invalid checksums count: {checksumsCount}");
        }

        // Read Table Checksums
        for (int i = 0; i < checksumsCount; i++)
        {
            if (offset + 12 > buffer.Length)
            {
                throw new InvalidOperationException("Buffer too small for table checksums");
            }

            var tableId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
            offset += 4;
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
            offset += 4;
            offset += 4; // Reserved

            metadata.TableChecksums[tableId] = checksum;
        }

        return metadata;
    }

    /// <summary>
    /// Creates a new checkpoint metadata with current timestamp.
    /// </summary>
    public static CheckpointMetadata Create(ulong lsn, ulong previousLsn)
    {
        return new CheckpointMetadata
        {
            CheckpointLSN = lsn,
            PreviousCheckpointLSN = previousLsn,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    /// <summary>
    /// Adds a dirty page ID to the flushed list.
    /// </summary>
    public void AddDirtyPage(ulong pageId)
    {
        if (!DirtyPageIdsFlushed.Contains(pageId))
        {
            DirtyPageIdsFlushed.Add(pageId);
        }
    }

    /// <summary>
    /// Adds a table checksum.
    /// </summary>
    public void AddTableChecksum(uint tableId, uint checksum)
    {
        TableChecksums[tableId] = checksum;
    }

    /// <summary>
    /// Returns whether this checkpoint is valid (has a non-zero LSN).
    /// </summary>
    public bool IsValid => CheckpointLSN > 0;

    /// <summary>
    /// Returns a string representation of this checkpoint for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"Checkpoint[LSN={CheckpointLSN}, Prev={PreviousCheckpointLSN}, " +
               $"DirtyPages={DirtyPagesFlushed}, Tables={ColumnarTablesCount + PageBasedTablesCount}, " +
               $"WalSize={TotalWalSize / 1024}KB, Time={DateTimeOffset.FromUnixTimeMilliseconds(Timestamp):yyyy-MM-dd HH:mm:ss}]";
    }

    /// <summary>
    /// Returns detailed statistics about this checkpoint.
    /// </summary>
    public string GetDetailedStats()
    {
        return $"""
            Checkpoint Statistics:
            =====================
            Checkpoint LSN:        {CheckpointLSN}
            Previous LSN:          {PreviousCheckpointLSN}
            Timestamp:             {DateTimeOffset.FromUnixTimeMilliseconds(Timestamp):yyyy-MM-dd HH:mm:ss.fff}
            Dirty Pages Flushed:   {DirtyPagesFlushed:N0}
            Columnar Tables:       {ColumnarTablesCount}
            Page-Based Tables:     {PageBasedTablesCount}
            Total Tables:          {ColumnarTablesCount + PageBasedTablesCount}
            WAL Size:              {TotalWalSize / 1024.0:F2} KB
            Table Checksums:       {TableChecksums.Count}
            Page IDs:              {string.Join(", ", DirtyPageIdsFlushed.Take(10))}{(DirtyPagesFlushed > 10 ? "..." : "")}
            """;
    }
}
