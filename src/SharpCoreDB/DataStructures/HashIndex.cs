// <copyright file="HashIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Services;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

/// <summary>
/// Thread-safe hash index with SIMD-accelerated hash calculations for improved performance.
/// Uses Vector128/Vector256 instructions when available for string and byte[] hashing.
/// Thread-safety is provided via ReaderWriterLockSlim for optimal read performance.
/// ✅ COLLATE Phase 4: Now supports collation-aware key normalization.
/// </summary>
public class HashIndex : IDisposable
{
    private readonly Dictionary<object, List<long>> _index;
    private readonly string _columnName;
    private readonly CollationType _collation;
    private readonly bool _isUnique;
    private readonly SimdHashEqualityComparer _comparer;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly bool _useUnsafeEqualityIndex;
    private readonly UnsafeEqualityIndex? _unsafeIndex;
    private int _unsafeTotalRows;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HashIndex"/> class.
    /// ✅ COLLATE Phase 4: Now accepts collation type for key normalization.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name to index.</param>
    /// <param name="collation">The collation type for string keys. Defaults to Binary (case-sensitive).</param>
    /// <param name="isUnique">Whether the index enforces unique key constraints.</param>
    /// <param name="useUnsafeEqualityIndex">Whether to use <see cref="UnsafeEqualityIndex"/> backend.</param>
    public HashIndex(
        string tableName,
        string columnName,
        CollationType collation = CollationType.Binary,
        bool isUnique = false,
        bool useUnsafeEqualityIndex = false)
    {
        _columnName = columnName;
        _collation = collation;
        _isUnique = isUnique;
        _useUnsafeEqualityIndex = useUnsafeEqualityIndex;

        // Use SIMD-accelerated comparer with collation support
        _comparer = new SimdHashEqualityComparer(collation);
        _index = new Dictionary<object, List<long>>(_comparer);

        if (_useUnsafeEqualityIndex)
        {
            _unsafeIndex = new UnsafeEqualityIndex();
        }
    }

    /// <summary>
    /// Adds a row to the index at the specified position.
    /// ✅ COLLATE Phase 4: Normalizes string keys based on collation before indexing.
    /// Thread-safe operation using write lock.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="position">The file position of the row.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(Dictionary<string, object> row, long position)
    {
        if (!row.TryGetValue(_columnName, out var key) || key is null)
            return;

        Add(key, position);
    }

    /// <summary>
    /// Adds a key to the index at the specified position (key-based, avoids a row dictionary allocation).
    /// </summary>
    /// <param name="key">The indexed key value (must be non-null).</param>
    /// <param name="position">The file position of the row.</param>
    internal void Add(object key, long position)
    {
        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        // PERF: Non-unique unsafe path — skip outer RWLS; UnsafeEqualityIndex._gate handles sync.
        if (_useUnsafeEqualityIndex && !_isUnique)
        {
            var keyBytes = BuildUnsafeKey(normalizedKey);
            _unsafeIndex.Add(keyBytes, position);
            Interlocked.Increment(ref _unsafeTotalRows);
            return;
        }

        _lock.EnterWriteLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                // Unique path: atomic check + add under outer lock
                var keyBytes = BuildUnsafeKey(normalizedKey);
                if (HasUnsafeRowsForKey(keyBytes))
                {
                    throw new InvalidOperationException(
                        $"Duplicate key value '{key}' violates unique constraint on index '{_columnName}'");
                }

                _unsafeIndex.Add(keyBytes, position);
                _unsafeTotalRows++;
                return;
            }

            if (!_index.TryGetValue(normalizedKey, out var list))
            {
                list = [];
                _index[normalizedKey] = list;
            }
            else if (_isUnique && list.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Duplicate key value '{key}' violates unique constraint on index '{_columnName}'");
            }
            list.Add(position);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a row from the index.
    /// ✅ COLLATE Phase 4: Normalizes string keys based on collation before removal.
    /// Thread-safe operation using write lock.
    /// </summary>
    /// <param name="row">The row data.</param>
    public void Remove(Dictionary<string, object> row)
    {
        if (!row.TryGetValue(_columnName, out var key) || key is null)
            return;

        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        _lock.EnterWriteLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                var keyBytes = BuildUnsafeKey(normalizedKey);
                var positions = LookupUnsafePositions(keyBytes);
                foreach (var rowId in positions)
                {
                    if (_unsafeIndex.Remove(keyBytes, rowId))
                    {
                        _unsafeTotalRows--;
                    }
                }

                return;
            }

            _index.Remove(normalizedKey);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a specific position for a row from the index.
    /// ✅ COLLATE Phase 4: Normalizes string keys based on collation before removal.
    /// Thread-safe operation using write lock.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="position">The position to remove.</param>
    public void Remove(Dictionary<string, object> row, long position)
    {
        if (!row.TryGetValue(_columnName, out var key) || key is null)
            return;

        Remove(key, position);
    }

    /// <summary>
    /// Removes a specific position for a key from the index (key-based, avoids a row dictionary allocation).
    /// </summary>
    /// <param name="key">The indexed key value (must be non-null).</param>
    /// <param name="position">The position to remove.</param>
    internal void Remove(object key, long position)
    {
        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        _lock.EnterWriteLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                var keyBytes = BuildUnsafeKey(normalizedKey);
                if (_unsafeIndex.Remove(keyBytes, position))
                {
                    _unsafeTotalRows--;
                }

                return;
            }

            if (_index.TryGetValue(normalizedKey, out var list))
            {
                list.Remove(position);
                if (list.Count == 0)
                {
                    _index.Remove(normalizedKey);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes multiple row-position pairs from the index in a single lock acquisition.
    /// PERF: Acquires write lock once for entire batch instead of per-row.
    /// </summary>
    /// <param name="rows">The rows to remove.</param>
    /// <param name="positions">Corresponding storage positions.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void RemoveBatch(List<Dictionary<string, object>> rows, long[] positions)
    {
        if (rows.Count == 0) return;

        // Extract each row's indexed key once (null when the column is absent) and delegate to
        // the key-based batch removal, which compacts duplicate-key lists in a single pass.
        var keys = new object?[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].TryGetValue(_columnName, out keys[i]);
        }

        RemoveBatchKeys(keys, positions);
    }

    /// <summary>
    /// WP12: removes multiple key-position pairs in a single lock acquisition.
    /// Key-based overload of <see cref="RemoveBatch(List{Dictionary{string, object}}, long[])"/>:
    /// callers that already know each indexed column's key (or intentionally build a
    /// key-only row) skip the per-row dictionary lookup inside the lock.
    /// </summary>
    /// <param name="keys">The indexed key values (null entries are skipped).</param>
    /// <param name="positions">Corresponding storage positions.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal void RemoveBatchKeys(object?[] keys, long[] positions)
    {
        if (keys.Length == 0) return;

        _lock.EnterWriteLock();
        try
        {
            // Deferred duplicate-key removals. A key whose position list has more than one entry and
            // appears more than once in the batch previously cost one O(list) List.Remove shift per
            // duplicate — O(m·n) for a key with n rows and m duplicate-key deletions in the batch.
            // Instead the first occurrence is removed directly and later occurrences are collected,
            // after which the list is compacted in one O(list) pass. Single-row keys (the common
            // case for unique-ish indexed values) stay on the allocation-free direct path.
            Dictionary<object, HashSet<long>>? deferred = null;

            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (key is null)
                {
                    continue;
                }

                var normalizedKey = NormalizeKey(key);

                if (_useUnsafeEqualityIndex)
                {
                    var keyBytes = BuildUnsafeKey(normalizedKey);
                    if (_unsafeIndex.Remove(keyBytes, positions[i]))
                    {
                        _unsafeTotalRows--;
                    }

                    continue;
                }

                if (!_index.TryGetValue(normalizedKey, out var list))
                {
                    continue;
                }

                if (list.Count > 1)
                {
                    deferred ??= new Dictionary<object, HashSet<long>>(_comparer);
                    if (deferred.TryGetValue(normalizedKey, out var dupSet))
                    {
                        (dupSet ??= new HashSet<long>()).Add(positions[i]);
                        deferred[normalizedKey] = dupSet;
                    }
                    else
                    {
                        list.Remove(positions[i]);
                        if (list.Count == 0)
                        {
                            _index.Remove(normalizedKey);
                        }
                        else
                        {
                            deferred.Add(normalizedKey, null);
                        }
                    }

                    continue;
                }

                // Single-row key: direct removal, no duplicate tracking needed.
                list.Remove(positions[i]);
                if (list.Count == 0)
                {
                    _index.Remove(normalizedKey);
                }
            }

            if (deferred != null)
            {
                foreach (var kvp in deferred.Where(static kvp => kvp.Value is not null))
                {
                    CompactPositionList(kvp.Key, kvp.Value!);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes every position in <paramref name="removed"/> from the key's position list with one
    /// O(list) compaction pass (value-based membership, so ordering is irrelevant).
    /// </summary>
    private void CompactPositionList(object key, HashSet<long> removed)
    {
        if (!_index.TryGetValue(key, out var list))
        {
            return;
        }

        int write = 0;
        for (int read = 0; read < list.Count; read++)
        {
            if (!removed.Contains(list[read]))
            {
                list[write++] = list[read];
            }
        }

        if (write == 0)
        {
            _index.Remove(key);
        }
        else if (write < list.Count)
        {
            list.RemoveRange(write, list.Count - write);
        }
    }

    /// <summary>
    /// Key-based overload of <see cref="AddBatch"/>: callers that already know each indexed key
    /// (e.g. an in-place UPDATE re-point) add all rows with one lock acquisition per index.
    /// </summary>
    /// <param name="keys">The indexed key values (null entries are skipped).</param>
    /// <param name="positions">Corresponding storage positions.</param>
    internal void AddBatchKeys(object?[] keys, long[] positions)
    {
        if (keys.Length == 0)
        {
            return;
        }

        // PERF: Non-unique unsafe path — batch keys, then a single UnsafeEqualityIndex acquisition.
        if (_useUnsafeEqualityIndex && !_isUnique)
        {
            var keyArrays = ArrayPool<byte[]>.Shared.Rent(keys.Length);
            var rowIdBuf = ArrayPool<long>.Shared.Rent(keys.Length);
            int validCount = 0;

            try
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i] is null)
                    {
                        continue;
                    }

                    keyArrays[validCount] = BuildUnsafeKey(NormalizeKey(keys[i]));
                    rowIdBuf[validCount] = positions[i];
                    validCount++;
                }

                if (validCount > 0)
                {
                    _unsafeIndex.AddBatch(keyArrays, rowIdBuf, validCount);
                    Interlocked.Add(ref _unsafeTotalRows, validCount);
                }
            }
            finally
            {
                ArrayPool<byte[]>.Shared.Return(keyArrays, clearArray: true);
                ArrayPool<long>.Shared.Return(rowIdBuf);
            }

            return;
        }

        _lock.EnterWriteLock();
        try
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] is null)
                {
                    continue;
                }

                var normalizedKey = NormalizeKey(keys[i]);

                if (_useUnsafeEqualityIndex)
                {
                    // Unique path: atomic check + add under outer lock.
                    var keyBytes = BuildUnsafeKey(normalizedKey);
                    if (HasUnsafeRowsForKey(keyBytes))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate key value '{keys[i]}' violates unique constraint on index '{_columnName}'");
                    }

                    _unsafeIndex.Add(keyBytes, positions[i]);
                    _unsafeTotalRows++;
                    continue;
                }

                if (!_index.TryGetValue(normalizedKey, out var list))
                {
                    list = [];
                    _index[normalizedKey] = list;
                }
                else if (_isUnique && list.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Duplicate key value '{keys[i]}' violates unique constraint on index '{_columnName}'");
                }

                list.Add(positions[i]);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Adds multiple rows to the index in a single lock acquisition.
    /// ✅ PERF: Acquires write lock once for entire batch instead of per-row.
    /// Reduces lock contention by ~95% for batch inserts.
    /// </summary>
    /// <param name="rows">The rows to index.</param>
    /// <param name="positions">Corresponding storage positions for each row.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void AddBatch(List<Dictionary<string, object>> rows, long[] positions)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(positions);

        // PERF: Non-unique unsafe path — skip outer RWLS; batch keys then call
        // UnsafeEqualityIndex.AddBatch with a single _gate acquisition.
        if (_useUnsafeEqualityIndex && !_isUnique)
        {
            var keyArrays = ArrayPool<byte[]>.Shared.Rent(rows.Count);
            var rowIdBuf = ArrayPool<long>.Shared.Rent(rows.Count);
            int validCount = 0;

            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!rows[i].TryGetValue(_columnName, out var key) || key is null)
                        continue;

                    keyArrays[validCount] = BuildUnsafeKey(NormalizeKey(key));
                    rowIdBuf[validCount] = positions[i];
                    validCount++;
                }

                if (validCount > 0)
                {
                    _unsafeIndex.AddBatch(keyArrays, rowIdBuf, validCount);
                    Interlocked.Add(ref _unsafeTotalRows, validCount);
                }
            }
            finally
            {
                ArrayPool<byte[]>.Shared.Return(keyArrays, clearArray: true);
                ArrayPool<long>.Shared.Return(rowIdBuf);
            }

            return;
        }

        _lock.EnterWriteLock();
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!row.TryGetValue(_columnName, out var key) || key is null)
                    continue;

                var normalizedKey = NormalizeKey(key);

                if (_useUnsafeEqualityIndex)
                {
                    // Unique path: atomic check + add under outer lock
                    var keyBytes = BuildUnsafeKey(normalizedKey);
                    if (HasUnsafeRowsForKey(keyBytes))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate key value '{key}' violates unique constraint on index '{_columnName}'");
                    }
                    _unsafeIndex.Add(keyBytes, positions[i]);
                    _unsafeTotalRows++;
                    continue;
                }

                if (!_index.TryGetValue(normalizedKey, out var list))
                {
                    list = [];
                    _index[normalizedKey] = list;
                }
                else if (_isUnique && list.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Duplicate key value '{key}' violates unique constraint on index '{_columnName}'");
                }
                list.Add(positions[i]);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Looks up positions for a given key using SIMD-accelerated comparison.
    /// ✅ COLLATE Phase 4: Normalizes string keys based on collation before lookup.
    /// Thread-safe operation using read lock.
    /// </summary>
    /// <param name="key">The key to lookup.</param>
    /// <returns>List of positions matching the key.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<long> LookupPositions(object key)
    {
        if (key is null)
            return [];

        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        _lock.EnterReadLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                return LookupUnsafePositions(BuildUnsafeKey(normalizedKey));
            }

            return _index.TryGetValue(normalizedKey, out var list) ? new List<long>(list) : [];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Returns the internal position list WITHOUT copying (v2 allocation reduction).
    /// <para>
    /// SAFETY: the caller MUST hold the owning table's write lock so no concurrent writer can
    /// mutate the returned list. Batch update/delete paths hold <c>Table.rwLock</c> in write
    /// mode for their entire duration, so this is safe there. Do NOT use in lock-free reads
    /// (e.g. <c>SelectInternal</c>) — those must keep using <see cref="LookupPositions"/>.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal List<long> LookupPositionsUnsafe(object key)
    {
        if (key is null)
            return [];

        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        _lock.EnterReadLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                return LookupUnsafePositions(BuildUnsafeKey(normalizedKey));
            }

            return _index.TryGetValue(normalizedKey, out var list) ? list : [];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the number of unique keys in the index.
    /// Thread-safe operation using read lock.
    /// </summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _useUnsafeEqualityIndex ? _unsafeIndex?.DistinctKeyCount ?? 0 : _index.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets statistics about the index.
    /// Thread-safe operation using read lock.
    /// </summary>
    /// <returns>Tuple of (UniqueKeys, TotalRows, AvgRowsPerKey).</returns>
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey) GetStatistics()
    {
        _lock.EnterReadLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                var unsafeUniqueKeys = _unsafeIndex?.DistinctKeyCount ?? 0;
                var unsafeTotalRows = _unsafeTotalRows;
                var unsafeAvgRowsPerKey = unsafeUniqueKeys > 0 ? (double)unsafeTotalRows / unsafeUniqueKeys : 0;
                return (unsafeUniqueKeys, unsafeTotalRows, unsafeAvgRowsPerKey);
            }

            var uniqueKeys = _index.Count;
            var totalRows = _index.Values.Sum(list => list.Count);
            var avgRowsPerKey = uniqueKeys > 0 ? (double)totalRows / uniqueKeys : 0;
            return (uniqueKeys, totalRows, avgRowsPerKey);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Checks if a key exists in the index using SIMD-accelerated hash lookup.
    /// ✅ COLLATE Phase 4: Normalizes string keys based on collation before checking.
    /// Thread-safe operation using read lock.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if key exists.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(object key)
    {
        if (key is null)
            return false;

        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        _lock.EnterReadLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                return HasUnsafeRowsForKey(BuildUnsafeKey(normalizedKey));
            }

            return _index.ContainsKey(normalizedKey);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Clears all entries from the index.
    /// Thread-safe operation using write lock.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                _unsafeIndex.Clear();
                _unsafeTotalRows = 0;
                return;
            }

            _index.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Rebuilds the index from a list of rows.
    /// ✅ COLLATE Phase 4: Normalizes string keys based on collation during rebuild.
    /// Thread-safe operation using write lock.
    /// </summary>
    /// <param name="rows">The rows to index.</param>
    public void Rebuild(List<Dictionary<string, object>> rows)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_useUnsafeEqualityIndex)
            {
                _unsafeIndex.Clear();
                _unsafeTotalRows = 0;

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].TryGetValue(_columnName, out var key) && key is not null)
                    {
                        var normalizedKey = NormalizeKey(key);
                        var keyBytes = BuildUnsafeKey(normalizedKey);
                        _unsafeIndex.Add(keyBytes, i);
                        _unsafeTotalRows++;
                    }
                }

                return;
            }

            _index.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].TryGetValue(_columnName, out var key) && key is not null)
                {
                    // ✅ COLLATE Phase 4: Normalize string keys based on collation
                    var normalizedKey = NormalizeKey(key);

                    if (!_index.TryGetValue(normalizedKey, out var list))
                    {
                        list = [];
                        _index[normalizedKey] = list;
                    }
                    list.Add(i);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Normalizes an index key based on the collation type.
    /// ✅ COLLATE Phase 4: Helper method for consistent key normalization.
    /// </summary>
    /// <param name="key">The original key.</param>
    /// <returns>The normalized key.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object NormalizeKey(object key)
    {
        // Only normalize string keys
        if (key is string str)
        {
            return CollationExtensions.NormalizeIndexKey(str, _collation);
        }

        return key;
    }

    /// <summary>
    /// Disposes the HashIndex and releases the read-write lock.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _unsafeIndex?.Dispose();
            _lock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasUnsafeRowsForKey(ReadOnlySpan<byte> keyBytes)
    {
        Span<long> probe = stackalloc long[1];
        return _unsafeIndex.GetRowIdsForValue(keyBytes, probe) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private List<long> LookupUnsafePositions(ReadOnlySpan<byte> keyBytes)
    {
        var capacity = 16;
        var rented = ArrayPool<long>.Shared.Rent(capacity);

        try
        {
            while (true)
            {
                var count = _unsafeIndex.GetRowIdsForValue(keyBytes, rented.AsSpan(0, capacity));
                if (count < capacity)
                {
                    var result = new List<long>(count);
                    for (int i = 0; i < count; i++)
                    {
                        result.Add(rented[i]);
                    }
                    return result;
                }

                ArrayPool<long>.Shared.Return(rented, clearArray: true);
                capacity *= 2;
                rented = ArrayPool<long>.Shared.Rent(capacity);
            }
        }
        finally
        {
            ArrayPool<long>.Shared.Return(rented, clearArray: true);
        }
    }

    private static byte[] BuildUnsafeKey(object key)
    {
        switch (key)
        {
            case int intValue:
                {
                    var bytes = new byte[1 + sizeof(int)];
                    bytes[0] = 1;
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), intValue);
                    return bytes;
                }

            case long longValue:
                {
                    var bytes = new byte[1 + sizeof(long)];
                    bytes[0] = 2;
                    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(1), longValue);
                    return bytes;
                }

            case double doubleValue:
                {
                    var bytes = new byte[1 + sizeof(long)];
                    bytes[0] = 3;
                    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(1), BitConverter.DoubleToInt64Bits(doubleValue));
                    return bytes;
                }

            case bool boolValue:
                return [4, boolValue ? (byte)1 : (byte)0];

            case DateTime dateTimeValue:
                {
                    var bytes = new byte[1 + sizeof(long)];
                    bytes[0] = 5;
                    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(1), dateTimeValue.ToBinary());
                    return bytes;
                }

            case decimal decimalValue:
                {
                    var bytes = new byte[1 + 16];
                    bytes[0] = 6;
                    var bits = decimal.GetBits(decimalValue);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), bits[0]);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), bits[1]);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9), bits[2]);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(13), bits[3]);
                    return bytes;
                }

            case Guid guidValue:
                {
                    var bytes = new byte[1 + 16];
                    bytes[0] = 7;
                    guidValue.TryWriteBytes(bytes.AsSpan(1));
                    return bytes;
                }

            case byte[] blob:
                {
                    var bytes = new byte[1 + sizeof(int) + blob.Length];
                    bytes[0] = 8;
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), blob.Length);
                    blob.CopyTo(bytes.AsSpan(1 + sizeof(int)));
                    return bytes;
                }

            case string str:
                {
                    var utf8 = Encoding.UTF8.GetBytes(str);
                    var bytes = new byte[1 + sizeof(int) + utf8.Length];
                    bytes[0] = 9;
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), utf8.Length);
                    utf8.CopyTo(bytes.AsSpan(1 + sizeof(int)));
                    return bytes;
                }

            default:
                {
                    var text = key.ToString() ?? string.Empty;
                    var utf8 = Encoding.UTF8.GetBytes(text);
                    var bytes = new byte[1 + sizeof(int) + utf8.Length];
                    bytes[0] = 10;
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), utf8.Length);
                    utf8.CopyTo(bytes.AsSpan(1 + sizeof(int)));
                    return bytes;
                }
        }
    }

    /// <summary>
    /// SIMD-accelerated equality comparer for hash index keys.
    /// Provides fast hash code computation and equality checks for strings and byte arrays.
    /// ✅ COLLATE Phase 4: Now supports collation-aware string comparisons.
    /// </summary>
    private sealed class SimdHashEqualityComparer : IEqualityComparer<object>
    {
        private readonly CollationType _collation;

        public SimdHashEqualityComparer(CollationType collation)
        {
            _collation = collation;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public new bool Equals(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x is null || y is null)
                return false;

            // ✅ COLLATE Phase 4: Collation-aware string equality
            if (x is string sx && y is string sy)
            {
                return CollationExtensions.AreEqual(sx, sy, _collation);
            }

            // Fast path for byte arrays - use SIMD
            if (x is byte[] bx && y is byte[] by)
            {
                return SimdHelper.SequenceEqual(bx, by);
            }

            // Default equality
            return x.Equals(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public int GetHashCode(object obj)
        {
            if (obj is null)
                return 0;

            // ✅ COLLATE Phase 4: Collation-aware hash code for strings
            if (obj is string str)
            {
                return CollationExtensions.GetHashCode(str, _collation);
            }

            // SIMD-accelerated hash for byte arrays
            if (obj is byte[] bytes)
            {
                return SimdHelper.ComputeHashCode(bytes);
            }

            // Default hash code for other types
            return obj.GetHashCode();
        }
    }
}
