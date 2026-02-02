// <copyright file="ColumnCodec.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Columnar;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Encodes and decodes columnar data with support for various compression formats.
/// C# 14: Primary constructors, file-scoped namespaces, async patterns.
/// 
/// ✅ SCDB Phase 7: Advanced Query Optimization
/// 
/// Responsibilities:
/// - Column serialization (write to binary format)
/// - Column deserialization (read from binary format)
/// - Encoding/decoding with selected compression
/// - Null bitmap handling
/// - Format validation
/// 
/// Binary format:
/// [Header: 64 bytes]
/// [Column count: 4 bytes]
/// [Row count: 4 bytes]
/// [Column metadata: variable]
/// [Null bitmap: variable]
/// [Encoded data: variable]
/// </summary>
public sealed class ColumnCodec(ColumnFormat format)
{
    private readonly ColumnFormat _format = format ?? throw new ArgumentNullException(nameof(format));

    /// <summary>Encodes column data to binary format.</summary>
    public byte[] EncodeColumn(string columnName, object?[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(values);

        var column = _format.Columns.Find(c => c.ColumnName == columnName)
            ?? throw new InvalidOperationException($"Column not found: {columnName}");

        if (values.Length != column.ValueCount)
            throw new InvalidOperationException("Value count mismatch");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        // Write column header
        writer.Write((byte)column.DataType);
        writer.Write((byte)column.Encoding);
        writer.Write(column.ValueCount);
        writer.Write(column.NullCount);

        // Write null bitmap
        var nullBitmap = new ColumnFormat.NullBitmap(column.ValueCount);
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null || values[i] is DBNull)
                nullBitmap.SetNull(i);
        }
        var bitmapBytes = nullBitmap.GetBytes();
        writer.Write(bitmapBytes.Length);
        writer.Write(bitmapBytes);

        // Encode data based on type and encoding
        EncodeData(writer, column, values);

        return stream.ToArray();
    }

    /// <summary>Decodes column data from binary format.</summary>
    public object?[] DecodeColumn(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        // Read header
        var dataType = (ColumnFormat.ColumnType)reader.ReadByte();
        var encoding = (ColumnFormat.ColumnEncoding)reader.ReadByte();
        var valueCount = reader.ReadInt32();
        var nullCount = reader.ReadInt32();

        // Read null bitmap
        var bitmapSize = reader.ReadInt32();
        var bitmapBytes = reader.ReadBytes(bitmapSize);
        var nullBitmap = new ColumnFormat.NullBitmap(valueCount);
        for (int i = 0; i < bitmapBytes.Length; i++)
        {
            var byteVal = bitmapBytes[i];
            for (int b = 0; b < 8; b++)
            {
                if ((byteVal & (1 << b)) != 0)
                    nullBitmap.SetNull((i * 8) + b);
            }
        }

        // Decode data
        var values = new object?[valueCount];
        DecodeData(reader, dataType, encoding, values, nullBitmap);

        return values;
    }

    /// <summary>Encodes data based on type and encoding scheme.</summary>
    private static void EncodeData(BinaryWriter writer, 
                                   ColumnFormat.ColumnMetadata column,
                                   object?[] values)
    {
        switch (column.DataType)
        {
            case ColumnFormat.ColumnType.Int32:
                EncodeInt32(writer, column, values);
                break;
            
            case ColumnFormat.ColumnType.Int64:
                EncodeInt64(writer, column, values);
                break;
            
            case ColumnFormat.ColumnType.Float64:
                EncodeDouble(writer, column, values);
                break;
            
            case ColumnFormat.ColumnType.String:
                EncodeString(writer, column, values);
                break;
            
            default:
                EncodeRaw(writer, column, values);
                break;
        }
    }

    /// <summary>Decodes data based on type and encoding scheme.</summary>
    private static void DecodeData(BinaryReader reader,
                                   ColumnFormat.ColumnType dataType,
                                   ColumnFormat.ColumnEncoding encoding,
                                   object?[] values,
                                   ColumnFormat.NullBitmap nullBitmap)
    {
        switch (dataType)
        {
            case ColumnFormat.ColumnType.Int32:
                DecodeInt32(reader, encoding, values, nullBitmap);
                break;
            
            case ColumnFormat.ColumnType.Int64:
                DecodeInt64(reader, encoding, values, nullBitmap);
                break;
            
            case ColumnFormat.ColumnType.Float64:
                DecodeDouble(reader, encoding, values, nullBitmap);
                break;
            
            case ColumnFormat.ColumnType.String:
                DecodeString(reader, encoding, values, nullBitmap);
                break;
            
            default:
                DecodeRaw(reader, values, nullBitmap);
                break;
        }
    }

    private static void EncodeInt32(BinaryWriter writer,
                                    ColumnFormat.ColumnMetadata column,
                                    object?[] values)
    {
        switch (column.Encoding)
        {
            case ColumnFormat.ColumnEncoding.Delta:
                EncodeDeltaInt32(writer, values);
                break;
            
            case ColumnFormat.ColumnEncoding.RunLength:
                EncodeRLEInt32(writer, values);
                break;
            
            default:
                EncodeRawInt32(writer, values);
                break;
        }
    }

    private static void EncodeInt64(BinaryWriter writer,
                                    ColumnFormat.ColumnMetadata column,
                                    object?[] values)
    {
        switch (column.Encoding)
        {
            case ColumnFormat.ColumnEncoding.Delta:
                EncodeDeltaInt64(writer, values);
                break;
            
            case ColumnFormat.ColumnEncoding.RunLength:
                EncodeRLEInt64(writer, values);
                break;
            
            default:
                EncodeRawInt64(writer, values);
                break;
        }
    }

    private static void EncodeDouble(BinaryWriter writer,
                                     ColumnFormat.ColumnMetadata column,
                                     object?[] values)
    {
        foreach (var value in values)
        {
            if (value is double d)
                writer.Write(d);
            else if (value == null || value is DBNull)
                writer.Write(double.NaN);
            else
                writer.Write(Convert.ToDouble(value));
        }
    }

    private static void EncodeString(BinaryWriter writer,
                                     ColumnFormat.ColumnMetadata column,
                                     object?[] values)
    {
        switch (column.Encoding)
        {
            case ColumnFormat.ColumnEncoding.Dictionary:
                EncodeDictionaryString(writer, values);
                break;
            
            default:
                EncodeRawString(writer, values);
                break;
        }
    }

    private static void EncodeDeltaInt32(BinaryWriter writer, object?[] values)
    {
        if (values.Length == 0)
            return;

        var baseValue = values[0] is int baseInt ? baseInt : 0;
        writer.Write(baseValue);

        for (int idx = 1; idx < values.Length; idx++)
        {
            var prev = values[idx - 1] is int prevInt ? prevInt : 0;
            var curr = values[idx] is int currInt ? currInt : 0;
            var delta = curr - prev;
            writer.Write(delta);
        }
    }

    private static void EncodeDeltaInt64(BinaryWriter writer, object?[] values)
    {
        if (values.Length == 0)
            return;

        var baseValue = values[0] is long baseLong ? baseLong : 0L;
        writer.Write(baseValue);

        for (int idx = 1; idx < values.Length; idx++)
        {
            var prev = values[idx - 1] is long prevLong ? prevLong : 0L;
            var curr = values[idx] is long currLong ? currLong : 0L;
            var delta = curr - prev;
            writer.Write(delta);
        }
    }

    private static void EncodeRLEInt32(BinaryWriter writer, object?[] values)
    {
        if (values.Length == 0)
            return;

        var current = values[0] is int baseInt ? baseInt : 0;
        int count = 1;

        for (int idx = 1; idx < values.Length; idx++)
        {
            var next = values[idx] is int nextInt ? nextInt : 0;
            if (next == current)
            {
                count++;
            }
            else
            {
                writer.Write(current);
                writer.Write(count);
                current = next;
                count = 1;
            }
        }

        writer.Write(current);
        writer.Write(count);
    }

    private static void EncodeRLEInt64(BinaryWriter writer, object?[] values)
    {
        if (values.Length == 0)
            return;

        var current = values[0] is long baseLong ? baseLong : 0L;
        int count = 1;

        for (int idx = 1; idx < values.Length; idx++)
        {
            var next = values[idx] is long nextLong ? nextLong : 0L;
            if (next == current)
            {
                count++;
            }
            else
            {
                writer.Write(current);
                writer.Write(count);
                current = next;
                count = 1;
            }
        }

        writer.Write(current);
        writer.Write(count);
    }

    private static void EncodeRawInt32(BinaryWriter writer, object?[] values)
    {
        foreach (var value in values)
        {
            if (value is int i)
                writer.Write(i);
            else if (value == null || value is DBNull)
                writer.Write(int.MinValue);
            else
                writer.Write(Convert.ToInt32(value));
        }
    }

    private static void EncodeRawInt64(BinaryWriter writer, object?[] values)
    {
        foreach (var value in values)
        {
            if (value is long l)
                writer.Write(l);
            else if (value == null || value is DBNull)
                writer.Write(long.MinValue);
            else
                writer.Write(Convert.ToInt64(value));
        }
    }

    private static void EncodeDictionaryString(BinaryWriter writer, object?[] values)
    {
        var dict = new ColumnFormat.StringDictionary();
        var indices = new int[values.Length];

        // Build dictionary
        for (int i = 0; i < values.Length; i++)
        {
            var str = values[i] as string ?? string.Empty;
            indices[i] = dict.GetOrAddIndex(str);
        }

        // Write dictionary
        writer.Write(dict.Count);
        foreach (var entry in dict.Entries)
        {
            writer.Write(entry);
        }

        // Write indices
        foreach (var index in indices)
        {
            writer.Write(index);
        }
    }

    private static void EncodeRawString(BinaryWriter writer, object?[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values)
        {
            var str = value as string ?? string.Empty;
            writer.Write(str);
        }
    }

    private static void DecodeInt32(BinaryReader reader,
                                    ColumnFormat.ColumnEncoding encoding,
                                    object?[] values,
                                    ColumnFormat.NullBitmap nullBitmap)
    {
        switch (encoding)
        {
            case ColumnFormat.ColumnEncoding.Delta:
                DecodeDeltaInt32(reader, values, nullBitmap);
                break;
            
            case ColumnFormat.ColumnEncoding.RunLength:
                DecodeRLEInt32(reader, values, nullBitmap);
                break;
            
            default:
                DecodeRawInt32(reader, values, nullBitmap);
                break;
        }
    }

    private static void DecodeDeltaInt32(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        var baseValue = reader.ReadInt32();
        values[0] = nullBitmap.IsNull(0) ? null : baseValue;

        for (int idx = 1; idx < values.Length; idx++)
        {
            var delta = reader.ReadInt32();
            values[idx] = nullBitmap.IsNull(idx) ? null : (int)values[idx - 1]! + delta;
        }
    }

    private static void DecodeRawInt32(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        for (int i = 0; i < values.Length; i++)
        {
            var value = reader.ReadInt32();
            values[i] = nullBitmap.IsNull(i) ? null : (object)value;
        }
    }

    private static void DecodeRLEInt32(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        int index = 0;
        while (index < values.Length)
        {
            var value = reader.ReadInt32();
            var count = reader.ReadInt32();

            for (int i = 0; i < count && index < values.Length; i++, index++)
            {
                values[index] = nullBitmap.IsNull(index) ? null : (object)value;
            }
        }
    }

    private static void DecodeInt64(BinaryReader reader,
                                    ColumnFormat.ColumnEncoding encoding,
                                    object?[] values,
                                    ColumnFormat.NullBitmap nullBitmap)
    {
        switch (encoding)
        {
            case ColumnFormat.ColumnEncoding.Delta:
                DecodeDeltaInt64(reader, values, nullBitmap);
                break;
            
            case ColumnFormat.ColumnEncoding.RunLength:
                DecodeRLEInt64(reader, values, nullBitmap);
                break;
            
            default:
                DecodeRawInt64(reader, values, nullBitmap);
                break;
        }
    }

    private static void DecodeDeltaInt64(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        var baseValue = reader.ReadInt64();
        values[0] = nullBitmap.IsNull(0) ? null : baseValue;

        for (int idx = 1; idx < values.Length; idx++)
        {
            var delta = reader.ReadInt64();
            values[idx] = nullBitmap.IsNull(idx) ? null : (long)values[idx - 1]! + delta;
        }
    }

    private static void DecodeRawInt64(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        for (int i = 0; i < values.Length; i++)
        {
            var value = reader.ReadInt64();
            values[i] = nullBitmap.IsNull(i) ? null : (object)value;
        }
    }

    private static void DecodeRLEInt64(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        int index = 0;
        while (index < values.Length)
        {
            var value = reader.ReadInt64();
            var count = reader.ReadInt32();

            for (int i = 0; i < count && index < values.Length; i++, index++)
            {
                values[index] = nullBitmap.IsNull(index) ? null : (object)value;
            }
        }
    }

    private static void DecodeDouble(BinaryReader reader,
                                    ColumnFormat.ColumnEncoding encoding,
                                    object?[] values,
                                    ColumnFormat.NullBitmap nullBitmap)
    {
        for (int idx = 0; idx < values.Length; idx++)
        {
            var value = reader.ReadDouble();
            values[idx] = nullBitmap.IsNull(idx) || double.IsNaN(value) ? null : (object)value;
        }
    }

    private static void DecodeString(BinaryReader reader,
                                     ColumnFormat.ColumnEncoding encoding,
                                     object?[] values,
                                     ColumnFormat.NullBitmap nullBitmap)
    {
        switch (encoding)
        {
            case ColumnFormat.ColumnEncoding.Dictionary:
                DecodeDictionaryString(reader, values, nullBitmap);
                break;
            
            default:
                DecodeRawString(reader, values, nullBitmap);
                break;
        }
    }

    private static void DecodeDictionaryString(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        var dict = new ColumnFormat.StringDictionary();
        var dictCount = reader.ReadInt32();

        for (int i = 0; i < dictCount; i++)
        {
            dict.GetOrAddIndex(reader.ReadString());
        }

        for (int i = 0; i < values.Length; i++)
        {
            var index = reader.ReadInt32();
            values[i] = nullBitmap.IsNull(i) ? null : (object)dict.GetString(index);
        }
    }

    private static void DecodeRawString(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        var count = reader.ReadInt32();
        for (int i = 0; i < Math.Min(count, values.Length); i++)
        {
            var value = reader.ReadString();
            values[i] = nullBitmap.IsNull(i) ? null : (object)value;
        }
    }

    private static void DecodeRaw(BinaryReader reader, object?[] values, ColumnFormat.NullBitmap nullBitmap)
    {
        // Fallback: store raw bytes
        for (int i = 0; i < values.Length; i++)
        {
            if (!nullBitmap.IsNull(i))
            {
                var length = reader.ReadInt32();
                var bytes = reader.ReadBytes(length);
                values[i] = bytes;
            }
        }
    }

    private static void EncodeRaw(BinaryWriter writer,
                                  ColumnFormat.ColumnMetadata column,
                                  object?[] values)
    {
        foreach (var value in values)
        {
            if (value == null || value is DBNull)
            {
                writer.Write(0);
            }
            else
            {
                var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
    }
}
