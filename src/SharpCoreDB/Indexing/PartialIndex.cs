// <copyright file="PartialIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Indexing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

/// <summary>
/// Partial (filtered) index implementation.
/// C# 14: Record types, expression trees, modern patterns.
/// 
/// ✅ SCDB Phase 9: Index Enhancements
/// 
/// Purpose:
/// - Index only rows matching a predicate (WHERE clause)
/// - Reduce index size and maintenance cost
/// - Speed up queries on filtered subsets
/// - Support complex predicates
/// 
/// Example:
/// CREATE INDEX idx_active_users ON users(email) WHERE is_active = true
/// </summary>
public sealed class PartialIndex<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly Dictionary<TKey, List<TValue>> _index = [];
    private readonly Func<TValue, bool> _predicate;
    private readonly Func<TValue, TKey> _keySelector;
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialIndex{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="keySelector">Function to extract the index key from a value.</param>
    /// <param name="predicate">Predicate to determine if a value should be indexed.</param>
    public PartialIndex(Func<TValue, TKey> keySelector, Func<TValue, bool> predicate)
    {
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <summary>Gets the number of indexed entries.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _index.Values.Sum(list => list.Count);
            }
        }
    }

    /// <summary>Gets the number of distinct keys.</summary>
    public int KeyCount
    {
        get
        {
            lock (_lock)
            {
                return _index.Count;
            }
        }
    }

    /// <summary>
    /// Adds a value to the index if it matches the predicate.
    /// </summary>
    public bool Add(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!_predicate(value))
            return false;

        var key = _keySelector(value);

        lock (_lock)
        {
            if (!_index.TryGetValue(key, out var list))
            {
                list = [];
                _index[key] = list;
            }

            list.Add(value);
        }

        return true;
    }

    /// <summary>
    /// Adds multiple values to the index.
    /// </summary>
    public int AddRange(IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        int added = 0;
        foreach (var value in values)
        {
            if (Add(value))
                added++;
        }

        return added;
    }

    /// <summary>
    /// Removes a value from the index.
    /// </summary>
    public bool Remove(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var key = _keySelector(value);

        lock (_lock)
        {
            if (_index.TryGetValue(key, out var list))
            {
                bool removed = list.Remove(value);

                if (list.Count == 0)
                {
                    _index.Remove(key);
                }

                return removed;
            }
        }

        return false;
    }

    /// <summary>
    /// Lookups values by key.
    /// </summary>
    public IEnumerable<TValue> Lookup(TKey key)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(key, out var list))
            {
                return list.ToList(); // Return a copy
            }

            return [];
        }
    }

    /// <summary>
    /// Checks if the predicate matches a value.
    /// </summary>
    public bool Matches(TValue value)
    {
        return _predicate(value);
    }

    /// <summary>
    /// Rebuilds the index from a collection.
    /// </summary>
    public void Rebuild(IEnumerable<TValue> values)
    {
        lock (_lock)
        {
            _index.Clear();

            foreach (var value in values)
            {
                if (_predicate(value))
                {
                    var key = _keySelector(value);

                    if (!_index.TryGetValue(key, out var list))
                    {
                        list = [];
                        _index[key] = list;
                    }

                    list.Add(value);
                }
            }
        }
    }

    /// <summary>
    /// Clears the index.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _index.Clear();
        }
    }

    /// <summary>
    /// Gets statistics about the index.
    /// </summary>
    public PartialIndexStats GetStats()
    {
        lock (_lock)
        {
            var totalEntries = _index.Values.Sum(list => list.Count);
            var avgEntriesPerKey = _index.Count > 0 ? (double)totalEntries / _index.Count : 0;

            return new PartialIndexStats
            {
                KeyCount = _index.Count,
                TotalEntries = totalEntries,
                AverageEntriesPerKey = avgEntriesPerKey,
                MaxEntriesPerKey = _index.Values.Count > 0 ? _index.Values.Max(list => list.Count) : 0
            };
        }
    }

    /// <summary>
    /// Disposes the index.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _index.Clear();
        }

        _disposed = true;
    }
}

/// <summary>
/// Partial index statistics.
/// </summary>
public sealed record PartialIndexStats
{
    /// <summary>Number of distinct keys.</summary>
    public required int KeyCount { get; init; }

    /// <summary>Total indexed entries.</summary>
    public required int TotalEntries { get; init; }

    /// <summary>Average entries per key.</summary>
    public required double AverageEntriesPerKey { get; init; }

    /// <summary>Maximum entries for a single key.</summary>
    public required int MaxEntriesPerKey { get; init; }
}

/// <summary>
/// Partial index definition for metadata.
/// </summary>
public sealed record PartialIndexDefinition
{
    /// <summary>Index name.</summary>
    public required string Name { get; init; }

    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Indexed column.</summary>
    public required string ColumnName { get; init; }

    /// <summary>Filter predicate (WHERE clause).</summary>
    public required string WhereClause { get; init; }

    /// <summary>When the index was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Whether the index is active.</summary>
    public bool IsActive { get; init; } = true;
}
