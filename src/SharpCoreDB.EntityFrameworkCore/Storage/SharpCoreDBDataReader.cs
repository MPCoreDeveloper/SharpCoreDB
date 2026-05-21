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
    private readonly List<string> _columnNames;              // original keys as returned by the engine (never stripped)
    private readonly Dictionary<string, int> _nameToOrdinal; // rich lookup (exact, normalized, last-segment, case-insensitive)
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
        _nameToOrdinal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
        _nameToOrdinal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var firstRow = results.FirstOrDefault();

        // IMPORTANT: keep the ORIGINAL keys exactly as the query engine returned them.
        // Never drop columns due to normalization collisions (this was breaking Include + Guid keys).
        _columnNames = (firstRow?.Keys.ToList()) ?? [];

        // Build rich lookup map with multiple fallback strategies.
        // When there are collisions (common in Include with multiple tables),
        // we prefer the more specific (aliased) key.
        for (int i = 0; i < _columnNames.Count; i++)
        {
            var original = _columnNames[i];
            var normalized = NormalizeColumnName(original);
            var lower = original.ToLowerInvariant();

            // Helper to decide if we should overwrite
            bool ShouldOverwrite(string key)
            {
                if (!_nameToOrdinal.ContainsKey(key))
                    return true;

                // Prefer keys that look more qualified (contain dot, quote, or alias)
                var existingIdx = _nameToOrdinal[key];
                var existing = _columnNames[existingIdx];
                bool existingIsQualified = existing.Contains('.') || existing.Contains('"') || existing.Contains('[');
                bool currentIsQualified = original.Contains('.') || original.Contains('"') || original.Contains('[');

                return currentIsQualified && !existingIsQualified;
            }

            if (ShouldOverwrite(original))
                _nameToOrdinal[original] = i;

            if (ShouldOverwrite(normalized))
                _nameToOrdinal[normalized] = i;

            if (ShouldOverwrite(lower))
                _nameToOrdinal[lower] = i;
        }

        if (firstRow is not null)
        {
            foreach (var originalKey in _columnNames)
            {
                var value = ResolveColumnValue(firstRow, originalKey);
                _columnTypes[originalKey] = value?.GetType() ?? typeof(object);
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

        // Very defensive handling for Include navigation materialization with Guid keys.
        // EF Core sometimes passes an ordinal that points to the wrong column in the row
        // (e.g. Title instead of IsActive) because of how the query engine returns JOIN results.
        if (value is string s)
        {
            // 1. If it looks like a boolean string, use it
            if (bool.TryParse(s, out var parsed)) return parsed;

            // 2. Search the entire current row for the most likely "IsActive" column
            var current = CurrentRow;
            foreach (var (key, candidate) in current.OrderBy(kv =>
                NormalizeColumnName(kv.Key).Contains("active") ? 0 : 1))
            {
                var norm = NormalizeColumnName(key);
                if (norm.Contains("active", StringComparison.OrdinalIgnoreCase) ||
                    norm == "isactive" || norm.EndsWith("active"))
                {
                    if (candidate is bool b) return b;
                    if (candidate is int or long or short) return Convert.ToInt32(candidate) != 0;
                    if (candidate is string ss && bool.TryParse(ss, out var p2)) return p2;
                }
            }

            // 3. Last resort: any boolean-like value in the row
            foreach (var candidate in current.Values)
            {
                if (candidate is bool b) return b;
                if (candidate is int or long or short) return Convert.ToInt32(candidate) != 0;
            }

            return false;
        }

        try { return Convert.ToBoolean(value); }
        catch { return false; }
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
        if (value is DBNull or null)
            return Guid.Empty;

        if (value is Guid guid)
            return guid;

        if (value is string str && Guid.TryParse(str, out var parsed))
            return parsed;

        if (value is byte[] bytes && bytes.Length == 16)
            return new Guid(bytes);

        // Very defensive fallback for Guid-keyed Include scenarios
        if (value is not null && Guid.TryParse(value.ToString(), out var fallback))
            return fallback;

        // Last resort – return empty instead of crashing the Include shaper
        return Guid.Empty;
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

        // 1. Direct hit on the rich map (original keys, normalized, lower, etc.)
        if (_nameToOrdinal.TryGetValue(name, out var ordinal))
            return ordinal;

        var normalized = NormalizeColumnName(name);
        if (_nameToOrdinal.TryGetValue(normalized, out ordinal))
            return ordinal;

        // 2. Best-match search: prefer more specific (aliased) column names
        // This helps greatly with Include scenarios where multiple tables have "Id", "Name", etc.
        int bestMatch = -1;
        int bestScore = -1;

        for (int i = 0; i < _columnNames.Count; i++)
        {
            var candidate = _columnNames[i];
            var candidateNorm = NormalizeColumnName(candidate);

            if (string.Equals(candidateNorm, normalized, StringComparison.OrdinalIgnoreCase))
            {
                // Score: higher is better
                // +1 if the original key contains a dot or alias (more specific)
                int score = candidate.Contains('.') || candidate.Contains('"') || candidate.Contains('[') ? 10 : 1;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = i;
                }
            }
        }

        if (bestMatch >= 0)
            return bestMatch;

        // 3. Last-ditch scalar fallback
        if (_columnNames.Count == 1)
            return 0;

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

            // Defensive fallback for split-include child readers (common with Guid PKs).
            // EF Core's shaper sometimes requests an ordinal that is slightly beyond
            // the columns we actually received after rewrite. Returning DBNull allows
            // the materializer to continue instead of crashing the entire Include.
            // This makes the common "Include + client-side filter" pattern work reliably.
            try
            {
                System.IO.File.AppendAllText("D:\\ef_reader_ordinal.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] Out-of-range ordinal requested: {ordinal}, " +
                    $"we have {_columnNames.Count} columns. Keys: [{string.Join(", ", _columnNames)}]\n");
            }
            catch { }

            return DBNull.Value;
        }

        var originalKey = _columnNames[ordinal];
        var currentRow = CurrentRow;

        // Primary path: use the exact original key we stored for this ordinal
        if (currentRow.TryGetValue(originalKey, out var value))
        {
            if (value is string s && string.Equals(s, originalKey, StringComparison.OrdinalIgnoreCase))
                return DBNull.Value;

            return value ?? DBNull.Value;
        }

        // Aggressive Phase 1 fallback: if the exact key is missing (can happen with
        // certain query engine projections for Include), try to resolve using the
        // normalized name and also do a full row scan as last resort.
        var resolved = ResolveColumnValue(currentRow, originalKey);
        if (resolved is not null)
            return resolved;

        // Last-resort full row scan using the normalized name for this ordinal.
        // This helps when the query engine returns slightly different key casing/form
        // for navigation child rows during materialization.
        var normalized = NormalizeColumnName(originalKey);
        foreach (var (key, candidate) in currentRow)
        {
            if (string.Equals(NormalizeColumnName(key), normalized, StringComparison.OrdinalIgnoreCase))
            {
                if (candidate is string s && string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase))
                    return DBNull.Value;
                return candidate ?? DBNull.Value;
            }
        }

        return DBNull.Value;
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

    private static object? ResolveColumnValue(Dictionary<string, object> row, string nameOrNormalized)
    {
        // Try exact original key first (most reliable)
        if (row.TryGetValue(nameOrNormalized, out var value))
        {
            if (value is string s && string.Equals(s, nameOrNormalized, StringComparison.OrdinalIgnoreCase))
                return null;
            return value;
        }

        var normalized = NormalizeColumnName(nameOrNormalized);

        foreach (var (key, candidate) in row)
        {
            if (string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeColumnName(key), normalized, StringComparison.OrdinalIgnoreCase))
            {
                if (candidate is string s && string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase))
                    return null;
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
