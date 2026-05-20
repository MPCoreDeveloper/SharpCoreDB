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
        ArgumentNullException.ThrowIfNull(results);

        _rows = results;
        _columnTypes = [];

        var firstRow = results.FirstOrDefault();
        _columnNames = BuildNormalizedColumnNames(firstRow);

        if (firstRow is not null)
        {
            foreach (var col in _columnNames)
            {
                var value = ResolveColumnValue(firstRow, col);
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
    public override bool GetBoolean(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is DBNull or null)
            return false;
        return Convert.ToBoolean(value);
    }

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
        if (value is DBNull or null)
            throw new InvalidCastException("Cannot convert NULL to DateTime.");
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string str => DateTime.Parse(str, null, System.Globalization.DateTimeStyles.RoundtripKind),
            long ticks => new DateTime(ticks, DateTimeKind.Utc),
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
            byte[] bytes when bytes.Length == 16 => new Guid(bytes),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType()} to Guid")
        };
    }

    /// <inheritdoc />
    public override short GetInt16(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is DBNull or null)
            throw new InvalidCastException("Cannot convert NULL to Int16.");
        return Convert.ToInt16(value);
    }

    /// <inheritdoc />
    public override int GetInt32(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is DBNull or null)
            throw new InvalidCastException("Cannot convert NULL to Int32.");
        return Convert.ToInt32(value);
    }

    /// <inheritdoc />
    public override long GetInt64(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is DBNull or null)
            throw new InvalidCastException("Cannot convert NULL to Int64.");
        return Convert.ToInt64(value);
    }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedName = NormalizeColumnName(name);
        for (int i = 0; i < _columnNames.Count; i++)
        {
            if (string.Equals(_columnNames[i], normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // Fallback for scalar key retrieval (last_insert_rowid):
        // If EF Core asks for a specific column name but we only have one column,
        // return it. This makes "SELECT last_insert_rowid() AS \"BlogId\"" work
        // even if the exact alias normalization differs slightly.
        if (_columnNames.Count == 1)
        {
            return 0;
        }

        throw new IndexOutOfRangeException($"Column '{name}' not found.");
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
        if (ordinal < 0 || ordinal >= _columnNames.Count)
        {
            // Scalar fallback: when only one column exists (key retrieval case),
            // EF Core may ask for ordinal 0 even if the exact name lookup failed.
            if (_columnNames.Count == 1 && ordinal == 0)
            {
                var current = CurrentRow;
                return current.Values.FirstOrDefault() ?? DBNull.Value;
            }
            throw new IndexOutOfRangeException($"Invalid column ordinal: {ordinal}");
        }

        var name = GetName(ordinal);
        var currentRow = CurrentRow;
        var value = ResolveColumnValue(currentRow, name);

        return value ?? DBNull.Value;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is null or DBNull;
    }

    /// <inheritdoc />
    public override bool NextResult() => false;

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

    private static List<string> BuildNormalizedColumnNames(Dictionary<string, object>? firstRow)
    {
        if (firstRow is null || firstRow.Count == 0)
        {
            return [];
        }

        var names = new List<string>(firstRow.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in firstRow.Keys)
        {
            var normalized = NormalizeColumnName(key);
            if (seen.Add(normalized))
            {
                names.Add(normalized);
            }
        }

        return names;
    }

    private static object? ResolveColumnValue(Dictionary<string, object> row, string normalizedName)
    {
        if (row.TryGetValue(normalizedName, out var value))
        {
            // Guard: if the value is literally the column name string, the upstream
            // query engine returned a bad projection (common with legacy parser + EF SQL).
            // Treat as missing column so EF gets DBNull instead of a FormatException.
            if (value is string s && string.Equals(s, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return value;
        }

        foreach (var (key, candidate) in row)
        {
            if (string.Equals(NormalizeColumnName(key), normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                if (candidate is string s && string.Equals(s, normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return candidate;
            }
        }

        return null;
    }

    private static string NormalizeColumnName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = name.Trim();

        // Strip table/alias prefix. EF Core generates alias-qualified columns that the
        // SharpCoreDB parser may return verbatim as dictionary keys, e.g.:
        //   b."BlogId"  -> BlogId
        //   b"."BlogId  -> BlogId   (parser artefact when alias contains quotes)
        //   "Blogs"."BlogId" -> BlogId
        // Strategy: find the last dot that is NOT inside a quoted segment, then take the suffix.
        var lastRealDot = FindLastUnquotedDot(normalized);
        if (lastRealDot >= 0 && lastRealDot < normalized.Length - 1)
        {
            normalized = normalized[(lastRealDot + 1)..];
        }
        else
        {
            // Fallback: plain last-dot split (handles b.BlogId)
            var lastDot = normalized.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < normalized.Length - 1)
                normalized = normalized[(lastDot + 1)..];
        }

        normalized = normalized.Trim('"', '[', ']', '`');
        return normalized;
    }

    private static int FindLastUnquotedDot(string name)
    {
        var inQuote = false;
        var quoteChar = '\0';
        var result = -1;
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (!inQuote && (ch == '"' || ch == '[' || ch == '`'))
            {
                inQuote = true;
                quoteChar = ch == '[' ? ']' : ch;
            }
            else if (inQuote && ch == quoteChar)
            {
                inQuote = false;
            }
            else if (!inQuote && ch == '.')
            {
                result = i;
            }
        }
        return result;
    }
}
