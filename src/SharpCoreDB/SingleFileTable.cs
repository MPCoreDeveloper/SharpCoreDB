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
using SharpCoreDB.Storage.Hybrid;
using SharpCoreDB.Storage.Scdb;
using StorageModeHybrid = SharpCoreDB.Storage.Hybrid.StorageMode;using System;
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
public sealed class SingleFileTable(string tableName, IStorageProvider storageProvider) : ITable, ITableSchemaApplicator
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleFileTable"/> class with full schema definition
    /// including primary key, NOT NULL, and AUTOINCREMENT constraints.
    /// </summary>
    /// <param name="tableName">Table name.</param>
    /// <param name="columns">Column names.</param>
    /// <param name="columnTypes">Column data types.</param>
    /// <param name="primaryKeyIndex">Index of the primary key column (-1 if none).</param>
    /// <param name="isNotNull">NOT NULL constraint per column.</param>
    /// <param name="isAuto">AUTOINCREMENT flag per column.</param>
    /// <param name="storageProvider">Storage provider.</param>
    public SingleFileTable(string tableName, List<string> columns, List<DataType> columnTypes,
        int primaryKeyIndex, List<bool> isNotNull, List<bool> isAuto, IStorageProvider storageProvider)
        : this(tableName, storageProvider)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(columnTypes);

        Columns = columns;
        ColumnTypes = columnTypes;
        PrimaryKeyIndex = primaryKeyIndex;

        // Copy constraint lists
        IsNotNull.Clear();
        IsNotNull.AddRange(isNotNull);
        IsAuto.Clear();
        IsAuto.AddRange(isAuto);

        InitializeColumnMetadata();
    }

    /// <inheritdoc />
    public string Name { get; set; } = tableName;

    /// <inheritdoc />
    public List<string> Columns { get; set; } = [];

    /// <inheritdoc />
    public List<DataType> ColumnTypes { get; set; } = [];

    /// <inheritdoc />
    public string DataFile { get; set; } = storageProvider.RootPath;

    /// <inheritdoc />
    public int PrimaryKeyIndex { get; set; } = -1;

    /// <inheritdoc />
    public bool HasInternalRowId { get; set; }

    /// <inheritdoc />
    /// <remarks>Single-file tables store this for schema compatibility with the shared DDL path;
    /// the actual value does not affect storage engine behaviour.</remarks>
    public StorageModeHybrid StorageMode { get; set; } = StorageModeHybrid.Columnar;

    /// <inheritdoc />
    /// <remarks>Single-file tables do not use a standalone B-tree PK index;
    /// the setter is accepted but the value is unused at runtime.</remarks>
    public IIndex<string, long> Index { get; set; } = new NullIndex();

    /// <inheritdoc />
    public List<string?> DefaultExpressions { get; set; } = [];

    /// <inheritdoc />
    public List<string?> ColumnCheckExpressions { get; set; } = [];

    /// <inheritdoc />
    public List<string> TableCheckConstraints { get; set; } = [];

    /// <inheritdoc />
    public List<bool> IsAuto { get; set; } = [];

    /// <inheritdoc />
    public List<bool> IsNotNull { get; set; } = [];

    /// <inheritdoc />
    public List<object?> DefaultValues { get; set; } = [];

    /// <inheritdoc />
    public List<ForeignKeyConstraint> ForeignKeys { get; set; } = [];

    /// <inheritdoc />
    public List<List<string>> UniqueConstraints { get; set; } = [];

    /// <inheritdoc />
    public List<CollationType> ColumnCollations { get; set; } = [];

    /// <inheritdoc />
    public List<string?> ColumnLocaleNames { get; set; } = [];

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

        // Strip leading WHERE keyword if present
        var condition = where?.Trim();
        if (condition is not null && condition.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
        {
            condition = condition[6..].Trim();
        }

        List<Dictionary<string, object>> results;
        lock (_tableLock)
        {
            results = _rowCache.Select(row => new Dictionary<string, object>(row)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(condition))
        {
            results = results.Where(row => EvaluateCondition(row, condition)).ToList();
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

        // Strip leading WHERE keyword if present
        var condition = where?.Trim();
        if (condition is not null && condition.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
        {
            condition = condition[6..].Trim();
        }

        lock (_tableLock)
        {
            foreach (var row in _rowCache)
            {
                if (string.IsNullOrWhiteSpace(condition) || EvaluateCondition(row, condition))
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

        // Strip leading WHERE keyword if present
        var condition = where?.Trim();
        if (condition is not null && condition.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
        {
            condition = condition[6..].Trim();
        }

        lock (_tableLock)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                _rowCache.Clear();
            }
            else
            {
                _rowCache.RemoveAll(row => EvaluateCondition(row, condition));
            }

            _isDirty = true;
        }

        if (AutoFlush)
        {
            FlushCache();
        }
    }

    /// <inheritdoc />
    public Dictionary<string, object>? FindByPrimaryKey(object key) => null;

    /// <inheritdoc />
    public List<Dictionary<string, object>> FindByIndex(string column, object value) => [];

    /// <inheritdoc />
    public bool UpdateByPrimaryKey(object key, Dictionary<string, object> updates) => false;

    /// <inheritdoc />
    public bool DeleteByPrimaryKey(object key) => false;

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

        // Serialize to byte array to get exact length
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(serializableRows);

        // Write using WriteBlockAsync to properly track data length
        _storageProvider.WriteBlockAsync(_dataBlockName, jsonBytes).GetAwaiter().GetResult();
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
    /// <remarks>No-op for single-file tables: the storage provider handles all I/O.</remarks>
    public void InitializeStorageEngine() { }

    /// <inheritdoc />
    /// <remarks>Single-file tables have no named index registry; always returns false.</remarks>
    public bool HasIndex(string nameOrColumn) => false;

    /// <inheritdoc />
    /// <remarks>
    /// Applies a DDL-parsed schema to this single-file table.
    /// The data file path and storage-engine-specific fields (StorageMode, Index) are
    /// stored for schema completeness but do not affect runtime behaviour since
    /// the storage provider manages all persistence.
    /// </remarks>
    public void ApplySchema(TableSchemaDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Columns = schema.Columns;
        ColumnTypes = schema.ColumnTypes;
        IsAuto = schema.IsAuto;
        PrimaryKeyIndex = schema.PrimaryKeyIndex;
        HasInternalRowId = schema.HasInternalRowId;
        DataFile = schema.DataFilePath;
        StorageMode = schema.StorageMode;
        IsNotNull = schema.IsNotNull;
        DefaultValues = schema.DefaultValues;
        UniqueConstraints = schema.UniqueConstraints;
        ForeignKeys = schema.ForeignKeys;
        DefaultExpressions = schema.DefaultExpressions;
        ColumnCheckExpressions = schema.ColumnCheckExpressions;
        TableCheckConstraints = schema.TableCheckConstraints;
        ColumnCollations = schema.ColumnCollations;
        ColumnLocaleNames = schema.ColumnLocaleNames;
    }

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
    public void DropColumn(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        EnsureCacheLoaded();

        lock (_tableLock)
        {
            var idx = Columns.FindIndex(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                throw new InvalidOperationException($"Column '{columnName}' does not exist in table '{Name}'.");

            // Cannot drop the primary key column
            if (idx == PrimaryKeyIndex)
                throw new InvalidOperationException($"Cannot drop primary key column '{columnName}'.");

            // Update schema lists
            Columns.RemoveAt(idx);
            ColumnTypes.RemoveAt(idx);
            if (idx < IsAuto.Count) IsAuto.RemoveAt(idx);
            if (idx < IsNotNull.Count) IsNotNull.RemoveAt(idx);
            if (idx < DefaultValues.Count) DefaultValues.RemoveAt(idx);
            if (idx < DefaultExpressions.Count) DefaultExpressions.RemoveAt(idx);
            if (idx < ColumnCheckExpressions.Count) ColumnCheckExpressions.RemoveAt(idx);
            if (idx < ColumnCollations.Count) ColumnCollations.RemoveAt(idx);
            if (idx < ColumnLocaleNames.Count) ColumnLocaleNames.RemoveAt(idx);

            // Adjust primary key index
            if (PrimaryKeyIndex > idx)
                PrimaryKeyIndex--;

            // Remove the column from all cached rows
            foreach (var row in _rowCache)
            {
                var actualKey = row.Keys.FirstOrDefault(k => k.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                if (actualKey is not null)
                    row.Remove(actualKey);
            }

            // Remove from unique constraints
            UniqueConstraints.RemoveAll(uc => uc.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)));

            // Remove from foreign keys
            ForeignKeys.RemoveAll(fk => fk.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));

            _isDirty = true;
        }

        if (AutoFlush)
            FlushCache();
    }

    /// <inheritdoc />
    public void RenameColumn(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        EnsureCacheLoaded();

        lock (_tableLock)
        {
            var idx = Columns.FindIndex(c => c.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                throw new InvalidOperationException($"Column '{oldName}' does not exist in table '{Name}'.");

            if (Columns.Any(c => c.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Column '{newName}' already exists in table '{Name}'.");

            Columns[idx] = newName;

            // Rename key in all cached rows
            foreach (var row in _rowCache)
            {
                var actualKey = row.Keys.FirstOrDefault(k => k.Equals(oldName, StringComparison.OrdinalIgnoreCase));
                if (actualKey is not null)
                {
                    var val = row[actualKey];
                    row.Remove(actualKey);
                    row[newName] = val;
                }
            }

            // Update unique constraints
            foreach (var uc in UniqueConstraints)
            {
                for (int i = 0; i < uc.Count; i++)
                    if (uc[i].Equals(oldName, StringComparison.OrdinalIgnoreCase))
                        uc[i] = newName;
            }

            // Update foreign keys
            foreach (var fk in ForeignKeys.Where(fk => fk.ColumnName.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
                fk.ColumnName = newName;

            _isDirty = true;
        }

        if (AutoFlush)
            FlushCache();
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
            if (value is null)
            {
                result[key] = DBNull.Value;
            }
            else if (value is JsonElement element)
            {
                // Convert JsonElement to the appropriate CLR type
                result[key] = element.ValueKind switch
                {
                    JsonValueKind.Number => ConvertJsonNumber(element),
                    JsonValueKind.String => element.GetString() ?? string.Empty,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => DBNull.Value,
                    _ => value
                };
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static object ConvertJsonNumber(JsonElement element)
    {
        if (element.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (element.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        // Get as double, but if it's a whole number, convert to long or int
        var doubleValue = element.GetDouble();

        // Check if it's a whole number
        if (Math.Abs(doubleValue % 1) < double.Epsilon)
        {
            var longVal = (long)doubleValue;
            if (longVal >= int.MinValue && longVal <= int.MaxValue)
            {
                return (int)longVal;
            }
            return longVal;
        }

        return doubleValue;
    }

    /// <summary>
    /// No-op index implementation used by <see cref="SingleFileTable"/> to satisfy the
    /// <see cref="ITable.Index"/> contract. Single-file tables do not use a standalone
    /// B-tree PK index; the storage provider manages data directly.
    /// </summary>
    private sealed class NullIndex : IIndex<string, long>
    {
        /// <inheritdoc />
        public void Insert(string key, long value) { }

        /// <inheritdoc />
        public (bool Found, long Value) Search(string key) => (false, 0);

        /// <inheritdoc />
        public bool Delete(string key) => false;

        /// <inheritdoc />
        public void Clear() { }
    }
}
