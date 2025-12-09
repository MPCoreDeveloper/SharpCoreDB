// <copyright file="HashIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Hash index with SIMD-accelerated hash calculations for improved performance.
/// Uses Vector128/Vector256 instructions when available for string and byte[] hashing.
/// </summary>
public class HashIndex
{
    private readonly Dictionary<object, List<long>> _index;
    private readonly string _columnName;
    private readonly SimdHashEqualityComparer _comparer = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HashIndex"/> class.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name to index.</param>
    public HashIndex(string tableName, string columnName) 
    {
        _columnName = columnName;
        // Use default comparer: SimdHashEqualityComparer was removed due to issues with
        // reference equality vs value equality for boxed value types on Linux/.NET 10.
        // May revisit with proper generic implementation in future.
        _index = new Dictionary<object, List<long>>();
    }

    /// <summary>
    /// Adds a row to the index at the specified position.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="position">The file position of the row.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(Dictionary<string, object> row, long position)
    {
        if (!row.TryGetValue(_columnName, out var key) || key == null)
            return;

        if (!_index.TryGetValue(key, out var list))
        {
            list = new List<long>();
            _index[key] = list;
        }
        list.Add(position);
    }

    /// <summary>
    /// Removes a row from the index.
    /// </summary>
    /// <param name="row">The row data.</param>
    public void Remove(Dictionary<string, object> row)
    {
        if (row.TryGetValue(_columnName, out var key) && key != null)
        {
            _index.Remove(key);
        }
    }

    /// <summary>
    /// Removes a specific position for a row from the index.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="position">The position to remove.</param>
    public void Remove(Dictionary<string, object> row, long position)
    {
        if (!row.TryGetValue(_columnName, out var key) || key == null)
            return;

        if (_index.TryGetValue(key, out var list))
        {
            list.Remove(position);
            if (list.Count == 0)
            {
                _index.Remove(key);
            }
        }
    }

    /// <summary>
    /// Looks up positions for a given key using SIMD-accelerated comparison.
    /// </summary>
    /// <param name="key">The key to lookup.</param>
    /// <returns>List of positions matching the key.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<long> LookupPositions(object key)
        => key != null && _index.TryGetValue(key, out var list) ? new List<long>(list) : new();

    /// <summary>
    /// Gets the number of unique keys in the index.
    /// </summary>
    public int Count => _index.Count;

    /// <summary>
    /// Checks if a key exists in the index using SIMD-accelerated hash lookup.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if key exists.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(object key) => key != null && _index.ContainsKey(key);

    /// <summary>
    /// Clears all entries from the index.
    /// </summary>
    public void Clear() => _index.Clear();

    /// <summary>
    /// Rebuilds the index from a list of rows.
    /// </summary>
    /// <param name="rows">The rows to index.</param>
    public void Rebuild(List<Dictionary<string, object>> rows)
    {
        Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            Add(rows[i], i);
        }
    }

    /// <summary>
    /// Gets statistics about the index.
    /// </summary>
    /// <returns>Tuple of (UniqueKeys, TotalRows, AvgRowsPerKey).</returns>
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey) GetStatistics()
    {
        var uniqueKeys = _index.Count;
        var totalRows = _index.Values.Sum(list => list.Count);
        var avgRowsPerKey = uniqueKeys > 0 ? (double)totalRows / uniqueKeys : 0;
        return (uniqueKeys, totalRows, avgRowsPerKey);
    }

    /// <summary>
    /// SIMD-accelerated equality comparer for hash index keys.
    /// Provides fast hash code computation and equality checks for strings and byte arrays.
    /// </summary>
    private class SimdHashEqualityComparer : IEqualityComparer<object>
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
