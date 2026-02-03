// <copyright file="ExpressionIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Indexing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

/// <summary>
/// Expression (computed column) index implementation.
/// C# 14: Record types, expression trees, caching.
/// 
/// ✅ SCDB Phase 9: Index Enhancements
/// 
/// Purpose:
/// - Index computed values from expressions
/// - Cache computed results for fast lookups
/// - Support functions like LOWER(), UPPER(), YEAR()
/// - Enable case-insensitive searches
/// 
/// Example:
/// CREATE INDEX idx_email_lower ON users(LOWER(email))
/// CREATE INDEX idx_birth_year ON users(YEAR(birth_date))
/// </summary>
public sealed class ExpressionIndex<TInput, TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly Dictionary<TKey, List<TValue>> _index = [];
    private readonly Dictionary<TValue, TKey> _computedCache = [];
    private readonly Func<TInput, TKey> _expression;
    private readonly Func<TInput, TValue> _valueSelector;
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpressionIndex{TInput, TKey, TValue}"/> class.
    /// </summary>
    /// <param name="expression">Expression to compute the index key.</param>
    /// <param name="valueSelector">Function to extract the value to store.</param>
    public ExpressionIndex(Func<TInput, TKey> expression, Func<TInput, TValue> valueSelector)
    {
        _expression = expression ?? throw new ArgumentNullException(nameof(expression));
        _valueSelector = valueSelector ?? throw new ArgumentNullException(nameof(valueSelector));
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

    /// <summary>Gets the number of distinct computed keys.</summary>
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
    /// Adds an entry to the index by computing the expression.
    /// </summary>
    public void Add(TInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var key = _expression(input);
        var value = _valueSelector(input);

        lock (_lock)
        {
            if (!_index.TryGetValue(key, out var list))
            {
                list = [];
                _index[key] = list;
            }

            list.Add(value);
            _computedCache[value] = key;
        }
    }

    /// <summary>
    /// Adds multiple entries to the index.
    /// </summary>
    public void AddRange(IEnumerable<TInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        foreach (var input in inputs)
        {
            Add(input);
        }
    }

    /// <summary>
    /// Removes an entry from the index.
    /// </summary>
    public bool Remove(TValue value)
    {
        lock (_lock)
        {
            if (_computedCache.TryGetValue(value, out var key))
            {
                if (_index.TryGetValue(key, out var list))
                {
                    bool removed = list.Remove(value);

                    if (list.Count == 0)
                    {
                        _index.Remove(key);
                    }

                    if (removed)
                    {
                        _computedCache.Remove(value);
                    }

                    return removed;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Looks up values by the computed key.
    /// </summary>
    public IEnumerable<TValue> Lookup(TKey computedKey)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(computedKey, out var list))
            {
                return list.ToList(); // Return a copy
            }

            return [];
        }
    }

    /// <summary>
    /// Looks up values by computing the expression on input.
    /// </summary>
    public IEnumerable<TValue> LookupByInput(TInput input)
    {
        var computedKey = _expression(input);
        return Lookup(computedKey);
    }

    /// <summary>
    /// Gets the computed key for a value (from cache).
    /// </summary>
    public TKey? GetComputedKey(TValue value)
    {
        lock (_lock)
        {
            return _computedCache.TryGetValue(value, out var key) ? key : default;
        }
    }

    /// <summary>
    /// Rebuilds the index from a collection.
    /// </summary>
    public void Rebuild(IEnumerable<TInput> inputs)
    {
        lock (_lock)
        {
            _index.Clear();
            _computedCache.Clear();

            foreach (var input in inputs)
            {
                var key = _expression(input);
                var value = _valueSelector(input);

                if (!_index.TryGetValue(key, out var list))
                {
                    list = [];
                    _index[key] = list;
                }

                list.Add(value);
                _computedCache[value] = key;
            }
        }
    }

    /// <summary>
    /// Clears the index and cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _index.Clear();
            _computedCache.Clear();
        }
    }

    /// <summary>
    /// Gets statistics about the index.
    /// </summary>
    public ExpressionIndexStats GetStats()
    {
        lock (_lock)
        {
            var totalEntries = _index.Values.Sum(list => list.Count);
            var avgEntriesPerKey = _index.Count > 0 ? (double)totalEntries / _index.Count : 0;

            return new ExpressionIndexStats
            {
                KeyCount = _index.Count,
                TotalEntries = totalEntries,
                CachedComputations = _computedCache.Count,
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
            _computedCache.Clear();
        }

        _disposed = true;
    }
}

/// <summary>
/// Expression index statistics.
/// </summary>
public sealed record ExpressionIndexStats
{
    /// <summary>Number of distinct computed keys.</summary>
    public required int KeyCount { get; init; }

    /// <summary>Total indexed entries.</summary>
    public required int TotalEntries { get; init; }

    /// <summary>Number of cached computations.</summary>
    public required int CachedComputations { get; init; }

    /// <summary>Average entries per computed key.</summary>
    public required double AverageEntriesPerKey { get; init; }

    /// <summary>Maximum entries for a single computed key.</summary>
    public required int MaxEntriesPerKey { get; init; }
}

/// <summary>
/// Expression index definition for metadata.
/// </summary>
public sealed record ExpressionIndexDefinition
{
    /// <summary>Index name.</summary>
    public required string Name { get; init; }

    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Expression (e.g., "LOWER(email)").</summary>
    public required string Expression { get; init; }

    /// <summary>When the index was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Whether the index is active.</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Common expression functions for index creation.
/// </summary>
public static class ExpressionFunctions
{
    /// <summary>Converts string to lowercase.</summary>
    public static string Lower(string value) => value?.ToLowerInvariant() ?? string.Empty;

    /// <summary>Converts string to uppercase.</summary>
    public static string Upper(string value) => value?.ToUpperInvariant() ?? string.Empty;

    /// <summary>Extracts year from DateTime.</summary>
    public static int Year(DateTime value) => value.Year;

    /// <summary>Extracts month from DateTime.</summary>
    public static int Month(DateTime value) => value.Month;

    /// <summary>Extracts day from DateTime.</summary>
    public static int Day(DateTime value) => value.Day;

    /// <summary>Gets the length of a string.</summary>
    public static int Length(string value) => value?.Length ?? 0;

    /// <summary>Trims whitespace from a string.</summary>
    public static string Trim(string value) => value?.Trim() ?? string.Empty;

    /// <summary>Gets substring.</summary>
    public static string Substring(string value, int start, int length) =>
        value?.Length >= start + length ? value.Substring(start, length) : string.Empty;
}
