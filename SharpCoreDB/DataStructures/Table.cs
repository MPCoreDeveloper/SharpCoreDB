using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.CompilerServices;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Implementation of ITable.
/// </summary>
public class Table : ITable
{
    /// <summary>
    /// Parameterless constructor for deserialization.
    /// </summary>
    public Table() { }

    /// <summary>
    /// Constructor with storage.
    /// </summary>
    public Table(IStorage storage, bool isReadOnly = false) => (_storage, _isReadOnly) = (storage, isReadOnly);

    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public List<string> Columns { get; set; } = [];

    /// <inheritdoc />
    public List<DataType> ColumnTypes { get; set; } = [];

    /// <summary>
    /// The primary key column index.
    /// </summary>
    public int PrimaryKeyIndex { get; set; } = -1;

    /// <summary>
    /// Whether columns are auto-generated.
    /// </summary>
    public List<bool> IsAuto { get; set; } = [];

    /// <summary>
    /// The data file path.
    /// </summary>
    public string DataFile { get; set; } = string.Empty;

    /// <summary>
    /// The index for primary key.
    /// </summary>
    public IIndex<string, long> Index { get; set; } = new BTree<string, long>();

    /// <summary>
    /// Hash indexes for fast WHERE clause lookups on specific columns.
    /// </summary>
    private readonly Dictionary<string, HashIndex> _hashIndexes = new();

    private IStorage _storage;
    // .NET 10: Use Lock instead of ReaderWriterLockSlim for better performance
    private readonly Lock _lock = new();
    private bool _isReadOnly;

    /// <summary>
    /// Sets the storage for this table.
    /// </summary>
    public void SetStorage(IStorage storage) => _storage = storage;

    /// <summary>
    /// Sets the readonly flag.
    /// </summary>
    public void SetReadOnly(bool isReadOnly) => _isReadOnly = isReadOnly;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Insert(Dictionary<string, object> row)
    {
        if (_isReadOnly) throw new InvalidOperationException("Cannot insert in readonly mode");
        
        // .NET 10: Use lock statement with Lock type for better performance
        lock (_lock)
        {
            // Validate types
            for (int i = 0; i < Columns.Count; i++)
            {
                if (row.TryGetValue(Columns[i], out var val))
                {
                    if (!IsValidType(val, ColumnTypes[i]))
                        throw new InvalidOperationException($"Type mismatch for column {Columns[i]}");
                }
            }

            // Fill missing columns
            for (int i = 0; i < Columns.Count; i++)
            {
                var col = Columns[i];
                if (!row.ContainsKey(col))
                {
                    if (IsAuto[i])
                    {
                        row[col] = GenerateAutoValue(ColumnTypes[i]);
                    }
                    else
                    {
                        row[col] = null; // assume nullable
                    }
                }
            }

            // Check primary key
            if (PrimaryKeyIndex >= 0)
            {
                var newPkVal = row[Columns[PrimaryKeyIndex]];
                var (found, _) = Index.Search(newPkVal?.ToString() ?? string.Empty);
                if (found)
                {
                    throw new InvalidOperationException("Primary key violation");
                }
            }

            // Prepare row data
            using var rowMs = new MemoryStream();
            using var rowWriter = new BinaryWriter(rowMs);
            foreach (var col in Columns)
            {
                WriteTypedValue(rowWriter, row[col], ColumnTypes[Columns.IndexOf(col)]);
            }
            var rowData = rowMs.ToArray();

            // Append to file
            var data = _storage.ReadBytes(DataFile) ?? [];
            using var ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Write(rowData, 0, rowData.Length);
            _storage.WriteBytes(DataFile, ms.ToArray());

            // Update hash indexes
            foreach (var index in _hashIndexes.Values)
            {
                index.Add(row);
            }
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true)
    {
        // .NET 10: Lock type provides better performance than ReaderWriterLockSlim
        // For read-only mode, skip locking entirely for maximum throughput
        if (_isReadOnly)
        {
            return SelectInternal(where, orderBy, asc);
        }
        
        lock (_lock)
        {
            return SelectInternal(where, orderBy, asc);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Dictionary<string, object>> SelectInternal(string? where, string? orderBy, bool asc)
    {
        var results = new List<Dictionary<string, object>>();
        
        // Try to use hash index for WHERE clause if available
        bool usedHashIndex = false;
        if (where != null)
        {
            var whereParts = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (whereParts.Length == 3 && whereParts[1] == "=")
            {
                var col = whereParts[0];
                var val = whereParts[2].Trim('\'');
                
                // Check if we have a hash index for this column
                if (_hashIndexes.TryGetValue(col, out var hashIndex))
                {
                    // O(1) hash lookup instead of full table scan!
                    // Parse the value to the correct type for lookup
                    var colIdx = Columns.IndexOf(col);
                    if (colIdx >= 0)
                    {
                        var typedValue = ParseValueForHashLookup(val, ColumnTypes[colIdx]);
                        results = hashIndex.Lookup(typedValue);
                        usedHashIndex = true;
                    }
                }
            }
        }

        // Fall back to full table scan if hash index wasn't used
        if (!usedHashIndex)
        {
            var data = _storage.ReadBytes(DataFile);
            if (data == null || data.Length == 0) return results;
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            while (ms.Position < ms.Length)
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < Columns.Count; i++)
                {
                    row[Columns[i]] = ReadTypedValue(reader, ColumnTypes[i]);
                }
                results.Add(row);
            }

            if (where != null)
            {
                // parse where
                var whereParts = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (whereParts.Length == 3 && whereParts[1] == "=")
                {
                    var col = whereParts[0];
                    var val = whereParts[2].Trim('\'');
                    results = results.Where(r => string.Equals(r[col]?.ToString() ?? "NULL", val, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
        }

        if (orderBy != null)
        {
            var idx = Columns.IndexOf(orderBy);
            if (idx >= 0)
            {
                results = asc ? [.. results.OrderBy(r => r[Columns[idx]])] : [.. results.OrderByDescending(r => r[Columns[idx]])];

            }
        }
        return results;
    }

    /// <summary>
    /// Parses a string value to the appropriate type for hash index lookup.
    /// </summary>
    private object ParseValueForHashLookup(string value, DataType type)
    {
        return type switch
        {
            DataType.Integer => int.TryParse(value, out var i) ? i : value,
            DataType.Long => long.TryParse(value, out var l) ? l : value,
            DataType.Real => double.TryParse(value, out var d) ? d : value,
            DataType.Decimal => decimal.TryParse(value, out var dec) ? dec : value,
            DataType.Boolean => bool.TryParse(value, out var b) ? b : value,
            DataType.DateTime => DateTime.TryParse(value, out var dt) ? dt : value,
            DataType.Guid => Guid.TryParse(value, out var g) ? g : value,
            _ => value // String, Ulid, Blob, etc.
        };
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Update(string where, Dictionary<string, object> updates)
    {
        var allRows = Select(); // load outside lock
        var updatedRows = new List<Dictionary<string, object>>();
        var modifiedRows = new List<(Dictionary<string, object> oldRow, Dictionary<string, object> newRow)>();
        
        foreach (var row in allRows)
        {
            var matches = EvaluateWhere(row, where);
            if (matches)
            {
                // Keep a copy of the old row for index updates
                var oldRow = new Dictionary<string, object>(row);
                
                foreach (var update in updates)
                {
                    row[update.Key] = update.Value;
                }
                
                modifiedRows.Add((oldRow, row));
            }
            updatedRows.Add(row);
        }
        
        // .NET 10: Use lock statement with Lock type
        lock (_lock)
        {
            // Rewrite file
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            foreach (var row in updatedRows)
            {
                foreach (var col in Columns)
                {
                    WriteTypedValue(writer, row[col], ColumnTypes[Columns.IndexOf(col)]);
                }
            }
            _storage.WriteBytes(DataFile, ms.ToArray());

            // Optimize: Only update modified rows in indexes (O(k) instead of O(n))
            foreach (var index in _hashIndexes.Values)
            {
                foreach (var (oldRow, newRow) in modifiedRows)
                {
                    index.Remove(oldRow);  // Remove old key
                    index.Add(newRow);      // Add new key
                }
            }
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Delete(string where)
    {
        var allRows = Select(); // load outside lock
        var deletedRows = new List<Dictionary<string, object>>();
        var remainingRows = new List<Dictionary<string, object>>();
        
        foreach (var row in allRows)
        {
            if (EvaluateWhere(row, where))
            {
                deletedRows.Add(row);
            }
            else
            {
                remainingRows.Add(row);
            }
        }
        
        // .NET 10: Use lock statement with Lock type
        lock (_lock)
        {
            // Rewrite
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            foreach (var row in remainingRows)
            {
                foreach (var col in Columns)
                {
                    WriteTypedValue(writer, row[col], ColumnTypes[Columns.IndexOf(col)]);
                }
            }
            _storage.WriteBytes(DataFile, ms.ToArray());

            // Optimize: Only remove deleted rows from indexes (O(k) instead of O(n))
            foreach (var index in _hashIndexes.Values)
            {
                foreach (var deletedRow in deletedRows)
                {
                    index.Remove(deletedRow);
                }
            }
        }
    }

    private bool IsValidType(object val, DataType type) => val is null || type switch
    {
        DataType.Integer => val is int,
        DataType.String => val is string,
        DataType.Real => val is double,
        DataType.Blob => val is byte[],
        DataType.Boolean => val is bool,
        DataType.DateTime => val is DateTime,
        DataType.Long => val is long,
        DataType.Decimal => val is decimal,
        DataType.Ulid => val is Ulid,
        DataType.Guid => val is Guid,
        _ => false
    };

    private void WriteTypedValue(BinaryWriter writer, object val, DataType type)
    {
        if (val is null)
        {
            writer.Write((byte)0); // null flag
            return;
        }
        writer.Write((byte)1); // not null
        switch (type)
        {
            case DataType.Integer: writer.Write((int)val); break;
            case DataType.String: writer.Write((string)val); break;
            case DataType.Real: writer.Write((double)val); break;
            case DataType.Blob: var bytes = (byte[])val; writer.Write(bytes.Length); writer.Write(bytes); break;
            case DataType.Boolean: writer.Write((bool)val); break;
            case DataType.DateTime: writer.Write(((DateTime)val).Ticks); break;
            case DataType.Long: writer.Write((long)val); break;
            case DataType.Decimal: var bits = decimal.GetBits((decimal)val); foreach (var bit in bits) writer.Write(bit); break;
            case DataType.Ulid: writer.Write(((Ulid)val).Value); break;
            case DataType.Guid: writer.Write(((Guid)val).ToString()); break;
        }
    }

    private object ReadTypedValue(BinaryReader reader, DataType type)
    {
        var isNull = reader.ReadByte();
        if (isNull == 0) return null;
        switch (type)
        {
            case DataType.Integer: return reader.ReadInt32();
            case DataType.String: return reader.ReadString();
            case DataType.Real: return reader.ReadDouble();
            case DataType.Blob: var length = reader.ReadInt32(); return reader.ReadBytes(length);
            case DataType.Boolean: return reader.ReadBoolean();
            case DataType.DateTime: return new DateTime(reader.ReadInt64());
            case DataType.Long: return reader.ReadInt64();
            case DataType.Decimal: var bits = new int[4]; for (int i = 0; i < 4; i++) bits[i] = reader.ReadInt32(); return new decimal(bits);
            case DataType.Ulid: return Ulid.Parse(reader.ReadString());
            case DataType.Guid: return Guid.Parse(reader.ReadString());
            default: return null;
        }
    }

    private object GenerateAutoValue(DataType type) => type switch
    {
        DataType.Ulid => Ulid.NewUlid(),
        DataType.Guid => Guid.NewGuid(),
        _ => throw new InvalidOperationException($"Auto generation not supported for type {type}")
    };

    private bool EvaluateWhere(Dictionary<string, object> row, string where)
    {
        if (string.IsNullOrEmpty(where)) return true;
        var parts = where.Split(' ');
        if (parts.Length == 3 && parts[1] == "=")
        {
            var col = parts[0];
            var val = parts[2].Trim('\'');
            return row[col]?.ToString() == val;
        }
        return true;
    }

    /// <summary>
    /// Creates a hash index on the specified column for fast WHERE clause lookups.
    /// </summary>
    /// <param name="columnName">The column name to index.</param>
    /// <remarks>
    /// This method loads all table rows into memory to build the index.
    /// For large tables (>1M rows), consider creating indexes before bulk data loading.
    /// Once created, the index is maintained incrementally on INSERT/UPDATE/DELETE.
    /// </remarks>
    public void CreateHashIndex(string columnName)
    {
        if (!Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} does not exist in table {Name}");

        if (_hashIndexes.ContainsKey(columnName))
            return; // Already indexed

        var index = new HashIndex(Name, columnName);
        
        // Build index from existing data (one-time operation)
        var allRows = Select();
        index.Rebuild(allRows);
        
        _hashIndexes[columnName] = index;
    }

    /// <summary>
    /// Checks if a hash index exists for the specified column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if index exists.</returns>
    public bool HasHashIndex(string columnName) => _hashIndexes.ContainsKey(columnName);

    /// <summary>
    /// Gets hash index statistics for a column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>Index statistics or null if no index exists.</returns>
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName)
    {
        if (_hashIndexes.TryGetValue(columnName, out var index))
            return index.GetStatistics();
        return null;
    }
}
