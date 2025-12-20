// <copyright file="BinaryRowDecoder.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Optimizations;

using SharpCoreDB.DataStructures;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Binary row decoder for StreamingRowEncoder format.
/// Decodes binary-encoded rows back into Dictionary format or directly into storage.
/// PERFORMANCE: Eliminates intermediate Dictionary allocation when paired with direct storage writes.
/// </summary>
public sealed class BinaryRowDecoder
{
    private readonly List<string> columns;
    private readonly List<DataType> columnTypes;

    /// <summary>
    /// Initializes a new binary row decoder.
    /// </summary>
    /// <param name="columns">Column names.</param>
    /// <param name="columnTypes">Column data types.</param>
    public BinaryRowDecoder(List<string> columns, List<DataType> columnTypes)
    {
        this.columns = columns ?? throw new ArgumentNullException(nameof(columns));
        this.columnTypes = columnTypes ?? throw new ArgumentNullException(nameof(columnTypes));

        if (columns.Count != columnTypes.Count)
            throw new ArgumentException("Column count must match column type count");
    }

    /// <summary>
    /// Decodes all rows from the binary buffer into Dictionary format.
    /// Use this for compatibility with existing InsertBatch() implementations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<Dictionary<string, object>> DecodeRows(ReadOnlySpan<byte> encodedData, int rowCount)
    {
        var rows = new List<Dictionary<string, object>>(rowCount);
        int position = 0;

        for (int i = 0; i < rowCount; i++)
        {
            if (position >= encodedData.Length)
                throw new InvalidOperationException($"Unexpected end of data at row {i}");

            // Read row size header (4 bytes)
            int rowSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                encodedData[position..]);
            position += 4;

            if (position + rowSize > encodedData.Length)
                throw new InvalidOperationException($"Row {i} size {rowSize} exceeds buffer bounds");

            // Decode row data
            var row = DecodeRow(encodedData.Slice(position, rowSize));
            rows.Add(row);

            position += rowSize;
        }

        return rows;
    }

    /// <summary>
    /// Decodes a single row from binary data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private Dictionary<string, object> DecodeRow(ReadOnlySpan<byte> rowData)
    {
        var row = new Dictionary<string, object>(columns.Count);
        int position = 0;

        for (int i = 0; i < columns.Count; i++)
        {
            var colName = columns[i];
            var colType = columnTypes[i];

            // Read NULL marker (1 byte)
            bool isNull = rowData[position] == 0;
            position++;

            if (isNull)
            {
                row[colName] = DBNull.Value;
                continue;
            }

            // Decode value based on type
            object value = DecodeValue(rowData[position..], colType, out int bytesRead);
            row[colName] = value;
            position += bytesRead;
        }

        return row;
    }

    /// <summary>
    /// Decodes a single value from binary data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static object DecodeValue(
        ReadOnlySpan<byte> data,
        DataType type,
        out int bytesRead)
    {
        bytesRead = 0;

        switch (type)
        {
            case DataType.Integer:
                bytesRead = 4;
                return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data);

            case DataType.Long:
                bytesRead = 8;
                return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(data);

            case DataType.Real:
                bytesRead = 8;
                return System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(data);

            case DataType.Boolean:
                bytesRead = 1;
                return data[0] != 0;

            case DataType.DateTime:
                bytesRead = 8;
                long ticks = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(data);
                return DateTime.FromBinary(ticks);

            case DataType.Decimal:
                bytesRead = 16;
                Span<int> bits = stackalloc int[4];
                for (int i = 0; i < 4; i++)
                {
                    bits[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                        data[(i * 4)..]);
                }
                return new decimal(bits);

            case DataType.String:
                {
                    int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data);
                    bytesRead = 4 + length;
                    return System.Text.Encoding.UTF8.GetString(data.Slice(4, length));
                }

            case DataType.Blob:
                {
                    int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data);
                    bytesRead = 4 + length;
                    return data.Slice(4, length).ToArray();
                }

            case DataType.Guid:
                bytesRead = 16;
                return new Guid(data[..16]);

            case DataType.Ulid:
                {
                    int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data);
                    bytesRead = 4 + length;
                    string ulidStr = System.Text.Encoding.UTF8.GetString(data.Slice(4, length));
                    return new Ulid { Value = ulidStr };
                }

            default:
                throw new NotSupportedException($"Type {type} not supported");
        }
    }

    /// <summary>
    /// ✅ ADVANCED: Decodes rows directly into byte arrays for storage without Dictionary materialization.
    /// This is the zero-allocation path for maximum performance.
    /// Returns array of (rowData, length) tuples ready for direct storage write.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public (byte[] Data, int Length)[] DecodeRowsToByteArrays(
        ReadOnlySpan<byte> encodedData,
        int rowCount,
        Func<Dictionary<string, object>, byte[]> rowSerializer)
    {
        var results = new (byte[], int)[rowCount];
        int position = 0;

        for (int i = 0; i < rowCount; i++)
        {
            if (position >= encodedData.Length)
                throw new InvalidOperationException($"Unexpected end of data at row {i}");

            // Read row size header
            int rowSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                encodedData[position..]);
            position += 4;

            // Decode row
            var row = DecodeRow(encodedData.Slice(position, rowSize));
            
            // Serialize to storage format
            byte[] serialized = rowSerializer(row);
            results[i] = (serialized, serialized.Length);

            position += rowSize;
        }

        return results;
    }
}

