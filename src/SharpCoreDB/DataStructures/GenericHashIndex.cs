// <copyright file="GenericHashIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System.Runtime.CompilerServices;

/// <summary>
/// High-performance generic hash index with type-safe keys.
/// Uses Dictionary for O(1) lookups, optimized for .NET 10 with modern C# 14.
/// ✅ COLLATE Phase 4: Now supports custom equality comparers for collation-aware indexing.
/// Target: &lt; 0.05ms for lookups on 10k records.
/// </summary>
/// <typeparam name="TKey">The type of the index key.</typeparam>
public sealed partial class GenericHashIndex<TKey> : IGenericIndex<TKey>
    where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
{
    private readonly Dictionary<TKey, List<long>> _index;
    private int _totalEntries;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericHashIndex{TKey}"/> class.
    /// ✅ COLLATE Phase 4: Now accepts optional equality comparer for collation support.
    /// </summary>
    /// <param name="columnName">The column name this index is for.</param>
    /// <param name="comparer">Optional equality comparer for custom key comparison (e.g., collation).</param>
    public GenericHashIndex(string columnName, IEqualityComparer<TKey>? comparer = null)
    {
        ColumnName = columnName;
        _index = comparer is not null 
            ? new Dictionary<TKey, List<long>>(capacity: 10000, comparer) 
            : new Dictionary<TKey, List<long>>(capacity: 10000); // Pre-size for 10k
    }

    /// <inheritdoc/>
    public string ColumnName { get; }

    /// <inheritdoc/>
    public IndexType Type => IndexType.Hash;

    /// <inheritdoc/>
    public int Count => _index.Count;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(TKey key, long position)
    {
        if (!_index.TryGetValue(key, out var positions))
        {
            positions = [];
            _index[key] = positions;
        }
        
        positions.Add(position);
        _totalEntries++;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<long> Find(TKey key)
    {
        return _index.TryGetValue(key, out var positions) ? positions : [];
    }

    /// <inheritdoc/>
    public IEnumerable<long> FindRange(TKey start, TKey end)
    {
        // Hash index doesn't support range queries efficiently
        // Fall back to full scan (caller should use B-Tree for ranges)
        return _index
            .Where(kvp => kvp.Key.CompareTo(start) >= 0 && kvp.Key.CompareTo(end) <= 0)
            .SelectMany(kvp => kvp.Value);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(TKey key, long position)
    {
        if (_index.TryGetValue(key, out var positions))
        {
            var removed = positions.Remove(position);
            if (removed)
            {
                _totalEntries--;
                
                // Clean up empty lists
                if (positions.Count == 0)
                    _index.Remove(key);
            }
            return removed;
        }
        return false;
    }

    /// <inheritdoc/>
    public IndexStatistics GetStatistics()
    {
        var uniqueKeys = _index.Count;
        var avgEntriesPerKey = uniqueKeys > 0 ? (double)_totalEntries / uniqueKeys : 0;
        var selectivity = _totalEntries > 0 ? (double)uniqueKeys / _totalEntries : 0;
        
        // Estimate memory usage
        var memoryUsage = 
            uniqueKeys * (Unsafe.SizeOf<TKey>() + 8 + 24) + // Key + pointer + List overhead
            _totalEntries * 8; // Position longs

        return new IndexStatistics
        {
            UniqueKeys = uniqueKeys,
            TotalEntries = _totalEntries,
            AverageEntriesPerKey = avgEntriesPerKey,
            Selectivity = selectivity,
            MemoryUsageBytes = memoryUsage
        };
    }

    /// <summary>
    /// Optimizes the index by rebuilding with exact capacity.
    /// Call after bulk inserts for better memory efficiency.
    /// </summary>
    public void Optimize()
    {
        // Dictionary is already pretty optimized in .NET 10
        // Could trim excess capacity in Lists if needed
        foreach (var positions in _index.Values)
        {
            if (positions.Capacity > positions.Count * 2)
                positions.TrimExcess();
        }
    }

    /// <summary>
    /// Clears all entries from the index.
    /// </summary>
    public void Clear()
    {
        _index.Clear();
        _totalEntries = 0;
    }
}
