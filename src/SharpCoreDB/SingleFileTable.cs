// <copyright file="SingleFileTable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Optimizations;
using SharpCoreDB.Services;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;

/// <summary>
/// Table implementation for single-file storage.
/// Uses an in-memory cache with explicit flush to the storage provider.
/// </summary>
public sealed class SingleFileTable(string tableName, IStorageProvider storageProvider) : ITable
{
    private readonly IStorageProvider _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
    private readonly Lock _tableLock = new();
    private readonly string _dataBlockName = $"table:{tableName}:data";
    private List<Dictionary<string, object>> _rowCache = [];
    private bool _cacheLoaded;
    private bool _isDirty;
    private long _nextId = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleFileTable"/> class from table metadata.
    /// </summary>
    /// <param name="tableName">Table name.</param>
    /// <param name="storageProvider">Storage provider.</param>
    /// <param name="metadata">Table metadata entry.</param>
    public SingleFileTable(string tableName, IStorageProvider storageProvider, TableMetadataEntry metadata)
        : this(tableName, storageProvider)
    {
        PrimaryKeyIndex = metadata.PrimaryKeyIndex;
        LoadSchemaFromProvider(tableName);
        InitializeColumnMetadata();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleFileTable"/> class with schema definition.
    /// </summary>
    /// <param name="tableName">Table name.</param>
    /// <param name="columns">Column names.</param>
    /// <param name="columnTypes">Column data types.</param>
    /// <param name="storageProvider">Storage provider.</param>
    public SingleFileTable(string tableName, List<string> columns, List<DataType> columnTypes, IStorageProvider storageProvider)
        : this(tableName, storageProvider)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(columnTypes);

        Columns = columns;
        ColumnTypes = columnTypes;
        InitializeColumnMetadata();
    }

    /// <inheritdoc />
    public string Name { get; set; } = tableName;

    /// <inheritdoc />
    public List<string> Columns { get; private set; } = [];

    /// <inheritdoc />
    public List<DataType> ColumnTypes { get; private set; } = [];

    /// <inheritdoc />
    public string DataFile { get; set; } = storageProvider.RootPath;

    /// <inheritdoc />
    public int PrimaryKeyIndex { get; private set; } = -1;

    /// <inheritdoc />
    public List<bool> IsAuto { get; } = [];

    /// <inheritdoc />
    public List<bool> IsNotNull { get; } = [];

    /// <inheritdoc />
    public List<object?> DefaultValues { get; } = [];

    /// <inheritdoc />
    public List<ForeignKeyConstraint> ForeignKeys { get; } = [];

    /// <inheritdoc />
    public List<List<string>> UniqueConstraints { get; } = [];

    /// <inheritdoc />
    public List<CollationType> ColumnCollations { get; } = [];

    /// <inheritdoc />
    public List<string?> ColumnLocaleNames { get; } = [];

    /// <summary>
    /// Gets or sets whether changes are automatically flushed to disk after each operation.
    /// </summary>
    public bool AutoFlush { get; set; } = true;

    /// <inheritdoc />
    public void Insert(Dictionary<string, object> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        EnsureCacheLoaded();

        lock (_tableLock)
        {
            ApplyDefaults(row);
            _rowCache.Add(row);
            _isDirty = true;
        }

        if (AutoFlush)
        {
            FlushCache();
        }
    }

    /// <inheritdoc />
    public long[] InsertBatch(List<Dictionary<string, object>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return [];

        EnsureCacheLoaded();
        var positions = new long[rows.Count];

        lock (_tableLock)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                ApplyDefaults(row);
                _rowCache.Add(row);
                positions[i] = _rowCache.Count - 1;
            }

            _isDirty = true;
        }

        if (AutoFlush)
        {
            FlushCache();
        }

        return positions;
    }

    /// <inheritdoc />
    public long[] InsertBatchFromBuffer(ReadOnlySpan<byte> encodedData, int rowCount)
    {
        if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (rowCount == 0) return [];
        if (encodedData.IsEmpty) throw new ArgumentException("Encoded data buffer is empty", nameof(encodedData));

        var decoder = new BinaryRowDecoder(Columns, ColumnTypes);
        var rows = decoder.DecodeRows(encodedData, rowCount);
        return InsertBatch(rows);
    }

    /// <inheritdoc />
    public List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true)
        => Select(where, orderBy, asc, noEncrypt: false);

    /// <inheritdoc />
    public List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        EnsureCacheLoaded();

        List<Dictionary<string, object>> results;
        lock (_tableLock)
        {
            results = _rowCache.Select(row => new Dictionary<string, object>(row)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(where))
        {
            results = results.Where(row => EvaluateCondition(row, where)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            results = asc
                ? results.OrderBy(row => row.TryGetValue(orderBy, out var value) ? value : null).ToList()
                : results.OrderByDescending(row => row.TryGetValue(orderBy, out var value) ? value : null).ToList();
        }

        return results;
    }

    /// <inheritdoc />
    public void Update(string? where, Dictionary<string, object> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        EnsureCacheLoaded();

        lock (_tableLock)
        {
            foreach (var row in _rowCache)
            {
                if (string.IsNullOrWhiteSpace(where) || EvaluateCondition(row, where))
                {
                    foreach (var update in updates)
                    {
                        row[update.Key] = update.Value;
                    }

                    _isDirty = true;
                }
            }
        }

        if (AutoFlush && _isDirty)
        {
            FlushCache();
        }
    }

    /// <summary>
    /// Executes batch updates keyed by primary key value.
    /// </summary>
    /// <param name="updates">Dictionary of primary key to update values.</param>
    public void UpdateBatch(Dictionary<object, Dictionary<string, object>> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        EnsureCacheLoaded();

        if (PrimaryKeyIndex < 0) return;

        var pkColumn = Columns[PrimaryKeyIndex];

        lock (_tableLock)
        {
            foreach (var row in _rowCache)
            {
                if (!row.TryGetValue(pkColumn, out var pkValue) || pkValue is null)
                {
                    continue;
                }

                if (!updates.TryGetValue(pkValue, out var rowUpdates))
                {
                    continue;
                }

                foreach (var update in rowUpdates)
                {
                    row[update.Key] = update.Value;
                }

                _isDirty = true;
            }
        }

        if (AutoFlush && _isDirty)
        {
            FlushCache();
        }
    }

    /// <inheritdoc />
    public void Delete(string? where)
    {
        EnsureCacheLoaded();

        lock (_tableLock)
        {
            if (string.IsNullOrWhiteSpace(where))
            {
                _rowCache.Clear();
            }
            else
            {
                _rowCache.RemoveAll(row => EvaluateCondition(row, where));
            }

            _isDirty = true;
        }

        if (AutoFlush)
        {
            FlushCache();
        }
    }

    /// <summary>
    /// Flushes the in-memory row cache to the storage provider.
    /// </summary>
    public void FlushCache()
    {
        if (!_isDirty)
        {
            return;
        }

        List<Dictionary<string, object?>> serializableRows;
        lock (_tableLock)
        {
            serializableRows = _rowCache.Select(ToSerializableRow).ToList();
            _isDirty = false;
        }

        using var stream = _storageProvider.GetWriteStream(_dataBlockName, append: false);
        JsonSerializer.Serialize(stream, serializableRows);
    }

    /// <inheritdoc />
    public void CreateHashIndex(string columnName) { }

    /// <inheritdoc />
    public void CreateHashIndex(string indexName, string columnName, bool isUnique = false) { }

    /// <inheritdoc />
    public bool HasHashIndex(string columnName) => false;

    /// <inheritdoc />
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName) => null;

    /// <inheritdoc />
    public void IncrementColumnUsage(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return;
        _columnUsage[columnName] = _columnUsage.TryGetValue(columnName, out var count) ? count + 1 : 1;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, long> GetColumnUsage() => new Dictionary<string, long>(_columnUsage);

    /// <inheritdoc />
    public void TrackAllColumnsUsage()
    {
        foreach (var column in Columns)
        {
            IncrementColumnUsage(column);
        }
    }

    /// <inheritdoc />
    public void TrackColumnUsage(string columnName) => IncrementColumnUsage(columnName);

    /// <inheritdoc />
    public bool RemoveHashIndex(string columnName) => false;

    /// <inheritdoc />
    public void ClearAllIndexes() { }

    /// <inheritdoc />
    public long GetCachedRowCount() => _rowCache.Count;

    /// <inheritdoc />
    public void RefreshRowCount() { }

    /// <inheritdoc />
    public void CreateBTreeIndex(string columnName) { }

    /// <inheritdoc />
    public void CreateBTreeIndex(string indexName, string columnName, bool isUnique = false) { }

    /// <inheritdoc />
    public bool HasBTreeIndex(string columnName) => false;

    /// <inheritdoc />
    public bool RemoveBTreeIndex(string columnName) => false;

    /// <inheritdoc />
    public void SetDatabase(Database database) { }

    private readonly Dictionary<string, long> _columnUsage = new(StringComparer.OrdinalIgnoreCase);

    private void EnsureCacheLoaded()
    {
        if (_cacheLoaded)
        {
            return;
        }

        lock (_tableLock)
        {
            if (_cacheLoaded)
            {
                return;
            }

            using var stream = _storageProvider.GetReadStream(_dataBlockName);
            if (stream is null)
            {
                _rowCache = [];
                _cacheLoaded = true;
                return;
            }

            var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(stream);
            _rowCache = rows?.Select(FromSerializableRow).ToList() ?? [];
            _cacheLoaded = true;
        }
    }

    private void LoadSchemaFromProvider(string tableName)
    {
        if (_storageProvider is not SingleFileStorageProvider provider)
        {
            return;
        }

        var columnDefs = provider.TableDirectoryManager.GetColumnDefinitions(tableName);
        var columns = new List<string>(columnDefs.Count);
        var types = new List<DataType>(columnDefs.Count);

        foreach (var entry in columnDefs)
        {
            columns.Add(GetColumnName(entry));
            types.Add((DataType)entry.DataType);
        }

        Columns = columns;
        ColumnTypes = types;
    }

    private void InitializeColumnMetadata()
    {
        IsAuto.Clear();
        IsNotNull.Clear();
        DefaultValues.Clear();
        ColumnCollations.Clear();
        ColumnLocaleNames.Clear();

        for (int i = 0; i < Columns.Count; i++)
        {
            IsAuto.Add(false);
            IsNotNull.Add(false);
            DefaultValues.Add(null);
            ColumnCollations.Add(CollationType.Binary);
            ColumnLocaleNames.Add(null);
        }
    }

    private void ApplyDefaults(Dictionary<string, object> row)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            var col = Columns[i];
            if (!row.ContainsKey(col))
            {
                if (IsAuto.Count > i && IsAuto[i])
                {
                    row[col] = GenerateAutoValue(ColumnTypes[i]);
                }
                else if (DefaultValues.Count > i)
                {
                    row[col] = DefaultValues[i] ?? DBNull.Value;
                }
                else
                {
                    row[col] = DBNull.Value;
                }
            }
        }
    }

    private object GenerateAutoValue(DataType type)
    {
        var nextValue = _nextId++;
        return type switch
        {
            DataType.Integer => (int)nextValue,
            DataType.Long => nextValue,
            _ => nextValue
        };
    }

    /// <inheritdoc />
    public void Flush() => FlushCache();

    /// <inheritdoc />
    public void AddColumn(ColumnDefinition columnDef)
    {
        ArgumentNullException.ThrowIfNull(columnDef);

        var dataType = ParseDataType(columnDef.DataType);

        Columns.Add(columnDef.Name);
        ColumnTypes.Add(dataType);
        IsAuto.Add(columnDef.IsAutoIncrement);
        IsNotNull.Add(columnDef.IsNotNull);
        DefaultValues.Add(columnDef.DefaultValue);
        ColumnCollations.Add(columnDef.Collation);
        ColumnLocaleNames.Add(columnDef.LocaleName);

        if (columnDef.IsPrimaryKey)
        {
            PrimaryKeyIndex = Columns.Count - 1;
        }

        if (columnDef.IsUnique)
        {
            UniqueConstraints.Add([columnDef.Name]);
        }

        _isDirty = true;

        if (AutoFlush)
        {
            FlushCache();
        }
    }

    /// <inheritdoc />
    public void SetMetadata(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _metadata[key] = value;
    }

    /// <inheritdoc />
    public object? GetMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _metadata.TryGetValue(key, out var value) ? value : null;
    }

    /// <inheritdoc />
    public bool RemoveMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _metadata.Remove(key);
    }

    private readonly Dictionary<string, object> _metadata = new(StringComparer.OrdinalIgnoreCase);

    private static DataType ParseDataType(string typeName)
        => typeName.ToUpperInvariant() switch
        {
            "INT" or "INTEGER" => DataType.Integer,
            "LONG" or "BIGINT" => DataType.Long,
            "REAL" or "FLOAT" or "DOUBLE" => DataType.Real,
            "DECIMAL" or "NUMERIC" => DataType.Decimal,
            "DATETIME" or "DATE" => DataType.DateTime,
            "BOOL" or "BOOLEAN" => DataType.Boolean,
            "BLOB" => DataType.Blob,
            "GUID" => DataType.Guid,
            "ULID" => DataType.Ulid,
            _ => DataType.String
        };

    private static string GetColumnName(ColumnDefinitionEntry entry)
    {
        unsafe
        {
            ref var start = ref entry.ColumnName[0];
            var span = MemoryMarshal.CreateReadOnlySpan(ref start, ColumnDefinitionEntry.MAX_COLUMN_NAME_LENGTH + 1);
            var nullIndex = span.IndexOf((byte)0);
            if (nullIndex >= 0)
            {
                span = span[..nullIndex];
            }

            return Encoding.UTF8.GetString(span);
        }
    }

    private static bool EvaluateCondition(Dictionary<string, object> row, string condition)
    {
        var parts = condition.Split([" AND ", " and "], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (!EvaluateSingleCondition(row, part.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateSingleCondition(Dictionary<string, object> row, string condition)
    {
        var operators = new[] { ">=", "<=", "!=", "<>", "=", ">", "<" };
        string? op = null;
        int opIndex = -1;

        foreach (var testOp in operators)
        {
            opIndex = condition.IndexOf(testOp, StringComparison.Ordinal);
            if (opIndex >= 0)
            {
                op = testOp;
                break;
            }
        }

        if (op == null || opIndex < 0)
        {
            return true;
        }

        var columnName = condition[..opIndex].Trim();
        var valueStr = condition[(opIndex + op.Length)..].Trim();

        if (!row.TryGetValue(columnName, out var rowValue))
        {
            return false;
        }

        if ((valueStr.StartsWith('\'') && valueStr.EndsWith('\'')) ||
            (valueStr.StartsWith('"') && valueStr.EndsWith('"')))
        {
            valueStr = valueStr[1..^1];
        }

        if (rowValue is int intVal && int.TryParse(valueStr, out var intCompare))
        {
            return op switch
            {
                "=" => intVal == intCompare,
                "!=" or "<>" => intVal != intCompare,
                ">" => intVal > intCompare,
                "<" => intVal < intCompare,
                ">=" => intVal >= intCompare,
                "<=" => intVal <= intCompare,
                _ => true
            };
        }

        if (rowValue is long longVal && long.TryParse(valueStr, out var longCompare))
        {
            return op switch
            {
                "=" => longVal == longCompare,
                "!=" or "<>" => longVal != longCompare,
                ">" => longVal > longCompare,
                "<" => longVal < longCompare,
                ">=" => longVal >= longCompare,
                "<=" => longVal <= longCompare,
                _ => true
            };
        }

        if (rowValue is decimal decVal && decimal.TryParse(valueStr, out var decCompare))
        {
            return op switch
            {
                "=" => decVal == decCompare,
                "!=" or "<>" => decVal != decCompare,
                ">" => decVal > decCompare,
                "<" => decVal < decCompare,
                ">=" => decVal >= decCompare,
                "<=" => decVal <= decCompare,
                _ => true
            };
        }

        var comparison = string.Compare(rowValue.ToString(), valueStr, StringComparison.Ordinal);
        return op switch
        {
            "=" => comparison == 0,
            "!=" or "<>" => comparison != 0,
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            _ => true
        };
    }

    private static Dictionary<string, object?> ToSerializableRow(Dictionary<string, object> row)
    {
        var result = new Dictionary<string, object?>(row.Count);
        foreach (var (key, value) in row)
        {
            result[key] = value == DBNull.Value ? null : value;
        }

        return result;
    }

    private static Dictionary<string, object> FromSerializableRow(Dictionary<string, object?> row)
    {
        var result = new Dictionary<string, object>(row.Count);
        foreach (var (key, value) in row)
        {
            result[key] = value ?? DBNull.Value;
        }

        return result;
    }
}
