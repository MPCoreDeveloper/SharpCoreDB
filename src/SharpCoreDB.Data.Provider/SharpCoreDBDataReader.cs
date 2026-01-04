using System.Collections;
using System.Data;
using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Data reader for SharpCoreDB query results.
/// Wraps the List&lt;Dictionary&lt;string, object&gt;&gt; results from ExecuteQuery.
/// Modern C# 14 implementation with pattern matching and collection expressions.
/// </summary>
public sealed class SharpCoreDBDataReader : DbDataReader
{
    private readonly List<Dictionary<string, object>> _results;
    private readonly string[] _fieldNames;
    private readonly Dictionary<string, int> _fieldOrdinals;
    private int _currentRow = -1;
    private bool _isClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataReader"/> class.
    /// </summary>
    public SharpCoreDBDataReader(List<Dictionary<string, object>> results, CommandBehavior behavior)
    {
        _results = results ?? [];

        if (_results.Count > 0)
        {
            _fieldNames = [.. _results[0].Keys];
            _fieldOrdinals = _fieldNames
                .Select((name, index) => (name, index))
                .ToDictionary(x => x.name, x => x.index);
        }
        else
        {
            _fieldNames = [];
            _fieldOrdinals = [];
        }
    }

    /// <summary>
    /// Gets the value at the specified ordinal.
    /// </summary>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <summary>
    /// Gets the value with the specified name.
    /// </summary>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <summary>
    /// Gets the depth of nesting for the current row (always 0).
    /// </summary>
    public override int Depth => 0;

    /// <summary>
    /// Gets the number of columns in the current row.
    /// </summary>
    public override int FieldCount => _fieldNames.Length;

    /// <summary>
    /// Gets a value indicating whether the reader has rows.
    /// </summary>
    public override bool HasRows => _results.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the reader is closed.
    /// </summary>
    public override bool IsClosed => _isClosed;

    /// <summary>
    /// Gets the number of rows changed, inserted, or deleted.
    /// </summary>
    public override int RecordsAffected => -1;

    /// <summary>
    /// Gets the value of the specified column as a Boolean.
    /// </summary>
    public override bool GetBoolean(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s => bool.Parse(s),
            _ => Convert.ToBoolean(value)
        };
    }

    /// <summary>
    /// Gets the value of the specified column as a byte.
    /// </summary>
    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));

    /// <summary>
    /// Reads a stream of bytes from the specified column.
    /// </summary>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = GetValue(ordinal);
        if (value is not byte[] bytes)
            throw new InvalidCastException($"Field at ordinal {ordinal} is not a byte array");

        if (buffer is null)
            return bytes.Length;

        var bytesToCopy = Math.Min(length, bytes.Length - (int)dataOffset);
        Array.Copy(bytes, dataOffset, buffer, bufferOffset, bytesToCopy);
        return bytesToCopy;
    }

    /// <summary>
    /// Gets the value of the specified column as a char.
    /// </summary>
    public override char GetChar(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            char c => c,
            string s when s.Length > 0 => s[0],
            _ => Convert.ToChar(value)
        };
    }

    /// <summary>
    /// Reads a stream of characters from the specified column.
    /// </summary>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = GetValue(ordinal);
        if (value is not string str)
            throw new InvalidCastException($"Field at ordinal {ordinal} is not a string");

        if (buffer is null)
            return str.Length;

        var charsToCopy = Math.Min(length, str.Length - (int)dataOffset);
        str.CopyTo((int)dataOffset, buffer, bufferOffset, charsToCopy);
        return charsToCopy;
    }

    /// <summary>
    /// Gets the name of the source data type.
    /// </summary>
    public override string GetDataTypeName(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.GetType().Name ?? "Object";
    }

    /// <summary>
    /// Gets the value of the specified column as a DateTime.
    /// </summary>
    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal));

    /// <summary>
    /// Gets the value of the specified column as a decimal.
    /// </summary>
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));

    /// <summary>
    /// Gets the value of the specified column as a double.
    /// </summary>
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));

    /// <summary>
    /// Gets the Type of the specified column.
    /// </summary>
    public override Type GetFieldType(int ordinal)
    {
        if (_currentRow < 0 || _currentRow >= _results.Count)
            return typeof(object);

        var value = GetValue(ordinal);
        return value?.GetType() ?? typeof(object);
    }

    /// <summary>
    /// Gets the value of the specified column as a float.
    /// </summary>
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));

    /// <summary>
    /// Gets the value of the specified column as a Guid.
    /// </summary>
    public override Guid GetGuid(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            byte[] b when b.Length == 16 => new Guid(b),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType().Name ?? "null"} to Guid")
        };
    }

    /// <summary>
    /// Gets the value of the specified column as a 16-bit signed integer.
    /// </summary>
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));

    /// <summary>
    /// Gets the value of the specified column as a 32-bit signed integer.
    /// </summary>
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));

    /// <summary>
    /// Gets the value of the specified column as a 64-bit signed integer.
    /// </summary>
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));

    /// <summary>
    /// Gets the name of the specified column.
    /// </summary>
    public override string GetName(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _fieldNames.Length)
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"Ordinal {ordinal} is out of range");

        return _fieldNames[ordinal];
    }

    /// <summary>
    /// Gets the column ordinal, given the name of the column.
    /// </summary>
    public override int GetOrdinal(string name)
    {
        if (_fieldOrdinals.TryGetValue(name, out var ordinal))
            return ordinal;

        throw new ArgumentException($"Field '{name}' not found", nameof(name));
    }

    /// <summary>
    /// Gets the value of the specified column as a string.
    /// </summary>
    public override string GetString(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Gets the value of the specified column.
    /// </summary>
    public override object GetValue(int ordinal)
    {
        if (_currentRow < 0 || _currentRow >= _results.Count)
            throw new InvalidOperationException("Invalid read position");

        if (ordinal < 0 || ordinal >= _fieldNames.Length)
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"Ordinal {ordinal} is out of range");

        var fieldName = _fieldNames[ordinal];
        var row = _results[_currentRow];

        return row.TryGetValue(fieldName, out var value) ? value : DBNull.Value;
    }

    /// <summary>
    /// Gets all the attribute fields in the current row.
    /// </summary>
    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    /// <summary>
    /// Gets a value indicating whether the specified column contains a null value.
    /// </summary>
    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is null or DBNull;
    }

    /// <summary>
    /// Advances to the next result set (not supported).
    /// </summary>
    public override bool NextResult()
    {
        // SharpCoreDB doesn't support multiple result sets
        return false;
    }

    /// <summary>
    /// Advances the reader to the next record.
    /// </summary>
    public override bool Read()
    {
        if (_isClosed)
            throw new InvalidOperationException("Reader is closed");

        _currentRow++;
        return _currentRow < _results.Count;
    }

    /// <summary>
    /// Returns an enumerator for iterating through the rows.
    /// </summary>
    public override IEnumerator GetEnumerator()
    {
        return new DbEnumerator(this, closeReader: false);
    }

    /// <summary>
    /// Gets the schema table for the data reader.
    /// </summary>
    public override DataTable GetSchemaTable()
    {
        var schemaTable = new DataTable("SchemaTable");

        schemaTable.Columns.Add("ColumnName", typeof(string));
        schemaTable.Columns.Add("ColumnOrdinal", typeof(int));
        schemaTable.Columns.Add("DataType", typeof(Type));
        schemaTable.Columns.Add("ColumnSize", typeof(int));
        schemaTable.Columns.Add("AllowDBNull", typeof(bool));

        for (int i = 0; i < _fieldNames.Length; i++)
        {
            var row = schemaTable.NewRow();
            row["ColumnName"] = _fieldNames[i];
            row["ColumnOrdinal"] = i;
            row["DataType"] = GetFieldType(i);
            row["ColumnSize"] = -1;
            row["AllowDBNull"] = true;

            schemaTable.Rows.Add(row);
        }

        return schemaTable;
    }

    /// <summary>
    /// Closes the reader.
    /// </summary>
    public override void Close()
    {
        _isClosed = true;
    }

    /// <summary>
    /// Disposes the reader.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }
}
