// <copyright file="BTreeIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System.Runtime.CompilerServices;

/// <summary>
/// High-performance B-tree index with range query support.
/// Uses B-tree for O(log n) lookups and efficient range scans.
/// Target: &lt; 10ms for range queries on 10k records.
/// 
/// Performance Characteristics:
/// - Point lookup: O(log n) - slower than hash O(1) but supports ordering
/// - Range query: O(log n + k) where k is result size - MUCH faster than full scan O(n)
/// - Insert/Delete: O(log n) with automatic balancing
/// - Memory: ~40-60 bytes per entry (vs 24 for hash, but enables range queries)
/// 
/// Use Cases:
/// - WHERE age &gt; 30 (range scan)
/// - WHERE created_at BETWEEN '2024-01-01' AND '2024-12-31' (range scan)
/// - ORDER BY salary (sorted iteration)
/// - SELECT MIN(price), MAX(price) (O(log n) vs O(n) full scan)
/// </summary>
/// <typeparam name="TKey">The type of the index key (must be comparable).</typeparam>
/// <param name="columnName">The column name this index is for.</param>
public sealed class BTreeIndex<TKey>(string columnName) : IGenericIndex<TKey>
    where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
{
    private readonly BTree<TKey, List<long>> _btree = new();
    private int _totalEntries;

    /// <inheritdoc/>
    public string ColumnName { get; } = columnName;

    /// <inheritdoc/>
    public IndexType Type => IndexType.BTree;

    /// <inheritdoc/>
    public int Count => _totalEntries;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(TKey key, long position)
    {
        // Check if key already exists in B-tree
        var (found, positions) = _btree.Search(key);
        
        if (found && positions != null)
        {
            // Key exists - add position to existing list
            positions.Add(position);
        }
        else
        {
            // New key - create list and insert
            var newPositions = new List<long> { position };
            _btree.Insert(key, newPositions);
        }
        
        _totalEntries++;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<long> Find(TKey key)
    {
        var (found, positions) = _btree.Search(key);
        return found && positions != null ? positions : [];
    }

    /// <inheritdoc/>
    public IEnumerable<long> FindRange(TKey start, TKey end)
    {
        // Use B-tree's range scan capability
        // RangeScan returns IEnumerable<List<long>>, we need to flatten to IEnumerable<long>
        foreach (var positions in _btree.RangeScan(start, end))
        {
            if (positions is List<long> list)
            {
                foreach (var pos in list)
                {
                    yield return pos;
                }
            }
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(TKey key, long position)
    {
        var (found, positions) = _btree.Search(key);
        
        if (!found || positions == null)
            return false;
        
        var removed = positions.Remove(position);
        if (removed)
        {
            _totalEntries--;
            
            // If list is now empty, remove the entire key
            if (positions.Count == 0)
                _btree.Delete(key);
        }
        
        return removed;
    }

    /// <inheritdoc/>
    public IndexStatistics GetStatistics()
    {
        // Count unique keys by traversing B-tree
        var uniqueKeys = CountUniqueKeys();
        var avgEntriesPerKey = uniqueKeys > 0 ? (double)_totalEntries / uniqueKeys : 0;
        var selectivity = _totalEntries > 0 ? (double)uniqueKeys / _totalEntries : 0;
        
        // Estimate memory usage
        // B-tree nodes: ~40 bytes overhead + key size + value pointer
        var memoryUsage = 
            uniqueKeys * (Unsafe.SizeOf<TKey>() + 16 + 40) + // Key + List pointer + node overhead
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
    /// Counts unique keys in the B-tree by in-order traversal.
    /// </summary>
    private int CountUniqueKeys()
    {
        int count = 0;
        foreach (var _ in _btree.InOrderTraversal())
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Gets all keys in sorted order (useful for ORDER BY optimization).
    /// </summary>
    public IEnumerable<(TKey Key, IEnumerable<long> Positions)> GetSortedEntries()
    {
        // Convert List<long> to IEnumerable<long> for interface compatibility
        foreach (var (key, positions) in _btree.InOrderTraversal())
        {
            yield return (key, positions);
        }
    }

    /// <summary>
    /// Clears all entries from the index.
    /// </summary>
    public void Clear()
    {
        _btree.Clear();
        _totalEntries = 0;
    }
}
