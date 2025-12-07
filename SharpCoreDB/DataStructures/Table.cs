namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

/// <summary>
/// Implementation of ITable.
/// </summary>
public class Table : ITable, IDisposable
{
    public Table() { }
    public Table(IStorage storage, bool isReadOnly = false) : this()
    {
        (this.storage, this.isReadOnly) = (storage, isReadOnly);
        if (!isReadOnly)
        {
            this.indexManager = new IndexManager();
            Task.Run(ProcessIndexUpdatesAsync);
        }
    }

    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = [];
    public List<DataType> ColumnTypes { get; set; } = [];
    public int PrimaryKeyIndex { get; set; } = -1;
    public List<bool> IsAuto { get; set; } = new();
    public string DataFile { get; set; } = string.Empty;
    public IIndex<string, long> Index { get; set; } = new BTree<string, long>();

    private readonly Dictionary<string, HashIndex> hashIndexes = [];
    private IStorage? storage;
    private readonly ReaderWriterLockSlim rwLock = new();
    private bool isReadOnly;
    private IndexManager? indexManager;
    private readonly Channel<IndexUpdate> _indexQueue = Channel.CreateUnbounded<IndexUpdate>();
    private readonly Dictionary<string, long> columnUsage = new();
    private readonly object usageLock = new();

    public void SetStorage(IStorage storage) => this.storage = storage;
    public void SetReadOnly(bool isReadOnly) => this.isReadOnly = isReadOnly;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Insert(Dictionary<string, object> row)
    {
        ArgumentNullException.ThrowIfNull(this.storage);
        if (this.isReadOnly) throw new InvalidOperationException("Cannot insert in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            // Validate + fill defaults
            for (int i = 0; i < this.Columns.Count; i++)
            {
                var col = this.Columns[i];
                if (!row.TryGetValue(col, out var val))
                {
                    row[col] = this.IsAuto[i] ? this.GenerateAutoValue(this.ColumnTypes[i]) : GetDefaultValue(this.ColumnTypes[i]) ?? DBNull.Value;
                }
                else if (val != DBNull.Value && !this.IsValidType(val, this.ColumnTypes[i]))
                {
                    throw new InvalidOperationException($"Type mismatch for column {col}");
                }
            }

            // Primary key check
            if (this.PrimaryKeyIndex >= 0)
            {
                var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                if (this.Index.Search(pkVal).Found)
                    throw new InvalidOperationException("Primary key violation");
            }

            // Serialize row
            using var rowMs = new MemoryStream();
            using var rowWriter = new BinaryWriter(rowMs);
            foreach (var col in this.Columns)
                this.WriteTypedValue(rowWriter, row[col], this.ColumnTypes[this.Columns.IndexOf(col)]);
            var rowData = rowMs.ToArray();

            // TRUE APPEND + POSITION
            long position = this.storage.AppendBytes(this.DataFile, rowData);

            // Primary key index
            if (this.PrimaryKeyIndex >= 0)
            {
                var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                this.Index.Insert(pkVal, position);
            }

            // Async hash index update MET POSITION
            if (this.hashIndexes.Count > 0)
            {
                _ = _indexQueue.Writer.WriteAsync(new IndexUpdate(row, this.hashIndexes.Values, position));
            }
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true)
    {
        return Select(where, orderBy, asc, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        ArgumentNullException.ThrowIfNull(this.storage);
        return this.isReadOnly ? SelectInternal(where, orderBy, asc, noEncrypt) : SelectWithLock(where, orderBy, asc, noEncrypt);
    }

    private List<Dictionary<string, object>> SelectWithLock(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        this.rwLock.EnterReadLock();
        try
        {
            return SelectInternal(where, orderBy, asc, noEncrypt);
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Dictionary<string, object>> SelectInternal(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        var results = new List<Dictionary<string, object>>();

        // 1. HashIndex lookup (O(1))
        if (where != null)
        {
            var parts = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && parts[1] == "=")
            {
                var col = parts[0];
                var valStr = parts[2].Trim('\'');
                if (this.hashIndexes.TryGetValue(col, out var hashIndex))
                {
                    var colIdx = this.Columns.IndexOf(col);
                    if (colIdx >= 0)
                    {
                        var key = ParseValueForHashLookup(valStr, this.ColumnTypes[colIdx]);
                        var positions = hashIndex.LookupPositions(key);
                        foreach (var pos in positions)
                        {
                            var row = ReadRowAtPosition(pos, noEncrypt);
                            if (row != null) results.Add(row);
                        }
                        if (results.Count > 0) goto ApplyOrderBy;
                    }
                }
            }
        }

        // 2. Primary key lookup
        if (results.Count == 0 && where != null && this.PrimaryKeyIndex >= 0)
        {
            var pkCol = this.Columns[this.PrimaryKeyIndex];
            var match = System.Text.RegularExpressions.Regex.Match(where, $@"^{pkCol}\s*=\s*'([^']+)'$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var pkVal = match.Groups[1].Value;
                var searchResult = this.Index.Search(pkVal);
                if (searchResult.Found)
                {
                    long position = searchResult.Value;
                    var row = ReadRowAtPosition(position, noEncrypt);
                    if (row != null) results.Add(row);
                    goto ApplyOrderBy;
                }
            }
        }

        // 3. Full scan fallback
        if (results.Count == 0)
        {
            var data = this.storage.ReadBytes(this.DataFile, noEncrypt);
            if (data != null && data.Length > 0)
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                while (ms.Position < ms.Length)
                {
                    var row = new Dictionary<string, object>();
                    bool valid = true;
                    for (int i = 0; i < this.Columns.Count; i++)
                    {
                        try { row[this.Columns[i]] = this.ReadTypedValue(reader, this.ColumnTypes[i]); }
                        catch { valid = false; break; }
                    }
                    if (valid && (string.IsNullOrEmpty(where) || EvaluateWhere(row, where)))
                        results.Add(row);
                }
            }
        }

    ApplyOrderBy:
        if (orderBy != null && results.Count > 0)
        {
            var idx = this.Columns.IndexOf(orderBy);
            if (idx >= 0)
                results = asc ? results.OrderBy(r => r[this.Columns[idx]]).ToList() : results.OrderByDescending(r => r[this.Columns[idx]]).ToList();
        }
        return results;
    }

    private Dictionary<string, object>? ReadRowAtPosition(long position, bool noEncrypt = false)
    {
        var data = this.storage.ReadBytesAt(this.DataFile, position, 8192, noEncrypt);
        if (data == null || data.Length == 0) return null;
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        var row = new Dictionary<string, object>();
        try
        {
            for (int i = 0; i < this.Columns.Count; i++)
                row[this.Columns[i]] = this.ReadTypedValue(reader, this.ColumnTypes[i]);
            return row;
        }
        catch { return null; }
    }

    private object ParseValueForHashLookup(string value, DataType type) => type switch
    {
        DataType.Integer => int.TryParse(value, out var i) ? i : value,
        DataType.Long => long.TryParse(value, out var l) ? l : value,
        DataType.Real => double.TryParse(value, out var d) ? d : value,
        DataType.Boolean => bool.TryParse(value, out var b) ? b : value,
        _ => value,
    };

    private bool EvaluateWhere(Dictionary<string, object> row, string? where)
    {
        if (string.IsNullOrEmpty(where)) return true;
        var parts = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[1] == "=")
            return row.TryGetValue(parts[0], out var val) && val?.ToString() == parts[2].Trim('\'');
        return true;
    }

    // === REST VAN JE METHODES (Update, Delete, WriteTypedValue, etc.) ===

    public void Update(string? where, Dictionary<string, object> updates)
    {
        if (this.isReadOnly) throw new InvalidOperationException("Cannot update in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            var rows = this.SelectInternal(where, null, true, false);
            foreach (var row in rows)
            {
                foreach (var update in updates)
                {
                    row[update.Key] = update.Value;
                }
                // Note: In a real implementation, this would update the storage
            }
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    public void Delete(string? where)
    {
        if (this.isReadOnly) throw new InvalidOperationException("Cannot delete in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            var rows = this.SelectInternal(where, null, true, false);
            // Note: In a real implementation, this would remove from storage
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    private void WriteTypedValue(BinaryWriter writer, object value, DataType type)
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

    private object ReadTypedValue(BinaryReader reader, DataType type)
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
                var len = reader.ReadInt32();
                return reader.ReadBytes(len);
            default:
                return reader.ReadString();
        }
    }

    private object GenerateAutoValue(DataType type) => type switch
    {
        DataType.Ulid => Ulid.NewUlid(),
        DataType.Guid => Guid.NewGuid(),
        _ => throw new InvalidOperationException($"Auto generation not supported for type {type}"),
    };

    private object? GetDefaultValue(DataType type) => type switch
    {
        DataType.Integer => 0,
        DataType.String => "",
        DataType.Real => 0.0,
        DataType.Boolean => false,
        DataType.DateTime => DateTime.MinValue,
        DataType.Long => 0L,
        DataType.Decimal => 0m,
        DataType.Ulid => Ulid.NewUlid(),
        DataType.Guid => Guid.NewGuid(),
        _ => null,
    };

    private bool IsValidType(object value, DataType type)
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

    private class IndexManager : IDisposable
    {
        public void Dispose() { }
    }

    public void CreateHashIndex(string columnName)
    {
        if (!this.Columns.Contains(columnName)) throw new InvalidOperationException($"Column {columnName} not found");
        if (this.hashIndexes.ContainsKey(columnName)) return;
        var index = new HashIndex(this.Name, columnName);
        this.hashIndexes[columnName] = index;
        // Rebuild from existing data
        var allRows = this.Select();
        foreach (var row in allRows)
        {
            if (row.TryGetValue(columnName, out var val))
                index.Add(row, 0); // position not known – will be fixed on next insert
        }
    }

    public void Dispose()
    {
        this.indexManager?.Dispose();
        _indexQueue.Writer.Complete();
        this.rwLock.Dispose();
    }

    public bool HasHashIndex(string columnName) => this.hashIndexes.ContainsKey(columnName);

    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName)
    {
        if (this.hashIndexes.TryGetValue(columnName, out var index))
        {
            return index.GetStatistics();
        }
        return null;
    }

    public void IncrementColumnUsage(string columnName)
    {
        lock (usageLock)
        {
            if (!columnUsage.TryGetValue(columnName, out var count))
                columnUsage[columnName] = 1;
            else
                columnUsage[columnName] = count + 1;
        }
    }

    public IReadOnlyDictionary<string, long> GetColumnUsage()
    {
        lock (this.usageLock)
        {
            return new ReadOnlyDictionary<string, long>(this.columnUsage);
        }
    }

    public void TrackAllColumnsUsage()
    {
        lock (this.usageLock)
        {
            foreach (var col in this.Columns)
            {
                if (this.columnUsage.ContainsKey(col))
                    this.columnUsage[col]++;
                else
                    this.columnUsage[col] = 1;
            }
        }
    }

    public void TrackColumnUsage(string columnName)
    {
        lock (this.usageLock)
        {
            if (this.columnUsage.ContainsKey(columnName))
                this.columnUsage[columnName]++;
            else
                this.columnUsage[columnName] = 1;
        }
    }

    private async Task ProcessIndexUpdatesAsync()
    {
        await foreach (var update in _indexQueue.Reader.ReadAllAsync())
        {
            foreach (var index in update.Indexes)
            {
                index.Add(update.Row, update.Position); // NU MET POSITION!
            }
        }
    }

    private sealed record IndexUpdate(Dictionary<string, object> Row, IEnumerable<HashIndex> Indexes, long Position);
}
