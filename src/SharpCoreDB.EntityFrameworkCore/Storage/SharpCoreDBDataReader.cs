using System.Data.Common;
using System.Collections;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Data reader for SharpCoreDB query results.
/// Modern C# 14 implementation with full functionality.
/// </summary>
public class SharpCoreDBDataReader : DbDataReader
{
    private readonly List<Dictionary<string, object>> _rows;
    private readonly List<string> _columnNames;
    private readonly Dictionary<string, Type> _columnTypes;
    private int _currentRowIndex = -1;
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataReader"/> class.
    /// </summary>
    public SharpCoreDBDataReader()
    {
        _rows = [];
        _columnNames = [];
        _columnTypes = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataReader"/> class with query results.
    /// </summary>
    /// <param name="results">The query results.</param>
    public SharpCoreDBDataReader(List<Dictionary<string, object>> results)
    {
        ArgumentNullException.ThrowIfNull(results); // ? C# 14
        
        _rows = results;
        _columnNames = results.FirstOrDefault()?.Keys.ToList() ?? [];
        _columnTypes = [];
        
        // Infer column types from first row
        if (results.Count > 0)
        {
            foreach (var col in _columnNames)
            {
                var value = results[0][col];
                _columnTypes[col] = value?.GetType() ?? typeof(object);
            }
        }
    }

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override int FieldCount => _columnNames.Count;

    /// <inheritdoc />
    public override bool HasRows => _rows.Count > 0;

    /// <inheritdoc />
    public override bool IsClosed => _closed;

    /// <inheritdoc />
    public override int RecordsAffected => _rows.Count;

    private Dictionary<string, object> CurrentRow
    {
        get
        {
            if (_currentRowIndex < 0 || _currentRowIndex >= _rows.Count)
                throw new InvalidOperationException("No current row.");
            return _rows[_currentRowIndex];
        }
    }

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal));

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = GetValue(ordinal);
        if (value is not byte[] bytes)
            throw new InvalidCastException($"Column {ordinal} is not a byte array.");

        if (buffer is null)
            return bytes.Length;

        var bytesToCopy = Math.Min(length, bytes.Length - (int)dataOffset);
        Array.Copy(bytes, (int)dataOffset, buffer, bufferOffset, bytesToCopy);
        return bytesToCopy;
    }

    /// <inheritdoc />
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal));

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = GetString(ordinal);
        if (buffer is null)
            return value.Length;

        var charsToCopy = Math.Min(length, value.Length - (int)dataOffset);
        value.CopyTo((int)dataOffset, buffer, bufferOffset, charsToCopy);
        return charsToCopy;
    }

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal)
    {
        var name = GetName(ordinal);
        return _columnTypes.TryGetValue(name, out var type) ? type.Name : "Object";
    }

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            DateTime dt => dt,
            string str => DateTime.Parse(str),
            _ => Convert.ToDateTime(value)
        };
    }

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal)
    {
        var name = GetName(ordinal);
        return _columnTypes.TryGetValue(name, out var type) ? type : typeof(object);
    }

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            Guid guid => guid,
            string str => Guid.Parse(str),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType()} to Guid")
        };
    }

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));

    /// <inheritdoc />
    public override string GetName(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _columnNames.Count)
            throw new IndexOutOfRangeException($"Invalid column ordinal: {ordinal}");
        return _columnNames[ordinal];
    }

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); // ? C# 14
        var index = _columnNames.IndexOf(name);
        if (index < 0)
            throw new IndexOutOfRangeException($"Column '{name}' not found.");
        return index;
    }

    /// <inheritdoc />
    public override string GetString(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.ToString() ?? string.Empty;
    }

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        var name = GetName(ordinal);
        var row = CurrentRow;
        
        if (!row.TryGetValue(name, out var value))
            return DBNull.Value;

        return value ?? DBNull.Value;
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values); // ? C# 14
        
        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is null or DBNull;
    }

    /// <inheritdoc />
    public override bool NextResult() => false; // SharpCoreDB doesn't support multiple result sets

    /// <inheritdoc />
    public override bool Read()
    {
        if (_closed)
            return false;

        _currentRowIndex++;
        return _currentRowIndex < _rows.Count;
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator()
    {
        while (Read())
        {
            var values = new object[FieldCount];
            GetValues(values);
            yield return values;
        }
    }

    /// <inheritdoc />
    public override void Close()
    {
        _closed = true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }
}
