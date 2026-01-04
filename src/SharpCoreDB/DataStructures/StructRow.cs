#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a zero-copy view of a single row in a query result.
/// Provides lazy deserialization of column values without allocations during iteration.
/// </summary>
public readonly struct StructRow
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly StructRowSchema _schema;
    private readonly int _rowOffset;

    // Optional cache for deserialized values (improves performance for repeated access)
    private readonly Dictionary<int, object>? _cache;

    /// <summary>
    /// Initializes a new instance of StructRow.
    /// </summary>
    /// <param name="data">The raw byte data containing all rows.</param>
    /// <param name="schema">The schema defining column layout.</param>
    /// <param name="rowOffset">The byte offset to this row within the data.</param>
    /// <param name="enableCaching">Whether to enable caching of deserialized values.</param>
    internal StructRow(ReadOnlyMemory<byte> data, StructRowSchema schema, int rowOffset, bool enableCaching = false)
    {
        _data = data;
        _schema = schema;
        _rowOffset = rowOffset;
        _cache = enableCaching ? new Dictionary<int, object>() : null;
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
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if columnIndex is invalid.</exception>
    /// <exception cref="InvalidCastException">Thrown if T does not match the column type.</exception>
    public T GetValue<T>(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)_schema.ColumnNames.Length)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));

        // Check cache first (optional optimization)
        if (_cache?.TryGetValue(columnIndex, out var cached) == true)
        {
            return (T)cached;
        }

        // Lazy deserialization
        var span = _data.Span.Slice(_rowOffset + _schema.ColumnOffsets[columnIndex]);
        T value = DeserializeValue<T>(span, _schema.ColumnTypes[columnIndex]);

        // Cache result (optional)
        _cache?.Add(columnIndex, value!);

        return value;
    }

    /// <summary>
    /// Gets the value of the specified column by name with lazy deserialization.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="columnName">The column name.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentException">Thrown if columnName is not found.</exception>
    /// <exception cref="InvalidCastException">Thrown if T does not match the column type.</exception>
    public T GetValue<T>(string columnName)
    {
        int index = Array.IndexOf(_schema.ColumnNames, columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' not found", nameof(columnName));
        return GetValue<T>(index);
    }

    /// <summary>
    /// Checks if the specified column is null.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>True if the column value is null, otherwise false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if columnIndex is invalid.</exception>
    public bool IsNull(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)_schema.ColumnNames.Length)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));

        var span = _data.Span.Slice(_rowOffset + _schema.ColumnOffsets[columnIndex]);
        return span.Length == 0 || span[0] == 0; // Null flag
    }

    /// <summary>
    /// Deserializes a value from the raw byte data based on the data type.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">The raw byte span containing the value.</param>
    /// <param name="type">The data type of the column.</param>
    /// <returns>The deserialized value.</returns>
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
            return (T)(object)System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(valueData);
        }
        else if (type == DataType.Real)
        {
            if (typeof(T) != typeof(double))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            return (T)(object)System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(valueData);
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
            return (T)(object)System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(valueData);
        }
        else if (type == DataType.String)
        {
            if (typeof(T) != typeof(string))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(valueData);
            return (T)(object)System.Text.Encoding.UTF8.GetString(valueData.Slice(4, length));
        }
        else if (type == DataType.DateTime)
        {
            if (typeof(T) != typeof(DateTime))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            long binaryValue = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(valueData);
            return (T)(object)DateTime.FromBinary(binaryValue);
        }
        else if (type == DataType.Decimal)
        {
            if (typeof(T) != typeof(decimal))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            Span<int> bits = stackalloc int[4];
            for (int i = 0; i < 4; i++)
                bits[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(valueData.Slice(i * 4));
            return (T)(object)new decimal(bits);
        }
        else if (type == DataType.Ulid)
        {
            if (typeof(T) != typeof(Ulid))
                throw new InvalidCastException($"Cannot cast {type} to {typeof(T)}");
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(valueData);
            var ulidStr = System.Text.Encoding.UTF8.GetString(valueData.Slice(4, length));
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
}
