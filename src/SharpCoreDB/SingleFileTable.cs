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
using System.Text.Json.Serialization;
using System.Threading;
using System.Runtime.InteropServices;

/// <summary>
/// Table implementation for single-file storage.
/// Uses an in-memory cache with explicit flush to the storage provider.
/// ✅ CRITICAL FIX: Transaction-aware cache to support proper rollback semantics.
/// </summary>
public sealed class SingleFileTable(string tableName, IStorageProvider storageProvider) : ITable, ITableSchemaApplicator
{
    /// <summary>
    /// AOT-safe JSON options for the row cache: source-generated resolver plus the
    /// polymorphic object converter (issue #343 / single-file support under Native AOT).
    /// Produces the exact same JSON as the previous reflection-based serializer.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = SingleFileTableJsonContext.Default,
        };
        options.Converters.Add(PolymorphicObjectConverter.Instance);
        return options;
    }

    private readonly IStorageProvider _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
    private readonly DatabaseConfig? _config;
    private readonly Lock _tableLock = new();
    private readonly string _dataBlockName = $"table:{tableName}:data";
    private List<Dictionary<string, object>> _rowCache = [];
    private bool _cacheLoaded;

    // Issue A1: primary-key hash index for O(1) point lookups (FindByPrimaryKey /
    // SELECT … WHERE pk = value / UpdateByPrimaryKey / DeleteByPrimaryKey). Keyed by the ordinal
    // string form of the PK column value (the same comparison FindByPrimaryKey already used).
    // Maintained incrementally on every row mutation and rebuilt on cache load / rollback.
    private readonly Dictionary<string, List<Dictionary<string, object>>> _pkIndex = new(StringComparer.Ordinal);

    // Comparison operators in precedence order for simple-condition fast-path parsing
    // (must match EvaluateSingleCondition's ordering: >= before >, etc.).
    private static readonly string[] SingleFileConditionOperators = [">=", "<=", "!=", "<>", "=", ">", "<"];

    private bool _isDirty;
    private long _nextId = 1;

    // ✅ Transaction-aware cache snapshot for rollback support
    private List<Dictionary<string, object>>? _transactionSnapshot;
    private bool _isInTransaction;

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
        // ✅ REMOVED: InitializeColumnMetadata() — LoadSchemaFromProvider now handles IsAuto/IsNotNull
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleFileTable"/> class with database configuration.
    /// The config is used for type-mapping behavior (e.g. <see cref="DatabaseConfig.UseSqliteIntegerAffinity"/>)
    /// when columns are added via ALTER TABLE in single-file mode.
    /// </summary>
    /// <param name="tableName">Table name.</param>
    /// <param name="storageProvider">Storage provider.</param>
    /// <param name="config">Optional database configuration.</param>
    public SingleFileTable(string tableName, IStorageProvider storageProvider, DatabaseConfig? config)
        : this(tableName, storageProvider)
    {
        _config = config;
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
            IndexRow(row);
            _isDirty = true;
        }

        // ✅ CRITICAL FIX: Only flush if not in transaction
        if (AutoFlush && !_isInTransaction)
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
                IndexRow(row);
                positions[i] = _rowCache.Count - 1;
            }

            _isDirty = true;
        }

        // ✅ CRITICAL FIX: Only flush if not in transaction
        if (AutoFlush && !_isInTransaction)
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
            // Issue A1 fast path: an exact `pk = value` equality resolves through the primary-key
            // hash index (O(1)) instead of a full cache scan. Candidates are still verified with
            // the full predicate so semantics are identical to the scan path.
            if (IsPkIndexLookupSafe() && TryParsePkEquality(condition, out var pkValue) && pkValue is not null)
            {
                results = _pkIndex.TryGetValue(pkValue, out var candidates)
                    ? candidates.Where(row => EvaluateCondition(row, condition))
                                .Select(row => new Dictionary<string, object>(row)).ToList()
                    : [];

                if (!string.IsNullOrWhiteSpace(orderBy))
                {
                    results = asc
                        ? [.. results.OrderBy(row => row.TryGetValue(orderBy, out var value) ? value : null)]
                        : [.. results.OrderByDescending(row => row.TryGetValue(orderBy, out var value) ? value : null)];
                }

                return results;
            }

            // PERF: evaluate WHERE/ORDER BY against the cached rows (read-only) and
            // materialize (defensive-copy) only the surviving rows. Previously every
            // row was copied up-front, so a point lookup on a large cache copied the
            // whole table before filtering (O(N) dictionary allocations per query).
            IEnumerable<Dictionary<string, object>> source = _rowCache;
            if (!string.IsNullOrWhiteSpace(condition))
            {
                // Fast path: a simple "col op value" condition is parsed ONCE and
                // evaluated per row without per-row regex/IN/AND/OR parsing.
                var fastPredicate = TryCreateSimpleConditionPredicate(condition);
                source = fastPredicate is not null
                    ? _rowCache.Where(fastPredicate)
                    : _rowCache.Where(row => EvaluateCondition(row, condition));
            }

            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                source = asc
                    ? source.OrderBy(row => row.TryGetValue(orderBy, out var value) ? value : null)
                    : source.OrderByDescending(row => row.TryGetValue(orderBy, out var value) ? value : null);
            }

            results = source.Select(row => new Dictionary<string, object>(row)).ToList();
        }

        return results;
    }

    /// <inheritdoc />
    public void Update(string? where, Dictionary<string, object> updates) => UpdateAffectedCount(where, updates);

    /// <inheritdoc />
    public int UpdateAffectedCount(string? where, Dictionary<string, object> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        EnsureCacheLoaded();

        // Strip leading WHERE keyword if present
        var condition = where?.Trim();
        if (condition is not null && condition.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
        {
            condition = condition[6..].Trim();
        }

        bool updatesTouchPk = PrimaryKeyIndex >= 0 &&
            updates.Keys.Any(k => string.Equals(k, Columns[PrimaryKeyIndex], StringComparison.OrdinalIgnoreCase));

        int affected = 0;
        lock (_tableLock)
        {
            foreach (var row in _rowCache)
            {
                if (string.IsNullOrWhiteSpace(condition) || EvaluateCondition(row, condition))
                {
                    string? oldPkKey = updatesTouchPk ? GetPkKey(row) : null;

                    foreach (var update in updates)
                    {
                        row[update.Key] = update.Value;
                    }

                    if (oldPkKey is not null)
                    {
                        var newPkKey = GetPkKey(row);
                        if (!string.Equals(oldPkKey, newPkKey, StringComparison.Ordinal))
                        {
                            UnindexRow(row, oldPkKey);
                            IndexRow(row);
                        }
                    }

                    _isDirty = true;
                    affected++;
                }
            }
        }

        // ✅ CRITICAL FIX: Only flush if not in transaction
        if (AutoFlush && _isDirty && !_isInTransaction)
        {
            FlushCache();
        }

        return affected;
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
        bool updatesTouchPk = updates.Keys.Any(k => string.Equals(k?.ToString(), pkColumn, StringComparison.OrdinalIgnoreCase));

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

                string? oldPkKey = updatesTouchPk ? GetPkKey(row) : null;

                foreach (var update in rowUpdates)
                {
                    row[update.Key] = update.Value;
                }

                if (oldPkKey is not null)
                {
                    var newPkKey = GetPkKey(row);
                    if (!string.Equals(oldPkKey, newPkKey, StringComparison.Ordinal))
                    {
                        UnindexRow(row, oldPkKey);
                        IndexRow(row);
                    }
                }

                _isDirty = true;
            }
        }

        // ✅ CRITICAL FIX: Only flush if not in transaction
        if (AutoFlush && _isDirty && !_isInTransaction)
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
                _pkIndex.Clear();
            }
            else
            {
                // Remove matching rows while keeping the primary-key index in sync
                // (reverse iteration avoids index-shift issues).
                for (int i = _rowCache.Count - 1; i >= 0; i--)
                {
                    var row = _rowCache[i];
                    if (EvaluateCondition(row, condition))
                    {
                        UnindexRow(row);
                        _rowCache.RemoveAt(i);
                    }
                }
            }

            _isDirty = true;
        }

        // ✅ CRITICAL FIX: Only flush if not in transaction
        if (AutoFlush && !_isInTransaction)
        {
            FlushCache();
        }
    }

    /// <inheritdoc />
    public List<Dictionary<string, object>> DeleteAffectedRows(string? where)
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
            List<Dictionary<string, object>> toDelete;
            if (string.IsNullOrWhiteSpace(condition))
            {
                toDelete = [.. _rowCache];
            }
            else
            {
                toDelete = _rowCache.Where(row => EvaluateCondition(row, condition)).ToList();
            }

            if (toDelete.Count > 0)
            {
                foreach (var row in toDelete)
                {
                    UnindexRow(row);
                    _rowCache.Remove(row);
                }

                _isDirty = true;
            }

            // ✅ CRITICAL FIX: Only flush if not in transaction
            if (AutoFlush && _isDirty && !_isInTransaction)
            {
                FlushCache();
            }

            return toDelete;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// ✅ FIX (Known Issue 3): Point lookups now work in single-file mode via the in-memory
    /// row cache. The cache is transactional (rollback restores the pre-transaction snapshot),
    /// so lookups observe committed state consistently.
    /// </remarks>
    public Dictionary<string, object>? FindByPrimaryKey(object key)
    {
        EnsureCacheLoaded();
        if (PrimaryKeyIndex < 0)
        {
            return null;
        }

        var keyStr = key?.ToString();

        lock (_tableLock)
        {
            // Issue A1: O(1) primary-key hash index (was an O(N) cache scan).
            if (keyStr is not null && _pkIndex.TryGetValue(keyStr, out var rows) && rows.Count > 0)
            {
                return new Dictionary<string, object>(rows[0]);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public List<Dictionary<string, object>> FindByIndex(string column, object value) => [];

    /// <inheritdoc />
    /// <remarks>
    /// ✅ FIX (Known Issue 3): Update by primary key now works in single-file mode.
    /// The primary key value itself is not re-validated after update; if the caller changes
    /// the PK column, the row becomes addressable only by the new value.
    /// </remarks>
    public bool UpdateByPrimaryKey(object key, Dictionary<string, object> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        EnsureCacheLoaded();
        if (PrimaryKeyIndex < 0)
        {
            return false;
        }

        var keyStr = key?.ToString();
        bool found = false;

        lock (_tableLock)
        {
            // Issue A1: O(1) primary-key hash index (was an O(N) cache scan).
            if (keyStr is not null && _pkIndex.TryGetValue(keyStr, out var rows) && rows.Count > 0)
            {
                var row = rows[0];
                string? oldPkKey = GetPkKey(row);

                foreach (var update in updates)
                {
                    row[update.Key] = update.Value;
                }

                if (oldPkKey is not null)
                {
                    var newPkKey = GetPkKey(row);
                    if (!string.Equals(oldPkKey, newPkKey, StringComparison.Ordinal))
                    {
                        UnindexRow(row, oldPkKey);
                        IndexRow(row);
                    }
                }

                _isDirty = true;
                found = true;
            }
        }

        // ✅ CRITICAL FIX: Only flush if not in transaction (matches Update/Delete semantics)
        if (found && AutoFlush && !_isInTransaction)
        {
            FlushCache();
        }

        return found;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ✅ FIX (Known Issue 3): Delete by primary key now works in single-file mode.
    /// </remarks>
    public bool DeleteByPrimaryKey(object key)
    {
        EnsureCacheLoaded();
        if (PrimaryKeyIndex < 0)
        {
            return false;
        }

        var keyStr = key?.ToString();
        bool found = false;

        lock (_tableLock)
        {
            // Issue A1: O(1) primary-key hash index (was an O(N) cache scan).
            if (keyStr is not null && _pkIndex.TryGetValue(keyStr, out var rows) && rows.Count > 0)
            {
                var row = rows[0];
                UnindexRow(row, keyStr);
                _rowCache.Remove(row);
                _isDirty = true;
                found = true;
            }
        }

        // ✅ CRITICAL FIX: Only flush if not in transaction (matches Delete semantics)
        if (found && AutoFlush && !_isInTransaction)
        {
            FlushCache();
        }

        return found;
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

        // Serialize to byte array to get exact length
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(serializableRows, JsonOptions);

        // Write using WriteBlockAsync to properly track data length
        _storageProvider.WriteBlockAsync(_dataBlockName, jsonBytes).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Forces a reload of the row cache from storage, discarding any in-memory state.
    /// This is used after transaction commit/rollback to ensure queries see the persisted state.
    /// </summary>
    public void ReloadFromStorage()
    {
        lock (_tableLock)
        {
            _cacheLoaded = false;
            _rowCache.Clear();
            _isDirty = false;
            EnsureCacheLoaded();
        }
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

            // Read through ReadBlockAsync so encryption AND block-level compression (#344)
            // are both transparently handled. GetReadStream returns the raw on-disk bytes
            // when encryption is off, which would hand compressed data (Brotli/GZip marker
            // bytes) to the JSON parser on reopen — breaking SELECT after reopen.
            var jsonBytes = _storageProvider.ReadBlockAsync(_dataBlockName, CancellationToken.None).GetAwaiter().GetResult();
            if (jsonBytes is null || jsonBytes.Length == 0)
            {
                _rowCache = [];
                _cacheLoaded = true;
                RebuildPkIndex();
                return;
            }

            // Trim trailing null bytes
            var endIndex = jsonBytes.Length;
            while (endIndex > 0 && jsonBytes[endIndex - 1] == 0)
            {
                endIndex--;
            }
            
            if (endIndex == 0)
            {
                _rowCache = [];
                _cacheLoaded = true;
                RebuildPkIndex();
                return;
            }
            
            var trimmedJsonBytes = jsonBytes.AsSpan(0, endIndex);
            var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(trimmedJsonBytes, JsonOptions);
            _rowCache = rows?.Select(FromSerializableRow).ToList() ?? [];
            _cacheLoaded = true;
            RebuildPkIndex();
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
        var isAuto = new List<bool>(columnDefs.Count);
        var isNotNull = new List<bool>(columnDefs.Count);

        foreach (var entry in columnDefs)
        {
            columns.Add(GetColumnName(entry));
            types.Add((DataType)entry.DataType);
            isAuto.Add((entry.Flags & (uint)ColumnFlags.AutoIncrement) != 0);
            isNotNull.Add((entry.Flags & (uint)ColumnFlags.NotNull) != 0);

            if ((entry.Flags & (uint)ColumnFlags.PrimaryKey) != 0)
            {
                PrimaryKeyIndex = columns.Count - 1;
            }
        }

        Columns = columns;
        ColumnTypes = types;
        IsAuto = isAuto;
        IsNotNull = isNotNull;
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

    /// <summary>
    /// ✅ CRITICAL FIX: Begins a table-level transaction by creating a snapshot of the current cache.
    /// This allows rollback to restore the pre-transaction state.
    /// </summary>
    internal void BeginTransaction()
    {
        lock (_tableLock)
        {
            if (_isInTransaction)
            {
                throw new InvalidOperationException($"Table {Name} is already in a transaction");
            }

            EnsureCacheLoaded();
            // Deep copy the cache so rollback can restore the exact state
            _transactionSnapshot = _rowCache.Select(row => new Dictionary<string, object>(row)).ToList();
            _isInTransaction = true;
        }
    }

    /// <summary>
    /// ✅ CRITICAL FIX: Commits the transaction by flushing changes to storage and clearing the snapshot.
    /// </summary>
    internal void CommitTransaction()
    {
        lock (_tableLock)
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException($"Table {Name} is not in a transaction");
            }

            // Flush all pending changes to storage
            if (_isDirty)
            {
                FlushCache();
            }

            _transactionSnapshot = null;
            _isInTransaction = false;
        }
    }

    /// <summary>
    /// ✅ CRITICAL FIX: Rolls back the transaction by restoring the cache from the snapshot.
    /// </summary>
    internal void RollbackTransaction()
    {
        lock (_tableLock)
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException($"Table {Name} is not in a transaction");
            }

            // Restore the cache to the snapshot state
            if (_transactionSnapshot is not null)
            {
                _rowCache = _transactionSnapshot.Select(row => new Dictionary<string, object>(row)).ToList();
                RebuildPkIndex();
            }

            _transactionSnapshot = null;
            _isInTransaction = false;
            _isDirty = false; // Clear dirty flag since we discarded changes
        }
    }

    /// <summary>Returns the ordinal string key for a row's primary key value, or null when the
    /// table has no PK / the value is null.</summary>
    private string? GetPkKey(Dictionary<string, object> row)
    {
        if (PrimaryKeyIndex < 0 || PrimaryKeyIndex >= Columns.Count)
            return null;

        if (row.TryGetValue(Columns[PrimaryKeyIndex], out var pkValue) && pkValue is not null)
        {
            return pkValue.ToString();
        }

        return null;
    }

    /// <summary>Adds a row to the primary-key index (no-op without a PK or null PK).</summary>
    private void IndexRow(Dictionary<string, object> row)
    {
        var key = GetPkKey(row);
        if (key is null)
            return;

        if (!_pkIndex.TryGetValue(key, out var list))
        {
            list = new List<Dictionary<string, object>>(1);
            _pkIndex[key] = list;
        }

        list.Add(row);
    }

    /// <summary>Removes a row from the primary-key index. <paramref name="key"/> is the key to
    /// remove under (defaults to the row's current PK key) — pass the OLD key when the row's PK
    /// value has already been changed.</summary>
    private void UnindexRow(Dictionary<string, object> row, string? key = null)
    {
        key ??= GetPkKey(row);
        if (key is null)
            return;

        if (_pkIndex.TryGetValue(key, out var list))
        {
            list.Remove(row);
            if (list.Count == 0)
            {
                _pkIndex.Remove(key);
            }
        }
    }

    /// <summary>Rebuilds the primary-key index from the current row cache (cache load / rollback).</summary>
    private void RebuildPkIndex()
    {
        _pkIndex.Clear();
        foreach (var row in _rowCache)
        {
            IndexRow(row);
        }
    }

    /// <summary>
    /// Tries to parse an exact <c>pk = value</c> equality. Returns false for compound / range /
    /// special-syntax conditions (the caller falls back to the full scan).
    /// </summary>
    private bool TryParsePkEquality(string condition, out string? value)
    {
        value = null;
        if (PrimaryKeyIndex < 0 || string.IsNullOrWhiteSpace(condition))
            return false;

        var trimmed = condition.Trim();
        if (trimmed.Contains(" AND ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(" OR ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(" IN ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("LIKE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("BETWEEN", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(" IS ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int eq = trimmed.IndexOf('=');
        if (eq <= 0 || trimmed.IndexOf('=', eq + 1) >= 0)
            return false;

        var col = trimmed[..eq].Trim().Trim('"', '[', ']', '`');
        if (!string.Equals(col, Columns[PrimaryKeyIndex], StringComparison.OrdinalIgnoreCase))
            return false;

        var val = trimmed[(eq + 1)..].Trim();
        if (val.Length == 0)
            return false;

        if ((val.StartsWith('\'') && val.EndsWith('\'')) ||
            (val.StartsWith('"') && val.EndsWith('"')))
        {
            val = val[1..^1];
        }

        // Normalize numeric literals to the row's canonical ToString() form so `pk = 05`
        // resolves the same index key ("5") as `pk = 5` — matching the numeric comparison the
        // typed WHERE predicate performs.
        switch (ColumnTypes[PrimaryKeyIndex])
        {
            case DataType.Integer when int.TryParse(val, out var intVal):
                val = intVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case DataType.Long when long.TryParse(val, out var longVal):
                val = longVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
        }

        value = val;
        return true;
    }

    /// <summary>
    /// Whether the PK column's string form is a stable, canonical representation that can be
    /// compared against SQL literals for index lookups. String, Integer, Long, Boolean, Guid and
    /// Ulid are canonical; DateTime / Real / Decimal / Blob are not (culture / format), so those
    /// fall back to the full scan.
    /// </summary>
    private bool IsPkIndexLookupSafe()
    {
        if (PrimaryKeyIndex < 0 || PrimaryKeyIndex >= ColumnTypes.Count)
            return false;

        return ColumnTypes[PrimaryKeyIndex] is DataType.String or DataType.Integer or DataType.Long
            or DataType.Boolean or DataType.Guid or DataType.Ulid;
    }

    private void ApplyDefaults(Dictionary<string, object> row)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            var col = Columns[i];
            if (!row.ContainsKey(col))
            {
                // Check IsAuto flag, with fallback to PrimaryKeyIndex for AUTO PK columns
                bool shouldAuto = (IsAuto.Count > i && IsAuto[i]) ||
                                  (i == PrimaryKeyIndex && PrimaryKeyIndex >= 0);

                if (shouldAuto)
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

    private DataType ParseDataType(string typeName)
    {
        var upper = typeName.ToUpperInvariant();

        // ✅ FIX (Known Issue 6): SQLite integer affinity (opt-in).
        // "INT" is a SQL alias for "INTEGER" so both honor the flag.
        var useSqliteAffinity = _config?.UseSqliteIntegerAffinity ?? false;

        return upper switch
        {
            "INT" or "INTEGER" => useSqliteAffinity ? DataType.Long : DataType.Integer,
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
    }

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

    /// <summary>
    /// Normalizes a column reference to the bare row-key form: strips alias qualifiers
    /// (e.g. b.Url to Url) and identifier quotes (", [, ], `).
    /// </summary>
    private static string NormalizeColumnName(string columnName)
    {
        var dotIndex = columnName.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < columnName.Length - 1)
        {
            columnName = columnName[(dotIndex + 1)..];
        }

        return columnName.Trim('"', '[', ']', '`');
    }

    /// <summary>
    /// Fast-path predicate factory for a simple "col op value" WHERE condition.
    /// Parses the column/operator/value once per query and evaluates per row with the
    /// exact same type cascade as <see cref="EvaluateSingleCondition"/> (int → long →
    /// decimal → ordinal string compare), but WITHOUT the per-row regex (IS NULL / LIKE /
    /// BETWEEN), IN-predicate and AND/OR parsing that dominate single-file SELECT
    /// allocations. Returns null for anything that is not a simple comparison, in which
    /// case the caller falls back to the full <see cref="EvaluateCondition"/>.
    /// </summary>
    private static Func<Dictionary<string, object>, bool>? TryCreateSimpleConditionPredicate(string condition)
    {
        var trimmed = condition.Trim();

        // ✅ Issue #348: strip redundant outer parentheses so "(a = 1)" is parsed with the
        // bare column name ("a") instead of a malformed one ("(a").
        trimmed = SqlInPredicate.StripOuterParentheses(trimmed);

        // Conservative eligibility: reject any condition that the full evaluator handles
        // with dedicated syntax (AND/OR chains, IN lists, LIKE, BETWEEN, IS [NOT] NULL).
        // Rejecting is always safe — the fallback preserves existing behavior.
        if (trimmed.Contains(" AND ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(" OR ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(" IN ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("LIKE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("BETWEEN", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(" IS ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Same operator order as EvaluateSingleCondition (>= before >, etc.).
        string? op = null;
        int opIndex = -1;
        foreach (var testOp in SingleFileConditionOperators)
        {
            opIndex = trimmed.IndexOf(testOp, StringComparison.Ordinal);
            if (opIndex >= 0)
            {
                op = testOp;
                break;
            }
        }

        if (op is null || opIndex <= 0)
        {
            return null;
        }

        var columnName = trimmed[..opIndex].Trim();
        var dotIndex = columnName.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < columnName.Length - 1)
        {
            columnName = columnName[(dotIndex + 1)..].Trim('"', '[', ']', '`');
        }

        if (columnName.Length == 0)
        {
            return null;
        }

        var valueStr = trimmed[(opIndex + op.Length)..].Trim();
        if ((valueStr.StartsWith('\'') && valueStr.EndsWith('\'')) ||
            (valueStr.StartsWith('"') && valueStr.EndsWith('"')))
        {
            valueStr = valueStr[1..^1];
        }

        if (valueStr.Length == 0)
        {
            return null;
        }

        var column = columnName;
        var value = valueStr;
        var operatorStr = op;

        return row =>
        {
            if (!row.TryGetValue(column, out var rowValue) || rowValue is null or DBNull)
            {
                return false;
            }

            // Exact same type cascade as EvaluateSingleCondition.
            if (rowValue is int intVal && int.TryParse(value, out var intCompare))
            {
                return operatorStr switch
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

            if (rowValue is long longVal && long.TryParse(value, out var longCompare))
            {
                return operatorStr switch
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

            if (rowValue is decimal decVal && decimal.TryParse(value, out var decCompare))
            {
                return operatorStr switch
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

            var comparison = string.Compare(rowValue.ToString(), value, StringComparison.Ordinal);
            return operatorStr switch
            {
                "=" => comparison == 0,
                "!=" or "<>" => comparison != 0,
                ">" => comparison > 0,
                "<" => comparison < 0,
                ">=" => comparison >= 0,
                "<=" => comparison <= 0,
                _ => true
            };
        };
    }


    private static bool EvaluateCondition(Dictionary<string, object> row, string condition)
    {
        var trimmedCondition = condition.Trim();

        // ✅ Issue #348: strip redundant outer parentheses so "(a = 1 OR b = 2)" is split on
        // OR like the unparenthesized form instead of being treated as one condition with a
        // malformed column name ("(a").
        trimmedCondition = SqlInPredicate.StripOuterParentheses(trimmedCondition);

        // ✅ Parity: BETWEEN contains " AND " as part of its syntax; don't split on it.
        if (trimmedCondition.Contains("BETWEEN", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateSingleCondition(row, trimmedCondition);
        }

        // ✅ Issue #348: handle OR chains (e.g. col = @p0 OR col = @p1). Split on top-level
        // OR first — any matching branch makes the whole condition true.
        var orParts = SqlInPredicate.SplitTopLevelLogical(trimmedCondition, "OR");
        if (orParts.Count > 1)
        {
            return orParts.Any(part => EvaluateCondition(row, part));
        }

        var parts = SqlInPredicate.SplitTopLevelLogical(trimmedCondition, "AND");
        if (parts.Count > 1)
        {
            return parts.All(part => EvaluateCondition(row, part));
        }

        return EvaluateSingleCondition(row, trimmedCondition);
    }

    private static bool EvaluateSingleCondition(Dictionary<string, object> row, string condition)
    {
        var trimmed = condition.Trim();

        // ✅ Parity: support IS NULL / IS NOT NULL (previously ignored → all rows returned)
        var isNullMatch = System.Text.RegularExpressions.Regex.Match(
            trimmed, @"^([A-Za-z_]\w*)\s+IS\s+(NOT\s+)?NULL$", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (isNullMatch.Success)
        {
            var col = NormalizeColumnName(isNullMatch.Groups[1].Value);
            var negated = isNullMatch.Groups[2].Success;
            var isNull = !row.TryGetValue(col, out var v) || v is null or DBNull;
            return negated ? !isNull : isNull;
        }

        // ✅ Parity: support LIKE (previously ignored)
        var likeMatch = System.Text.RegularExpressions.Regex.Match(
            trimmed, @"^([A-Za-z_]\w*)\s+LIKE\s+'([^']*)'$", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (likeMatch.Success)
        {
            var col = NormalizeColumnName(likeMatch.Groups[1].Value);
            if (!row.TryGetValue(col, out var v) || v is null or DBNull)
            {
                return false;
            }

            var pattern = likeMatch.Groups[2].Value;
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("%", ".*").Replace("_", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                v.ToString() ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }

        // ✅ Parity: support BETWEEN (previously ignored)
        var betweenMatch = System.Text.RegularExpressions.Regex.Match(
            trimmed, @"^([A-Za-z_]\w*)\s+BETWEEN\s+([^\s]+)\s+AND\s+([^\s]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (betweenMatch.Success)
        {
            var col = NormalizeColumnName(betweenMatch.Groups[1].Value);
            var lowStr = betweenMatch.Groups[2].Value.Trim('\'', '"');
            var highStr = betweenMatch.Groups[3].Value.Trim('\'', '"');
            if (!row.TryGetValue(col, out var rowVal) || rowVal is null or DBNull)
            {
                return false;
            }

            if (double.TryParse(rowVal.ToString(), out var valD) &&
                double.TryParse(lowStr, out var lowD) &&
                double.TryParse(highStr, out var highD))
            {
                return valD >= lowD && valD <= highD;
            }

            // Fallback: string comparison
            return string.CompareOrdinal(rowVal.ToString(), lowStr) >= 0 &&
                   string.CompareOrdinal(rowVal.ToString(), highStr) <= 0;
        }

        // ✅ Issue #339/#340: support IN / NOT IN lists, SQLite VALUES forms and tuple
        // predicates (previously not in the operator list, so the condition fell through
        // to "accept all rows"; the VALUES/tuple shapes returned 0 rows).
        if (SqlInPredicate.TryParsePredicate(trimmed, out var inPredicate))
        {
            var matched = SqlInPredicate.IsMatch(row, inPredicate);
            return inPredicate.Negated ? !matched : matched;
        }

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
            // ✅ Issue #348: fail closed — an unrecognized condition must NOT accept every
            // row (the old "return true" turned malformed/unsupported predicates into a
            // tautology that silently returned the whole table).
            return false;
        }

        var columnName = condition[..opIndex].Trim();
        var valueStr = condition[(opIndex + op.Length)..].Trim();

        // Normalize alias-qualified identifiers produced by EF Core SQL rewriting
        // (e.g. b.Url -> Url) so row dictionary lookups succeed.
        var dotIndex = columnName.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < columnName.Length - 1)
        {
            columnName = columnName[(dotIndex + 1)..].Trim('"', '[', ']', '`');
        }

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
