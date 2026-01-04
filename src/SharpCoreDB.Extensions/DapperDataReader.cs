using SharpCoreDB.Interfaces;
using System.Collections;
using System.Data;
using System.Data.Common;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides a DbDataReader implementation for SharpCoreDB query results.
/// </summary>
internal class DapperDataReader : DbDataReader
{
    private readonly List<Dictionary<string, object>> _results;
    private readonly string[] _fieldNames;
    private readonly Dictionary<string, int> _fieldOrdinals;
    private int _currentRow = -1;
    private bool _isClosed;

    public DapperDataReader(List<Dictionary<string, object>> results)
    {
        _results = results ?? [];
        
        if (_results.Count > 0)
        {
            _fieldNames = _results[0].Keys.ToArray();
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

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override int Depth => 0;

    public override int FieldCount => _fieldNames.Length;

    public override bool HasRows => _results.Count > 0;

    public override bool IsClosed => _isClosed;

    public override int RecordsAffected => -1;

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

    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = GetValue(ordinal);
        if (value is not byte[] bytes)
            throw new InvalidCastException($"Field at ordinal {ordinal} is not a byte array");

        if (buffer == null)
            return bytes.Length;

        var bytesToCopy = Math.Min(length, bytes.Length - (int)dataOffset);
        Array.Copy(bytes, dataOffset, buffer, bufferOffset, bytesToCopy);
        return bytesToCopy;
    }

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

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = GetValue(ordinal);
        if (value is not string str)
            throw new InvalidCastException($"Field at ordinal {ordinal} is not a string");

        if (buffer == null)
            return str.Length;

        var charsToCopy = Math.Min(length, str.Length - (int)dataOffset);
        str.CopyTo((int)dataOffset, buffer, bufferOffset, charsToCopy);
        return charsToCopy;
    }

    public override string GetDataTypeName(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.GetType().Name ?? "Object";
    }

    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal));

    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));

    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));

    public override Type GetFieldType(int ordinal)
    {
        if (_currentRow < 0 || _currentRow >= _results.Count)
            return typeof(object);

        var value = GetValue(ordinal);
        return value?.GetType() ?? typeof(object);
    }

    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));

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

    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));

    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));

    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));

    public override string GetName(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _fieldNames.Length)
            throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range");
        
        return _fieldNames[ordinal];
    }

    public override int GetOrdinal(string name)
    {
        if (_fieldOrdinals.TryGetValue(name, out var ordinal))
            return ordinal;
        
        throw new IndexOutOfRangeException($"Field '{name}' not found");
    }

    public override string GetString(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.ToString() ?? string.Empty;
    }

    public override object GetValue(int ordinal)
    {
        if (_currentRow < 0 || _currentRow >= _results.Count)
            throw new InvalidOperationException("Invalid read position");

        if (ordinal < 0 || ordinal >= _fieldNames.Length)
            throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range");

        var fieldName = _fieldNames[ordinal];
        var row = _results[_currentRow];
        
        return row.TryGetValue(fieldName, out var value) ? value : DBNull.Value;
    }

    public override int GetValues(object[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        
        return count;
    }

    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value == null || value == DBNull.Value;
    }

    public override bool NextResult()
    {
        // SharpCoreDB doesn't support multiple result sets
        return false;
    }

    public override bool Read()
    {
        if (_isClosed)
            throw new InvalidOperationException("Reader is closed");

        _currentRow++;
        return _currentRow < _results.Count;
    }

    public override IEnumerator GetEnumerator()
    {
        return new DbEnumerator(this, closeReader: false);
    }

    public override void Close()
    {
        _isClosed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }
}
