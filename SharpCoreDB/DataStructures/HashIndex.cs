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
/// </summary>
public class HashIndex : IDisposable
{
    private readonly Dictionary<object, List<long>> _index;
    private readonly string _columnName;
    private readonly SimdHashEqualityComparer _comparer = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HashIndex"/> class.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name to index.</param>
    public HashIndex(string tableName, string columnName) 
    {
        _columnName = columnName;
        // Use SIMD-accelerated comparer for better hash performance
        _index = new Dictionary<object, List<long>>(_comparer);
    }

    /// <summary>
    /// Adds a row to the index at the specified position.
    /// Thread-safe operation using write lock.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="position">The file position of the row.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(Dictionary<string, object> row, long position)
    {
        if (!row.TryGetValue(_columnName, out var key) || key is null)
            return;

        _lock.EnterWriteLock();
        try
        {
            if (!_index.TryGetValue(key, out var list))
            {
                list = [];
                _index[key] = list;
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
    /// Thread-safe operation using write lock.
    /// </summary>
    /// <param name="row">The row data.</param>
    public void Remove(Dictionary<string, object> row)
    {
        if (!row.TryGetValue(_columnName, out var key) || key is null)
            return;

        _lock.EnterWriteLock();
        try
        {
            _index.Remove(key);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a specific position for a row from the index.
    /// Thread-safe operation using write lock.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="position">The position to remove.</param>
    public void Remove(Dictionary<string, object> row, long position)
    {
        if (!row.TryGetValue(_columnName, out var key) || key is null)
            return;

        _lock.EnterWriteLock();
        try
        {
            if (_index.TryGetValue(key, out var list))
            {
                list.Remove(position);
                if (list.Count == 0)
                {
                    _index.Remove(key);
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
    /// Thread-safe operation using read lock.
    /// </summary>
    /// <param name="key">The key to lookup.</param>
    /// <returns>List of positions matching the key.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<long> LookupPositions(object key)
    {
        if (key is null)
            return [];

        _lock.EnterReadLock();
        try
        {
            return _index.TryGetValue(key, out var list) ? new List<long>(list) : [];
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
    /// Checks if a key exists in the index using SIMD-accelerated hash lookup.
    /// Thread-safe operation using read lock.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if key exists.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(object key)
    {
        if (key is null)
            return false;

        _lock.EnterReadLock();
        try
        {
            return _index.ContainsKey(key);
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
                    if (!_index.TryGetValue(key, out var list))
                    {
                        list = [];
                        _index[key] = list;
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
    /// </summary>
    private sealed class SimdHashEqualityComparer : IEqualityComparer<object>
    {
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public new bool Equals(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x is null || y is null)
                return false;

            // Fast path for strings - use SIMD
            if (x is string sx && y is string sy)
            {
                return EqualsString(sx, sy);
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

            // SIMD-accelerated hash for strings
            if (obj is string str)
            {
                return GetHashCodeString(str);
            }

            // SIMD-accelerated hash for byte arrays
            if (obj is byte[] bytes)
            {
                return SimdHelper.ComputeHashCode(bytes);
            }

            // Default hash code for other types
            return obj.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool EqualsString(string x, string y)
        {
            if (x.Length != y.Length)
                return false;

            // Use SIMD for string comparison via UTF8 bytes
            // For small strings (<= 256 bytes), use stack allocation
            if (x.Length <= 128)
            {
                Span<byte> xBytes = stackalloc byte[x.Length * 2];
                Span<byte> yBytes = stackalloc byte[y.Length * 2];
                
                int xLen = Encoding.UTF8.GetBytes(x, xBytes);
                int yLen = Encoding.UTF8.GetBytes(y, yBytes);
                
                if (xLen != yLen)
                    return false;

                return SimdHelper.SequenceEqual(xBytes.Slice(0, xLen), yBytes.Slice(0, yLen));
            }

            // For larger strings, use standard comparison
            return x == y;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int GetHashCodeString(string str)
        {
            // Use SIMD for string hash via UTF8 bytes
            // For small strings, use stack allocation
            if (str.Length <= 128)
            {
                Span<byte> bytes = stackalloc byte[str.Length * 3]; // UTF8 max expansion
                int byteCount = Encoding.UTF8.GetBytes(str, bytes);
                return SimdHelper.ComputeHashCode(bytes.Slice(0, byteCount));
            }

            // For larger strings, allocate and use SIMD
            byte[] bytes2 = Encoding.UTF8.GetBytes(str);
            return SimdHelper.ComputeHashCode(bytes2);
        }
    }
}
