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
            if (buffer.Length < 1)
                throw new InvalidOperationException(
                    $"Buffer too small to write null flag: need 1 byte, have {buffer.Length}");
            buffer[0] = 0; // null flag
            return 1;
        }
        
        if (buffer.Length < 1)
            throw new InvalidOperationException(
                $"Buffer too small to write null flag: need 1 byte, have {buffer.Length}");
        
        buffer[0] = 1; // not null
        int bytesWritten = 1;
        
        switch (type)
        {
            case DataType.Integer:
                if (buffer.Length < 5) // 1 byte null flag + 4 bytes int
                    throw new InvalidOperationException(
                        $"Buffer too small for Integer write: need 5 bytes, have {buffer.Length}");
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), (int)value);
                bytesWritten += 4;
                break;
                
            case DataType.Long:
                if (buffer.Length < 9) // 1 byte null flag + 8 bytes long
                    throw new InvalidOperationException(
                        $"Buffer too small for Long write: need 9 bytes, have {buffer.Length}");
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(bytesWritten), (long)value);
                bytesWritten += 8;
                break;
                
            case DataType.Real:
                if (buffer.Length < 9) // 1 byte null flag + 8 bytes double
                    throw new InvalidOperationException(
                        $"Buffer too small for Real write: need 9 bytes, have {buffer.Length}");
                System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(buffer.Slice(bytesWritten), (double)value);
                bytesWritten += 8;
                break;
                
            case DataType.Boolean:
                if (buffer.Length < 2) // 1 byte null flag + 1 byte bool
                    throw new InvalidOperationException(
                        $"Buffer too small for Boolean write: need 2 bytes, have {buffer.Length}");
                buffer[bytesWritten] = (bool)value ? (byte)1 : (byte)0;
                bytesWritten += 1;
                break;
                
            case DataType.DateTime:
                if (buffer.Length < 9) // 1 byte null flag + 8 bytes long
                    throw new InvalidOperationException(
                        $"Buffer too small for DateTime write: need 9 bytes, have {buffer.Length}");
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(bytesWritten), ((DateTime)value).ToBinary());
                bytesWritten += 8;
                break;
                
            case DataType.Decimal:
                if (buffer.Length < 17) // 1 byte null flag + 16 bytes (4 ints)
                    throw new InvalidOperationException(
                        $"Buffer too small for Decimal write: need 17 bytes, have {buffer.Length}");
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
                if (ulidBytes.Length > 100)
                    throw new InvalidOperationException(
                        $"Ulid too large: {ulidBytes.Length} bytes (max 100)");
                if (buffer.Length < 5 + ulidBytes.Length) // 1 byte null + 4 bytes length + data
                    throw new InvalidOperationException(
                        $"Buffer too small for Ulid write: need {5 + ulidBytes.Length} bytes, have {buffer.Length}");
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), ulidBytes.Length);
                bytesWritten += 4;
                ulidBytes.AsSpan().CopyTo(buffer.Slice(bytesWritten));
                bytesWritten += ulidBytes.Length;
                break;
                
            case DataType.Guid:
                if (buffer.Length < 17) // 1 byte null flag + 16 bytes guid
                    throw new InvalidOperationException(
                        $"Buffer too small for Guid write: need 17 bytes, have {buffer.Length}");
                ((Guid)value).TryWriteBytes(buffer.Slice(bytesWritten));
                bytesWritten += 16;
                break;
                
            case DataType.Blob:
                var blobBytes = (byte[])value;
                if (blobBytes.Length > 1024 * 1024 * 100) // Max 100 MB
                    throw new InvalidOperationException(
                        $"Blob too large: {blobBytes.Length} bytes (max {1024 * 1024 * 100})");
                if (buffer.Length < 5 + blobBytes.Length) // 1 byte null + 4 bytes length + data
                    throw new InvalidOperationException(
                        $"Buffer too small for Blob write: need {5 + blobBytes.Length} bytes, have {buffer.Length}");
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), blobBytes.Length);
                bytesWritten += 4;
                blobBytes.AsSpan().CopyTo(buffer.Slice(bytesWritten));
                bytesWritten += blobBytes.Length;
                break;
                
            case DataType.String:
                var strBytes = System.Text.Encoding.UTF8.GetBytes((string)value);
                if (strBytes.Length > 1024 * 1024 * 100) // Max 100 MB
                    throw new InvalidOperationException(
                        $"String too large: {strBytes.Length} bytes (max {1024 * 1024 * 100})");
                if (buffer.Length < 5 + strBytes.Length) // 1 byte null + 4 bytes length + data
                    throw new InvalidOperationException(
                        $"Buffer too small for String write: need {5 + strBytes.Length} bytes, have {buffer.Length}");
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bytesWritten), strBytes.Length);
                bytesWritten += 4;
                strBytes.AsSpan().CopyTo(buffer.Slice(bytesWritten));
                bytesWritten += strBytes.Length;
                break;
                
            default:
                var defaultBytes = System.Text.Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty);
                if (defaultBytes.Length > 1024 * 1024 * 100) // Max 100 MB
                    throw new InvalidOperationException(
                        $"Default type value too large: {defaultBytes.Length} bytes (max {1024 * 1024 * 100})");
                if (buffer.Length < 5 + defaultBytes.Length)
                    throw new InvalidOperationException(
                        $"Buffer too small for default type write: need {5 + defaultBytes.Length} bytes, have {buffer.Length}");
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
        
        // Validate minimum buffer size for null flag
        if (buffer.Length < 1)
        {
            throw new InvalidOperationException(
                $"Buffer too small to read null flag: need 1 byte, have {buffer.Length}");
        }
        
        var isNull = buffer[0];
        if (isNull == 0) return DBNull.Value;
        
        switch (type)
        {
            case DataType.Integer:
                if (buffer.Length < 5) // 1 byte null flag + 4 bytes int
                    throw new InvalidOperationException(
                        $"Buffer too small for Integer: need 5 bytes, have {buffer.Length}");
                bytesRead += 4;
                return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                
            case DataType.Long:
                if (buffer.Length < 9) // 1 byte null flag + 8 bytes long
                    throw new InvalidOperationException(
                        $"Buffer too small for Long: need 9 bytes, have {buffer.Length}");
                bytesRead += 8;
                return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(1));
                
            case DataType.Real:
                if (buffer.Length < 9) // 1 byte null flag + 8 bytes double
                    throw new InvalidOperationException(
                        $"Buffer too small for Real: need 9 bytes, have {buffer.Length}");
                bytesRead += 8;
                return System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(buffer.Slice(1));
                
            case DataType.Boolean:
                if (buffer.Length < 2) // 1 byte null flag + 1 byte bool
                    throw new InvalidOperationException(
                        $"Buffer too small for Boolean: need 2 bytes, have {buffer.Length}");
                bytesRead += 1;
                return buffer[1] != 0;
                
            case DataType.DateTime:
                if (buffer.Length < 9) // 1 byte null flag + 8 bytes long
                    throw new InvalidOperationException(
                        $"Buffer too small for DateTime: need 9 bytes, have {buffer.Length}");
                bytesRead += 8;
                return DateTime.FromBinary(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(1)));
                
            case DataType.Decimal:
                if (buffer.Length < 17) // 1 byte null flag + 16 bytes (4 ints)
                    throw new InvalidOperationException(
                        $"Buffer too small for Decimal: need 17 bytes, have {buffer.Length}");
                Span<int> bits = stackalloc int[4];
                for (int i = 0; i < 4; i++)
                {
                    bits[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1 + i * 4));
                }
                bytesRead += 16;
                return new decimal(bits);
                
            case DataType.Ulid:
                if (buffer.Length < 5) // 1 byte null flag + 4 bytes length
                    throw new InvalidOperationException(
                        $"Buffer too small for Ulid length: need 5 bytes, have {buffer.Length}");
                int ulidLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                if (ulidLen < 0 || ulidLen > 100) // ULID should be ~26 chars max
                    throw new InvalidOperationException(
                        $"Invalid Ulid length: {ulidLen} (expected 0-100)");
                if (buffer.Length < 5 + ulidLen) // 1 byte null + 4 bytes length + data
                    throw new InvalidOperationException(
                        $"Buffer too small for Ulid data: need {5 + ulidLen} bytes, have {buffer.Length}");
                bytesRead += 4 + ulidLen;
                var ulidStr = System.Text.Encoding.UTF8.GetString(buffer.Slice(5, ulidLen));
                return new Ulid(ulidStr);
                
            case DataType.Guid:
                if (buffer.Length < 17) // 1 byte null flag + 16 bytes guid
                    throw new InvalidOperationException(
                        $"Buffer too small for Guid: need 17 bytes, have {buffer.Length}");
                bytesRead += 16;
                return new Guid(buffer.Slice(1, 16));
                
            case DataType.Blob:
                if (buffer.Length < 5) // 1 byte null flag + 4 bytes length
                    throw new InvalidOperationException(
                        $"Buffer too small for Blob length: need 5 bytes, have {buffer.Length}");
                int blobLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                if (blobLen < 0 || blobLen > 1024 * 1024 * 100) // Max 100 MB blob
                    throw new InvalidOperationException(
                        $"Invalid Blob length: {blobLen} (expected 0-{1024 * 1024 * 100})");
                if (buffer.Length < 5 + blobLen) // 1 byte null + 4 bytes length + data
                    throw new InvalidOperationException(
                        $"Buffer too small for Blob data: need {5 + blobLen} bytes, have {buffer.Length}");
                bytesRead += 4 + blobLen;
                return buffer.Slice(5, blobLen).ToArray();
                
            case DataType.String:
                if (buffer.Length < 5) // 1 byte null flag + 4 bytes length
                    throw new InvalidOperationException(
                        $"Buffer too small for String length: need 5 bytes, have {buffer.Length}");
                int strLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                
                // FIXED: Add validation for suspicious string lengths
                const int MaxStringSize = 1_000_000_000; // 1 GB max string
                
                if (strLen < 0)
                {
                    // Negative length indicates corruption or misalignment
                    throw new InvalidOperationException(
                        $"Invalid String length: {strLen} (negative - data corruption likely)");
                }
                
                if (strLen == 0)
                {
                    // Empty string is valid
                    bytesRead += 4;
                    return string.Empty;
                }
                
                if (strLen > MaxStringSize)
                {
                    throw new InvalidOperationException(
                        $"Invalid String length: {strLen} (expected 0-{MaxStringSize})");
                }
                
                if (buffer.Length < 5 + strLen) // 1 byte null + 4 bytes length + data
                    throw new InvalidOperationException(
                        $"Buffer too small for String data: need {5 + strLen} bytes, have {buffer.Length}");
                
                bytesRead += 4 + strLen;
                return System.Text.Encoding.UTF8.GetString(buffer.Slice(5, strLen));
                
            default:
                if (buffer.Length < 5) // 1 byte null flag + 4 bytes length
                    throw new InvalidOperationException(
                        $"Buffer too small for default type length: need 5 bytes, have {buffer.Length}");
                int defaultLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(1));
                if (defaultLen < 0 || defaultLen > 1024 * 1024 * 100) // Max 100 MB
                    throw new InvalidOperationException(
                        $"Invalid default type length: {defaultLen} (expected 0-{1024 * 1024 * 100})");
                if (buffer.Length < 5 + defaultLen)
                    throw new InvalidOperationException(
                        $"Buffer too small for default type data: need {5 + defaultLen} bytes, have {buffer.Length}");
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
        try
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
                    {
                        var len = reader.ReadInt32();
                        if (len < 0 || len > 1024 * 1024 * 100) // Max 100 MB
                            throw new InvalidOperationException(
                                $"Invalid Blob length: {len} (expected 0-{1024 * 1024 * 100})");
                        return reader.ReadBytes(len);
                    }
                default:
                    return reader.ReadString();
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidOperationException(
                $"Unexpected end of stream while reading {type}: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            throw new InvalidOperationException(
                $"Error reading {type}: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    private Dictionary<string, object>? ReadRowAtPosition(long position, bool noEncrypt = false)
    {
        // Read length-prefixed data written by AppendBytes
        var data = this.storage!.ReadBytesFrom(this.DataFile, position);
        if (data == null || data.Length == 0) return null;
        
        var row = new Dictionary<string, object>();
        
        int offset = 0;
        ReadOnlySpan<byte> dataSpan = data.AsSpan();
        
        // ADDED: Debug logging for row structure
        bool debugRow = data.Length < 512; // Only debug small rows to reduce spam
        if (debugRow)
        {
            var hexDump = System.BitConverter.ToString(data);
            Console.WriteLine($"🔍 ReadRowAtPosition({position}): Total buffer size: {dataSpan.Length} bytes");
            Console.WriteLine($"   Hex dump (first bytes): {hexDump.Substring(0, Math.Min(192, hexDump.Length))}");
        }
        
        for (int i = 0; i < this.Columns.Count; i++)
        {
            // Validate we have enough data left to read at least the null flag
            if (offset >= dataSpan.Length)
            {
                Console.WriteLine($"⚠️  Data corruption at position {position}: " +
                    $"Only {i}/{this.Columns.Count} fields could be read. " +
                    $"Buffer size: {dataSpan.Length}, offset: {offset}");
                return null;
            }
            
            // ADDED: Debug logging before reading each field
            if (debugRow)
            {
                var previewBytes = Math.Min(16, dataSpan.Length - offset);
                var preview = System.BitConverter.ToString(data, offset, previewBytes);
                Console.WriteLine($"   Field {i} ({this.Columns[i]}, type {this.ColumnTypes[i]}): offset={offset}, preview={preview}");
            }
            
            try
            {
                var value = ReadTypedValueFromSpan(dataSpan.Slice(offset), this.ColumnTypes[i], out int bytesRead);
                row[this.Columns[i]] = value;
                offset += bytesRead;
                
                // ADDED: Debug logging after reading each field
                if (debugRow)
                {
                    Console.WriteLine($"      ✓ Read {bytesRead} bytes, new offset={offset}, value={value?.ToString() ?? "NULL"}");
                }
            }
            catch (InvalidOperationException ioex) when (ioex.Message.Contains("Buffer"))
            {
                // Buffer size error - provide detailed diagnostic info
                int remainingBytes = dataSpan.Length - offset;
                Console.WriteLine($"❌ Field deserialization error at position {position}:");
                Console.WriteLine($"   Column: '{this.Columns[i]}' (index {i}, type {this.ColumnTypes[i]})");
                Console.WriteLine($"   Buffer state:");
                Console.WriteLine($"     - Total buffer size: {dataSpan.Length} bytes");
                Console.WriteLine($"     - Current offset: {offset} bytes");
                Console.WriteLine($"     - Remaining bytes: {remainingBytes} bytes");
                Console.WriteLine($"   Error: {ioex.Message}");
                
                // Log hex dump of problematic region (up to 32 bytes around error position)
                int dumpStart = Math.Max(0, offset - 4);
                int dumpEnd = Math.Min(dataSpan.Length, offset + 32);
                if (dumpStart < dumpEnd)
                {
                    var hexDump = System.BitConverter.ToString(data, dumpStart, dumpEnd - dumpStart);
                    Console.WriteLine($"   Hex dump (offset {dumpStart}-{dumpEnd}): {hexDump}");
                }
                
                // ADDED: Additional diagnostic - show all previously read fields
                Console.WriteLine($"   Previously read fields:");
                foreach (var kvp in row.Take(i))
                {
                    Console.WriteLine($"     - {kvp.Key} = {kvp.Value?.ToString() ?? "NULL"}");
                }
                
                return null;
            }
            catch (Exception ex)
            {
                // Other deserialization errors
                int remainingBytes = dataSpan.Length - offset;
                Console.WriteLine($"❌ Unexpected field deserialization error at position {position}:");
                Console.WriteLine($"   Column: '{this.Columns[i]}' (index {i}, type {this.ColumnTypes[i]})");
                Console.WriteLine($"   Buffer state:");
                Console.WriteLine($"     - Total buffer size: {dataSpan.Length} bytes");
                Console.WriteLine($"     - Current offset: {offset} bytes");
                Console.WriteLine($"     - Remaining bytes: {remainingBytes} bytes");
                Console.WriteLine($"   Error: {ex.GetType().Name}: {ex.Message}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner error: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                
                // ADDED: Show what we successfully read so far
                Console.WriteLine($"   Fields read so far ({row.Count}):");
                foreach (var kvp in row)
                {
                    Console.WriteLine($"     - {kvp.Key} = {kvp.Value?.ToString() ?? "NULL"}");
                }
                
                return null;
            }
        }
        
        // ADDED: Final validation - ensure we consumed all data
        if (offset < dataSpan.Length)
        {
            int remainingUnread = dataSpan.Length - offset;
            Console.WriteLine($"⚠️  Warning: {remainingUnread} bytes unread after parsing all {this.Columns.Count} fields at position {position}");
        }
        
        return row;
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
        DataType.String => string.Empty,
        DataType.Real => 0.0,
        DataType.Boolean => false,
        DataType.DateTime => DateTime.Now,
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
