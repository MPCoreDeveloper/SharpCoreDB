#nullable enable

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a zero-copy view of a single row in a query result.
/// Provides lazy deserialization of column values without allocations during iteration.
/// ✅ PERFORMANCE CRITICAL: This is the primary path for high-performance SELECT operations.
/// 
/// Memory usage: ~20 bytes per row (vs ~200 bytes for Dictionary API)
/// Allocation: Zero during iteration (uses ReadOnlyMemory pointing to raw storage)
/// </summary>
public readonly struct StructRow
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly StructRowSchema? _fixedSchema;
    private readonly VariableLengthSchema? _variableSchema;
    private readonly int _rowOffset;

    // Optional cache for deserialized values (improves performance for repeated access)
    private readonly Dictionary<int, object>? _cache;

    /// <summary>
    /// Initializes a new instance of StructRow with fixed-length schema.
    /// </summary>
    /// <param name="data">The raw byte data containing all rows.</param>
    /// <param name="schema">The schema defining column layout.</param>
    /// <param name="rowOffset">The byte offset to this row within the data.</param>
    /// <param name="enableCaching">Whether to enable caching of deserialized values.</param>
    internal StructRow(ReadOnlyMemory<byte> data, StructRowSchema schema, int rowOffset, bool enableCaching = false)
    {
        _data = data;
        _fixedSchema = schema;
        _variableSchema = null;
        _rowOffset = rowOffset;
        _cache = enableCaching ? new Dictionary<int, object>() : null;
    }

    /// <summary>
    /// Initializes a new instance of StructRow with variable-length schema.
    /// ✅ NEW: Supports variable-length records (strings, blobs, etc.)
    /// </summary>
    /// <param name="data">The raw byte data for this single row (not including length prefix).</param>
    /// <param name="schema">The variable-length schema defining column layout.</param>
    /// <param name="enableCaching">Whether to enable caching of deserialized values.</param>
    internal StructRow(ReadOnlyMemory<byte> data, VariableLengthSchema schema, bool enableCaching = false)
    {
        _data = data;
        _fixedSchema = null;
        _variableSchema = schema;
        _rowOffset = 0; // Data already points to the row
        _cache = enableCaching ? new Dictionary<int, object>() : null;
    }

    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount => _fixedSchema?.ColumnNames.Length ?? _variableSchema?.ColumnCount ?? 0;

    /// <summary>
    /// Gets the column names as an array.
    /// </summary>
    public string[] GetColumnNames()
    {
        if (_fixedSchema.HasValue)
            return _fixedSchema.Value.ColumnNames;
        if (_variableSchema.HasValue)
            return _variableSchema.Value.ColumnNames;
        return Array.Empty<string>();
    }

    /// <summary>
    /// Creates a StructRow from a dictionary (not implemented for zero-copy API).
    /// </summary>
    /// <param name="dict">The dictionary.</param>
    /// <param name="columns">The columns.</param>
    /// <param name="types">The types.</param>
    /// <returns>A StructRow.</returns>
    /// <exception cref="NotImplementedException">Always thrown.</exception>
    public static StructRow FromDictionary(Dictionary<string, object> dict, string[] columns, DataType[] types)
    {
        throw new NotImplementedException("FromDictionary not implemented for zero-copy StructRow");
    }

    /// <summary>
    /// Gets the value of the specified column by index with lazy deserialization.
    /// ✅ PERFORMANCE: Use column index for fastest access (avoids string lookup).
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if columnIndex is invalid.</exception>
    /// <exception cref="InvalidCastException">Thrown if T does not match the column type.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public T GetValue<T>(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));

        // Check cache first (optional optimization)
        if (_cache?.TryGetValue(columnIndex, out var cached) == true)
        {
            return (T)cached;
        }

        T value;

        if (_fixedSchema.HasValue)
        {
            // Fixed-length schema: direct offset access
            var span = _data.Span.Slice(_rowOffset + _fixedSchema.Value.ColumnOffsets[columnIndex]);
            value = DeserializeValue<T>(span, _fixedSchema.Value.ColumnTypes[columnIndex]);
        }
        else if (_variableSchema.HasValue)
        {
            // Variable-length schema: calculate offset by scanning previous columns
            int offset = CalculateColumnOffset(columnIndex);
            var span = _data.Span.Slice(offset);
            value = DeserializeValue<T>(span, _variableSchema.Value.ColumnTypes[columnIndex]);
        }
        else
        {
            throw new InvalidOperationException("No schema defined for this StructRow");
        }

        // Cache result (optional)
        _cache?.Add(columnIndex, value!);

        return value;
    }

    /// <summary>
    /// Gets the value of the specified column by name with lazy deserialization.
    /// ⚠️ PERFORMANCE: Use column index version for faster access.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="columnName">The column name.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentException">Thrown if columnName is not found.</exception>
    /// <exception cref="InvalidCastException">Thrown if T does not match the column type.</exception>
    public T GetValue<T>(string columnName)
    {
        var columnNames = GetColumnNames();
        int index = Array.IndexOf(columnNames, columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' not found", nameof(columnName));
        return GetValue<T>(index);
    }

    /// <summary>
    /// Gets the value of the specified column by index, returning boxed object.
    /// ✅ NEW: Used for Dictionary conversion when backward compatibility is needed.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>The deserialized value as object (may be boxed for primitives).</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public object GetValueBoxed(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));

        // Check cache first
        if (_cache?.TryGetValue(columnIndex, out var cached) == true)
        {
            return cached;
        }

        DataType type;
        ReadOnlySpan<byte> span;

        if (_fixedSchema.HasValue)
        {
            type = _fixedSchema.Value.ColumnTypes[columnIndex];
            span = _data.Span.Slice(_rowOffset + _fixedSchema.Value.ColumnOffsets[columnIndex]);
        }
        else if (_variableSchema.HasValue)
        {
            type = _variableSchema.Value.ColumnTypes[columnIndex];
            int offset = CalculateColumnOffset(columnIndex);
            span = _data.Span.Slice(offset);
        }
        else
        {
            throw new InvalidOperationException("No schema defined for this StructRow");
        }

        object value = DeserializeValueBoxed(span, type);
        _cache?.Add(columnIndex, value);
        return value;
    }

    /// <summary>
    /// Checks if the specified column is null.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>True if the column value is null, otherwise false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if columnIndex is invalid.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsNull(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));

        ReadOnlySpan<byte> span;

        if (_fixedSchema.HasValue)
        {
            span = _data.Span.Slice(_rowOffset + _fixedSchema.Value.ColumnOffsets[columnIndex]);
        }
        else if (_variableSchema.HasValue)
        {
            int offset = CalculateColumnOffset(columnIndex);
            span = _data.Span.Slice(offset);
        }
        else
        {
            throw new InvalidOperationException("No schema defined for this StructRow");
        }

        return span.Length == 0 || span[0] == 0; // Null flag
    }

    /// <summary>
    /// Calculates the byte offset for a column in variable-length records.
    /// ✅ CRITICAL: Must scan previous columns to find offset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int CalculateColumnOffset(int columnIndex)
    {
        if (!_variableSchema.HasValue)
            return 0;

        var schema = _variableSchema.Value;
        var span = _data.Span;
        int offset = 0;

        for (int i = 0; i < columnIndex; i++)
        {
            if (offset >= span.Length)
                return offset;

            offset += GetValueSize(span.Slice(offset), schema.ColumnTypes[i]);
        }

        return offset;
    }

    /// <summary>
    /// Gets the size of a value in the serialized data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetValueSize(ReadOnlySpan<byte> data, DataType type)
    {
        if (data.Length == 0)
            return 0;

        // Check null flag
        if (data[0] == 0)
            return 1; // Just null flag

        return type switch
        {
            DataType.Integer => 5,   // 1 null flag + 4 bytes
            DataType.Long => 9,      // 1 null flag + 8 bytes
            DataType.Real => 9,      // 1 null flag + 8 bytes
            DataType.Boolean => 2,   // 1 null flag + 1 byte
            DataType.DateTime => 9,  // 1 null flag + 8 bytes
            DataType.Decimal => 17,  // 1 null flag + 16 bytes
            DataType.Guid => 17,     // 1 null flag + 16 bytes
            DataType.String or DataType.Ulid or DataType.Blob => GetVariableLengthSize(data),
            _ => GetVariableLengthSize(data)
        };
    }

    /// <summary>
    /// Gets the size of a variable-length value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetVariableLengthSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
            return data.Length;

        int length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(1, 4));
        return 5 + length;
    }

    /// <summary>
    /// Deserializes a value from the raw byte data based on the data type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static T DeserializeValue<T>(ReadOnlySpan<byte> data, DataType type)
    {
        if (data.Length == 0 || data[0] == 0) // Null check
        {
            return default!;
        }

        var valueData = data.Slice(1); // Skip null flag

        if (type == DataType.Integer)
        {
            if (typeof(T) != typeof(int))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            return (T)(object)BinaryPrimitives.ReadInt32LittleEndian(valueData);
        }
        else if (type == DataType.Real)
        {
            if (typeof(T) != typeof(double))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            return (T)(object)BinaryPrimitives.ReadDoubleLittleEndian(valueData);
        }
        else if (type == DataType.Boolean)
        {
            if (typeof(T) != typeof(bool))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            return (T)(object)(valueData[0] != 0);
        }
        else if (type == DataType.Long)
        {
            if (typeof(T) != typeof(long))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            return (T)(object)BinaryPrimitives.ReadInt64LittleEndian(valueData);
        }
        else if (type == DataType.String)
        {
            if (typeof(T) != typeof(string))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            int length = BinaryPrimitives.ReadInt32LittleEndian(valueData);
            return (T)(object)Encoding.UTF8.GetString(valueData.Slice(4, length));
        }
        else if (type == DataType.DateTime)
        {
            if (typeof(T) != typeof(DateTime))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            long binaryValue = BinaryPrimitives.ReadInt64LittleEndian(valueData);
            return (T)(object)DateTime.FromBinary(binaryValue);
        }
        else if (type == DataType.Decimal)
        {
            if (typeof(T) != typeof(decimal))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            Span<int> bits = stackalloc int[4];
            for (int i = 0; i < 4; i++)
                bits[i] = BinaryPrimitives.ReadInt32LittleEndian(valueData.Slice(i * 4));
            return (T)(object)new decimal(bits);
        }
        else if (type == DataType.Ulid)
        {
            if (typeof(T) != typeof(Ulid))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            int length = BinaryPrimitives.ReadInt32LittleEndian(valueData);
            var ulidStr = Encoding.UTF8.GetString(valueData.Slice(4, length));
            return (T)(object)new Ulid(ulidStr);
        }
        else if (type == DataType.Guid)
        {
            if (typeof(T) != typeof(Guid))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            return (T)(object)new Guid(valueData);
        }
        else if (type == DataType.Blob)
        {
            if (typeof(T) != typeof(byte[]))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            return (T)(object)valueData.ToArray();
        }
        else
            throw new NotSupportedException($"Type {type} not supported for deserialization to {typeof(T)}");
    }

    /// <summary>
    /// Deserializes a value to boxed object (for Dictionary conversion).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static object DeserializeValueBoxed(ReadOnlySpan<byte> data, DataType type)
    {
        if (data.Length == 0 || data[0] == 0) // Null check
        {
            return DBNull.Value;
        }

        var valueData = data.Slice(1); // Skip null flag

        return type switch
        {
            DataType.Integer => BinaryPrimitives.ReadInt32LittleEndian(valueData),
            DataType.Long => BinaryPrimitives.ReadInt64LittleEndian(valueData),
            DataType.Real => BinaryPrimitives.ReadDoubleLittleEndian(valueData),
            DataType.Boolean => valueData[0] != 0,
            DataType.DateTime => DateTime.FromBinary(BinaryPrimitives.ReadInt64LittleEndian(valueData)),
            DataType.Decimal => DeserializeDecimal(valueData),
            DataType.Guid => new Guid(valueData),
            DataType.String => DeserializeString(valueData),
            DataType.Ulid => DeserializeUlid(valueData),
            DataType.Blob => valueData.ToArray(),
            _ => DeserializeString(valueData) // Default to string
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal DeserializeDecimal(ReadOnlySpan<byte> valueData)
    {
        Span<int> bits = stackalloc int[4];
        for (int i = 0; i < 4; i++)
            bits[i] = BinaryPrimitives.ReadInt32LittleEndian(valueData.Slice(i * 4));
        return new decimal(bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string DeserializeString(ReadOnlySpan<byte> valueData)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(valueData);
        return Encoding.UTF8.GetString(valueData.Slice(4, length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Ulid DeserializeUlid(ReadOnlySpan<byte> valueData)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(valueData);
        var ulidStr = Encoding.UTF8.GetString(valueData.Slice(4, length));
        return new Ulid(ulidStr);
    }
}
