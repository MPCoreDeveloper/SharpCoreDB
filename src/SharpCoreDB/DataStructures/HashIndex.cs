// <copyright file="HashIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Services;
using System;
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
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HashIndex"/> class.
    /// ✅ COLLATE Phase 4: Now accepts collation type for key normalization.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name to index.</param>
    /// <param name="collation">The collation type for string keys. Defaults to Binary (case-sensitive).</param>
    /// <param name="isUnique">Whether the index enforces unique key constraints.</param>
    public HashIndex(string tableName, string columnName, CollationType collation = CollationType.Binary, bool isUnique = false) 
    {
        _columnName = columnName;
        _collation = collation;
        _isUnique = isUnique;
        // Use SIMD-accelerated comparer with collation support
        _comparer = new SimdHashEqualityComparer(collation);
        _index = new Dictionary<object, List<long>>(_comparer);
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

        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        _lock.EnterWriteLock();
        try
        {
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

        // ✅ COLLATE Phase 4: Normalize string keys based on collation
        var normalizedKey = NormalizeKey(key);

        _lock.EnterWriteLock();
        try
        {
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
            return _index.TryGetValue(normalizedKey, out var list) ? new List<long>(list) : [];
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
                return _index.Count;
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
            _lock.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
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
