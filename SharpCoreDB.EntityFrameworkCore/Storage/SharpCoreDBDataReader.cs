using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Data reader for SharpCoreDB query results.
/// </summary>
public class SharpCoreDBDataReader : DbDataReader
{
    private readonly List<Dictionary<string, object>> _results;
    private readonly string[] _columnNames;
    private int _currentIndex = -1;
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBDataReader class with no results.
    /// </summary>
    public SharpCoreDBDataReader()
    {
        _results = [];
        _columnNames = [];
    }

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBDataReader class with query results.
    /// </summary>
    /// <param name="results">The query results.</param>
    public SharpCoreDBDataReader(List<Dictionary<string, object>> results)
    {
        _results = results ?? [];
        _columnNames = _results.Count > 0 ? _results[0].Keys.ToArray() : [];
    }

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override int FieldCount => _columnNames.Length;

    /// <inheritdoc />
    public override bool HasRows => _results.Count > 0;

    /// <inheritdoc />
    public override bool IsClosed => _closed;

    /// <inheritdoc />
    public override int RecordsAffected => 0;

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is bool b ? b : Convert.ToBoolean(value);
    }

    /// <inheritdoc />
    public override byte GetByte(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is byte b ? b : Convert.ToByte(value);
    }

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public override char GetChar(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is char c ? c : Convert.ToChar(value);
    }

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is DateTime dt ? dt : Convert.ToDateTime(value);
    }

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is decimal d ? d : Convert.ToDecimal(value);
    }

    /// <inheritdoc />
    public override double GetDouble(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is double dbl ? dbl : Convert.ToDouble(value);
    }

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal)
    {
        if (_currentIndex < 0 || _currentIndex >= _results.Count)
            return typeof(object);

        var value = GetValue(ordinal);
        return value?.GetType() ?? typeof(object);
    }

    /// <inheritdoc />
    public override float GetFloat(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is float f ? f : Convert.ToSingle(value);
    }

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is Guid g ? g : Guid.Parse(value?.ToString() ?? string.Empty);
    }

    /// <inheritdoc />
    public override short GetInt16(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is short s ? s : Convert.ToInt16(value);
    }

    /// <inheritdoc />
    public override int GetInt32(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is int i ? i : Convert.ToInt32(value);
    }

    /// <inheritdoc />
    public override long GetInt64(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is long l ? l : Convert.ToInt64(value);
    }

    /// <inheritdoc />
    public override string GetName(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _columnNames.Length)
            throw new IndexOutOfRangeException();

        return _columnNames[ordinal];
    }

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        var index = Array.IndexOf(_columnNames, name);
        if (index < 0)
            throw new ArgumentException($"Column '{name}' not found");

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
        if (_currentIndex < 0 || _currentIndex >= _results.Count)
            throw new InvalidOperationException("No current row");

        if (ordinal < 0 || ordinal >= _columnNames.Length)
            throw new IndexOutOfRangeException();

        var columnName = _columnNames[ordinal];
        var currentRow = _results[_currentIndex];
        
        return currentRow.TryGetValue(columnName, out var value) ? value : DBNull.Value;
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, _columnNames.Length);
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
        return value == null || value == DBNull.Value;
    }

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override bool Read()
    {
        _currentIndex++;
        return _currentIndex < _results.Count;
    }

    /// <inheritdoc />
    public override System.Collections.IEnumerator GetEnumerator()
    {
        while (Read())
        {
            yield return this;
        }
    }

    /// <inheritdoc />
    public override void Close()
    {
        _closed = true;
    }
}
