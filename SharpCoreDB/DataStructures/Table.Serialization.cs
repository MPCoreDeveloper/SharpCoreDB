namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Buffers;
using SharpCoreDB.Services;

/// <summary>
/// Serialization methods for Table - handles type-safe read/write operations.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Estimates the size needed to serialize a row.
    /// </summary>
    private int EstimateRowSize(Dictionary<string, object> row)
    {
        int size = 0;
        foreach (var col in this.Columns)
        {
            var value = row[col];
            var type = this.ColumnTypes[this.Columns.IndexOf(col)];
            
            size += 1; // null flag
            if (value == null || value == DBNull.Value) continue;
            
            size += type switch
            {
                DataType.Integer => 4,
                DataType.Long => 8,
                DataType.Real => 8,
                DataType.Boolean => 1,
                DataType.DateTime => 8,
                DataType.Decimal => 16,
                DataType.Ulid => 26, // ULID string representation
                DataType.Guid => 16,
                DataType.String => 4 + System.Text.Encoding.UTF8.GetByteCount((string)value),
                DataType.Blob => 4 + ((byte[])value).Length,
                _ => 4 + 50 // default estimate
            };
        }
        return Math.Max(size, 256); // minimum buffer
    }

    /// <summary>
    /// Writes a typed value to a Span using BinaryPrimitives for zero-allocation serialization.
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int WriteTypedValueToSpan(Span<byte> buffer, object value, DataType type)
    {
        if (value == DBNull.Value || value == null)
        {
            buffer[0] = 0; // null flag
            return 1;
        }
        
        buffer[0] = 1; // not null
        int bytesWritten = 1;
        
        switch (type)
        {
            case DataType.Integer:
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), (int)value);
                bytesWritten += 4;
                break;
                
            case DataType.Long:
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(bytesWritten), (long)value);
                bytesWritten += 8;
                break;
                
            case DataType.Real:
                System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(buffer.Slice(bytesWritten), (double)value);
                bytesWritten += 8;
                break;
                
            case DataType.Boolean:
                buffer[bytesWritten] = (bool)value ? (byte)1 : (byte)0;
                bytesWritten += 1;
                break;
                
            case DataType.DateTime:
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(bytesWritten), ((DateTime)value).ToBinary());
                bytesWritten += 8;
                break;
                
            case DataType.Decimal:
                // Decimal requires special handling - use Span-based serialization
                Span<int> bits = stackalloc int[4];
                _ = decimal.GetBits((decimal)value, bits);
                for (int i = 0; i < 4; i++)
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), bits[i]);
                    bytesWritten += 4;
                }
                break;
                
            case DataType.Ulid:
                var ulidStr = ((Ulid)value).Value;
                var ulidBytes = System.Text.Encoding.UTF8.GetBytes(ulidStr);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), ulidBytes.Length);
                bytesWritten += 4;
                ulidBytes.AsSpan().CopyTo(buffer.Slice(bytesWritten));
                bytesWritten += ulidBytes.Length;
                break;
                
            case DataType.Guid:
                ((Guid)value).TryWriteBytes(buffer.Slice(bytesWritten));
                bytesWritten += 16;
                break;
                
            case DataType.Blob:
                var blobBytes = (byte[])value;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), blobBytes.Length);
                bytesWritten += 4;
                blobBytes.AsSpan().CopyTo(buffer.Slice(bytesWritten));
                bytesWritten += blobBytes.Length;
                break;
                
            case DataType.String:
                var strBytes = System.Text.Encoding.UTF8.GetBytes((string)value);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), strBytes.Length);
                bytesWritten += 4;
                strBytes.AsSpan().CopyTo(buffer.Slice(bytesWritten));
                bytesWritten += strBytes.Length;
                break;
                
            default:
                var defaultBytes = System.Text.Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), defaultBytes.Length);
                bytesWritten += 4;
                defaultBytes.AsSpan().CopyTo(buffer.Slice(bytesWritten));
                bytesWritten += defaultBytes.Length;
                break;
        }
        
        return bytesWritten;
    }

    /// <summary>
    /// Reads a typed value from a ReadOnlySpan using BinaryPrimitives for zero-allocation deserialization.
    /// </summary>
    /// <param name="buffer">The buffer to read from.</param>
    /// <param name="type">The data type.</param>
    /// <param name="bytesRead">Output: number of bytes consumed.</param>
    /// <returns>The deserialized value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private object ReadTypedValueFromSpan(ReadOnlySpan<byte> buffer, DataType type, out int bytesRead)
    {
        bytesRead = 1;
        var isNull = buffer[0];
        if (isNull == 0) return DBNull.Value;
        
        switch (type)
        {
            case DataType.Integer:
                bytesRead += 4;
                return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                
            case DataType.Long:
                bytesRead += 8;
                return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(1));
                
            case DataType.Real:
                bytesRead += 8;
                return System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(buffer.Slice(1));
                
            case DataType.Boolean:
                bytesRead += 1;
                return buffer[1] != 0;
                
            case DataType.DateTime:
                bytesRead += 8;
                return DateTime.FromBinary(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(1)));
                
            case DataType.Decimal:
                // Decimal requires special handling
                Span<int> bits = stackalloc int[4];
                for (int i = 0; i < 4; i++)
                {
                    bits[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1 + i * 4));
                }
                bytesRead += 16;
                return new decimal(bits);
                
            case DataType.Ulid:
                int ulidLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                bytesRead += 4 + ulidLen;
                var ulidStr = System.Text.Encoding.UTF8.GetString(buffer.Slice(5, ulidLen));
                return new Ulid(ulidStr);
                
            case DataType.Guid:
                bytesRead += 16;
                return new Guid(buffer.Slice(1, 16));
                
            case DataType.Blob:
                int blobLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                bytesRead += 4 + blobLen;
                return buffer.Slice(5, blobLen).ToArray();
                
            case DataType.String:
                int strLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                bytesRead += 4 + strLen;
                return System.Text.Encoding.UTF8.GetString(buffer.Slice(5, strLen));
                
            default:
                int defaultLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                bytesRead += 4 + defaultLen;
                return System.Text.Encoding.UTF8.GetString(buffer.Slice(5, defaultLen));
        }
    }

    /// <summary>
    /// Legacy write method - kept for backward compatibility.
    /// </summary>
    private static void WriteTypedValue(BinaryWriter writer, object value, DataType type)
    {
        if (value == DBNull.Value || value == null)
        {
            writer.Write((byte)0); // null flag
            return;
        }
        writer.Write((byte)1); // not null
        switch (type)
        {
            case DataType.Integer:
                writer.Write((int)value);
                break;
            case DataType.String:
                writer.Write((string)value);
                break;
            case DataType.Real:
                writer.Write((double)value);
                break;
            case DataType.Boolean:
                writer.Write((bool)value);
                break;
            case DataType.DateTime:
                writer.Write(((DateTime)value).ToBinary());
                break;
            case DataType.Long:
                writer.Write((long)value);
                break;
            case DataType.Decimal:
                writer.Write((decimal)value);
                break;
            case DataType.Ulid:
                writer.Write(((Ulid)value).Value);
                break;
            case DataType.Guid:
                writer.Write(((Guid)value).ToByteArray());
                break;
            case DataType.Blob:
                var bytes = (byte[])value;
                writer.Write(bytes.Length);
                writer.Write(bytes);
                break;
            default:
                writer.Write((string)value);
                break;
        }
    }

    /// <summary>
    /// Legacy read method - kept for backward compatibility.
    /// </summary>
    private static object ReadTypedValue(BinaryReader reader, DataType type)
    {
        var isNull = reader.ReadByte();
        if (isNull == 0) return DBNull.Value;
        switch (type)
        {
            case DataType.Integer:
                return reader.ReadInt32();
            case DataType.String:
                return reader.ReadString();
            case DataType.Real:
                return reader.ReadDouble();
            case DataType.Boolean:
                return reader.ReadBoolean();
            case DataType.DateTime:
                return DateTime.FromBinary(reader.ReadInt64());
            case DataType.Long:
                return reader.ReadInt64();
            case DataType.Decimal:
                return reader.ReadDecimal();
            case DataType.Ulid:
                return new Ulid(reader.ReadString());
            case DataType.Guid:
                return new Guid(reader.ReadBytes(16));
            case DataType.Blob:
                var len = reader.ReadInt32();
                return reader.ReadBytes(len);
            default:
                return reader.ReadString();
        }
    }

    private Dictionary<string, object>? ReadRowAtPosition(long position, bool noEncrypt = false)
    {
        // FIXED: Use ReadBytesFrom which reads the length-prefixed data correctly
        // ReadBytesAt reads raw bytes, but AppendBytes writes [length][data]
        // noEncrypt parameter kept for future use with selective encryption
        var data = this.storage!.ReadBytesFrom(this.DataFile, position);
        if (data == null || data.Length == 0) return null;
        
        var row = new Dictionary<string, object>();
        try
        {
            int offset = 0;
            ReadOnlySpan<byte> dataSpan = data.AsSpan();
            
            for (int i = 0; i < this.Columns.Count; i++)
            {
                var value = ReadTypedValueFromSpan(dataSpan.Slice(offset), this.ColumnTypes[i], out int bytesRead);
                row[this.Columns[i]] = value;
                offset += bytesRead;
                if (offset >= dataSpan.Length) break;
            }
            return row;
        }
        catch { return null; }
    }

    private static object ParseValueForHashLookup(string value, DataType type)
    {
        return type switch
        {
            DataType.Integer => int.TryParse(value, out var i) ? i : value,
            DataType.Long => long.TryParse(value, out var l) ? l : value,
            DataType.Real => double.TryParse(value, out var d) ? d : value,
            DataType.Boolean => bool.TryParse(value, out var b) ? b : value,
            _ => value,
        };
    }

    private static object GenerateAutoValue(DataType type) => type switch
    {
        DataType.Ulid => Ulid.NewUlid(),
        DataType.Guid => Guid.NewGuid(),
        _ => throw new InvalidOperationException($"Auto generation not supported for type {type}"),
    };

    private static object? GetDefaultValue(DataType type) => type switch
    {
        DataType.Integer => 0,
        DataType.String => "",
        DataType.Real => 0.0,
        DataType.Boolean => false,
        DataType.DateTime => DateTime.MinValue,
        DataType.Long => 0L,
        DataType.Decimal => 0m,
        DataType.Ulid => Ulid.NewUlid(),
        DataType.Guid => Guid.NewGuid(),
        _ => null,
    };

    private static bool IsValidType(object value, DataType type)
    {
        if (value == DBNull.Value || value == null) return true;
        return type switch
        {
            DataType.Integer => value is int,
            DataType.String => value is string,
            DataType.Real => value is double or float,
            DataType.Boolean => value is bool,
            DataType.DateTime => value is DateTime,
            DataType.Long => value is long,
            DataType.Decimal => value is decimal,
            DataType.Ulid => value is Ulid,
            DataType.Guid => value is Guid,
            DataType.Blob => value is byte[],
            _ => true,
        };
    }
}
