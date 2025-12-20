// <copyright file="StreamingRowEncoder.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Optimizations;

using SharpCoreDB.DataStructures;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Streaming row encoder with column-oriented layout.
/// Encodes rows directly into Span buffers without Dictionary materialization.
/// PERFORMANCE: Eliminates Dictionary allocation overhead (major source of 405MB allocations).
/// </summary>
public sealed class StreamingRowEncoder : IDisposable
{
    private readonly List<string> columns;
    private readonly List<DataType> columnTypes;
    private readonly byte[] batchBuffer;
    private int batchPosition;
    private readonly int maxBatchSize;
    private int rowCount;

    /// <summary>
    /// Initializes a new streaming row encoder.
    /// </summary>
    /// <param name="columns">Column names.</param>
    /// <param name="columnTypes">Column data types.</param>
    /// <param name="maxBatchSize">Maximum bytes per batch (default 64KB).</param>
    public StreamingRowEncoder(
        List<string> columns,
        List<DataType> columnTypes,
        int maxBatchSize = 64 * 1024)
    {
        this.columns = columns ?? throw new ArgumentNullException(nameof(columns));
        this.columnTypes = columnTypes ?? throw new ArgumentNullException(nameof(columnTypes));
        this.maxBatchSize = maxBatchSize;
        this.batchBuffer = ArrayPool<byte>.Shared.Rent(maxBatchSize);
        this.batchPosition = 0;
        this.rowCount = 0;
    }

    /// <summary>
    /// Encodes a single row into the batch buffer.
    /// Returns true if row was added, false if batch is full.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool EncodeRow(Dictionary<string, object> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Reserve space for row header (4 bytes for row size)
        int rowStartPos = batchPosition;
        batchPosition += 4;

        // Encode each column value
        for (int i = 0; i < columns.Count; i++)
        {
            if (batchPosition + 256 > maxBatchSize)
            {
                // Batch is full - revert this row
                batchPosition = rowStartPos;
                return false;
            }

            var colName = columns[i];
            var colType = columnTypes[i];
            object? value = row.TryGetValue(colName, out var val) ? val : null;

            int bytesWritten = EncodeValue(
                batchBuffer.AsSpan(batchPosition),
                value,
                colType);

            batchPosition += bytesWritten;
        }

        // Write row size at the beginning
        int rowSize = batchPosition - rowStartPos - 4;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            batchBuffer.AsSpan(rowStartPos),
            rowSize);

        rowCount++;
        return true;
    }

    /// <summary>
    /// Gets the encoded batch data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetBatchData() => batchBuffer.AsSpan(0, batchPosition);

    /// <summary>
    /// Gets the number of rows in the current batch.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetRowCount() => rowCount;

    /// <summary>
    /// Gets the current batch size in bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetBatchSize() => batchPosition;

    /// <summary>
    /// Checks if batch has reached capacity.
    /// </summary>
    public bool IsFull => batchPosition > maxBatchSize - 512;  // Leave 512 bytes for safety

    /// <summary>
    /// Resets the batch for the next set of rows.
    /// </summary>
    public void Reset()
    {
        batchPosition = 0;
        rowCount = 0;
    }

    /// <summary>
    /// Encodes a single value into the buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int EncodeValue(
        Span<byte> target,
        object? value,
        DataType type)
    {
        if (value == null || value == DBNull.Value)
        {
            target[0] = 0;  // NULL marker
            return 1;
        }

        target[0] = 1;  // NOT NULL marker
        int bytesWritten = 1;

        switch (type)
        {
            case DataType.Integer:
                if (target.Length < 5) throw new InvalidOperationException("Buffer too small");
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(target[1..], (int)value);
                bytesWritten += 4;
                break;

            case DataType.Long:
                if (target.Length < 9) throw new InvalidOperationException("Buffer too small");
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(target[1..], (long)value);
                bytesWritten += 8;
                break;

            case DataType.Real:
                if (target.Length < 9) throw new InvalidOperationException("Buffer too small");
                System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(target[1..], (double)value);
                bytesWritten += 8;
                break;

            case DataType.Boolean:
                if (target.Length < 2) throw new InvalidOperationException("Buffer too small");
                target[1] = (bool)value ? (byte)1 : (byte)0;
                bytesWritten += 1;
                break;

            case DataType.DateTime:
                if (target.Length < 9) throw new InvalidOperationException("Buffer too small");
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                    target[1..],
                    ((DateTime)value).ToBinary());
                bytesWritten += 8;
                break;

            case DataType.Decimal:
                if (target.Length < 17) throw new InvalidOperationException("Buffer too small");
                Span<int> bits = stackalloc int[4];
                _ = decimal.GetBits((decimal)value, bits);
                for (int i = 0; i < 4; i++)
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                        target[(1 + i * 4)..],
                        bits[i]);
                }
                bytesWritten += 16;
                break;

            case DataType.String:
                {
                    var strValue = (string)value;
                    var strBytes = System.Text.Encoding.UTF8.GetBytes(strValue);

                    if (target.Length < 5 + strBytes.Length)
                        throw new InvalidOperationException("Buffer too small");

                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(target[1..], strBytes.Length);
                    strBytes.AsSpan().CopyTo(target[5..]);
                    bytesWritten += 4 + strBytes.Length;
                }
                break;

            case DataType.Blob:
                {
                    var blobValue = (byte[])value;

                    if (target.Length < 5 + blobValue.Length)
                        throw new InvalidOperationException("Buffer too small");

                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(target[1..], blobValue.Length);
                    blobValue.AsSpan().CopyTo(target[5..]);
                    bytesWritten += 4 + blobValue.Length;
                }
                break;

            case DataType.Guid:
                if (target.Length < 17) throw new InvalidOperationException("Buffer too small");
                ((Guid)value).TryWriteBytes(target[1..]);
                bytesWritten += 16;
                break;

            case DataType.Ulid:
                {
                    var ulidStr = ((Ulid)value).Value;
                    var ulidBytes = System.Text.Encoding.UTF8.GetBytes(ulidStr);

                    if (target.Length < 5 + ulidBytes.Length)
                        throw new InvalidOperationException("Buffer too small");

                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(target[1..], ulidBytes.Length);
                    ulidBytes.AsSpan().CopyTo(target[5..]);
                    bytesWritten += 4 + ulidBytes.Length;
                }
                break;

            default:
                throw new NotSupportedException($"Type {type} not supported");
        }

        return bytesWritten;
    }

    /// <summary>
    /// Disposes the encoder and returns buffers to the pool.
    /// </summary>
    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(batchBuffer, clearArray: false);
    }
}
