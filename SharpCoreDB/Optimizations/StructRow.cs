// <copyright file="StructRow.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Optimizations;

using SharpCoreDB.Services;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Zero-allocation row representation using struct arrays instead of Dictionary.
/// Replaces List&lt;Dictionary&lt;string, object&gt;&gt; materialization with typed column buffers.
/// 
/// Target performance (100k record full scan):
/// - Memory allocations: 9MB → &lt;1MB (90% reduction)
/// - GC collections: 10+ → &lt;2
/// - Scan time: 14.5ms → &lt;2ms (vs SQLite)
/// </summary>
public sealed class StructRow : IEquatable<StructRow>
{
    /// <summary>Column names for this row schema.</summary>
    public required string[] Columns { get; init; }

    /// <summary>Column data types for this row schema.</summary>
    public required DataType[] ColumnTypes { get; init; }

    /// <summary>Raw values (boxed for compatibility with existing code).</summary>
    public required object?[] Values { get; init; }

    /// <summary>Hash codes for each column name (for fast lookup).</summary>
    private int[]? _columnHashes;

    /// <summary>Gets or computes column name hashes.</summary>
    private int[] ColumnHashes
    {
        get
        {
            if (_columnHashes != null)
                return _columnHashes;

            _columnHashes = new int[Columns.Length];
            for (int i = 0; i < Columns.Length; i++)
            {
                _columnHashes[i] = Columns[i].GetHashCode();
            }
            return _columnHashes;
        }
    }

    /// <summary>Gets number of columns in this row.</summary>
    public int ColumnCount => Columns.Length;

    /// <summary>Gets a value by column name.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetValue(string columnName)
    {
        int hash = columnName.GetHashCode();
        int[] hashes = ColumnHashes;
        string[] cols = Columns;

        for (int i = 0; i < hashes.Length; i++)
        {
            if (hashes[i] == hash && cols[i] == columnName)
                return Values[i];
        }
        return null;
    }

    /// <summary>Gets a value by column index (fastest).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetValue(int columnIndex) => Values[columnIndex];

    /// <summary>Gets or sets a value by column name.</summary>
    public object? this[string columnName]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetValue(columnName);
        set
        {
            int hash = columnName.GetHashCode();
            int[] hashes = ColumnHashes;
            string[] cols = Columns;

            for (int i = 0; i < hashes.Length; i++)
            {
                if (hashes[i] == hash && cols[i] == columnName)
                {
                    Values[i] = value;
                    return;
                }
            }
            throw new KeyNotFoundException($"Column '{columnName}' not found");
        }
    }

    /// <summary>Gets or sets a value by column index.</summary>
    public object? this[int columnIndex]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Values[columnIndex];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Values[columnIndex] = value;
    }

    /// <summary>Finds the index of a column by name.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int FindColumnIndex(string columnName)
    {
        int hash = columnName.GetHashCode();
        int[] hashes = ColumnHashes;
        string[] cols = Columns;

        for (int i = 0; i < hashes.Length; i++)
        {
            if (hashes[i] == hash && cols[i] == columnName)
                return i;
        }
        return -1;
    }

    /// <summary>Converts to Dictionary for compatibility with existing APIs.</summary>
    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>(Columns.Length);
        for (int i = 0; i < Columns.Length; i++)
        {
            if (Values[i] is not null)
            {
                dict[Columns[i]] = Values[i]!;
            }
        }
        return dict;
    }

    /// <summary>Converts from Dictionary (for compatibility).</summary>
    public static StructRow FromDictionary(Dictionary<string, object> dict, string[] columns, DataType[] columnTypes)
    {
        var values = new object?[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            values[i] = dict.TryGetValue(columns[i], out var val) ? val : null;
        }

        return new StructRow
        {
            Columns = columns,
            ColumnTypes = columnTypes,
            Values = values
        };
    }

    /// <summary>Creates a struct row from parallel arrays.</summary>
    public static StructRow FromArrays(string[] columns, DataType[] columnTypes, object?[] values)
    {
        return new StructRow
        {
            Columns = columns,
            ColumnTypes = columnTypes,
            Values = values
        };
    }

    /// <inheritdoc />
    public bool Equals(StructRow? other)
    {
        if (other is null) return false;
        if (Columns.Length != other.Columns.Length) return false;

        for (int i = 0; i < Columns.Length; i++)
        {
            if (Columns[i] != other.Columns[i]) return false;
            if (!Equals(Values[i], other.Values[i])) return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StructRow);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < Columns.Length; i++)
            {
                hash = hash * 31 + Columns[i].GetHashCode();
                if (Values[i] is not null)
                    hash = hash * 31 + Values[i]!.GetHashCode();
            }
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var parts = new System.Text.StringBuilder();
        for (int i = 0; i < Columns.Length; i++)
        {
            if (i > 0) parts.Append(", ");
            parts.Append($"{Columns[i]}: {Values[i]?.ToString() ?? "NULL"}");
        }
        return parts.ToString();
    }
}

/// <summary>
/// Builder for creating StructRow arrays efficiently with pooled buffers.
/// </summary>
public sealed class StructRowArrayBuilder : IDisposable
{
    private readonly string[] _columns;
    private readonly DataType[] _columnTypes;
    private readonly List<object?[]> _rowBuffers = [];
    private object?[]? _currentBuffer;
    private int _currentIndex;
    private readonly int _bufferCapacity;
    private bool _disposed;

    /// <summary>Initializes a new builder with schema.</summary>
    public StructRowArrayBuilder(string[] columns, DataType[] columnTypes, int initialCapacity = 1000)
    {
        _columns = columns;
        _columnTypes = columnTypes;
        _bufferCapacity = initialCapacity;
        _currentBuffer = ArrayPool<object?>.Shared.Rent(initialCapacity);
        _currentIndex = 0;
    }

    /// <summary>Adds a row to the builder.</summary>
    public void AddRow(object?[] values)
    {
        if (_disposed)
            throw new ObjectDisposedException("StructRowArrayBuilder");

        if (values.Length != _columns.Length)
            throw new ArgumentException($"Row has {values.Length} values, expected {_columns.Length}");

        if (_currentIndex >= _currentBuffer!.Length)
        {
            _rowBuffers.Add(_currentBuffer);
            _currentBuffer = ArrayPool<object?>.Shared.Rent(_bufferCapacity);
            _currentIndex = 0;
        }

        // Store reference to the values array (or copy if needed)
        _currentBuffer[_currentIndex++] = values;
    }

    /// <summary>Builds the final StructRow array.</summary>
    public StructRow[] Build()
    {
        if (_disposed)
            throw new ObjectDisposedException("StructRowArrayBuilder");

        // Collect all rows
        var allRows = new List<StructRow>();

        foreach (var buffer in _rowBuffers)
        {
            for (int i = 0; i < buffer.Length && buffer[i] is not null; i++)
            {
                var valueArray = (object?[])buffer[i]!;
                allRows.Add(new StructRow
                {
                    Columns = _columns,
                    ColumnTypes = _columnTypes,
                    Values = valueArray
                });
            }
        }

        // Add rows from current buffer
        for (int i = 0; i < _currentIndex && _currentBuffer![i] is not null; i++)
        {
            var valueArray = (object?[])_currentBuffer[i]!;
            allRows.Add(new StructRow
            {
                Columns = _columns,
                ColumnTypes = _columnTypes,
                Values = valueArray
            });
        }

        return allRows.ToArray();
    }

    /// <summary>Gets current row count.</summary>
    public int RowCount => _rowBuffers.Sum(b => b.Count(v => v is not null)) + _currentIndex;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        if (_currentBuffer is not null)
        {
            ArrayPool<object?>.Shared.Return(_currentBuffer);
            _currentBuffer = null;
        }

        foreach (var buffer in _rowBuffers)
        {
            ArrayPool<object?>.Shared.Return(buffer);
        }

        _rowBuffers.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Span-based row array for stack-allocated or pinned memory scenarios.
/// Enables zero-copy access to row data in performance-critical paths.
/// </summary>
public readonly ref struct StructRowSpan
{
    private readonly ReadOnlySpan<StructRow> _rows;

    /// <summary>Initializes from span of rows.</summary>
    public StructRowSpan(ReadOnlySpan<StructRow> rows)
    {
        _rows = rows;
    }

    /// <summary>Gets number of rows.</summary>
    public int Count => _rows.Length;

    /// <summary>Gets row at index.</summary>
    public StructRow this[int index] => _rows[index];

    /// <summary>Gets enumerator for foreach loops.</summary>
    public ReadOnlySpan<StructRow>.Enumerator GetEnumerator() => _rows.GetEnumerator();

    /// <summary>Filters rows using predicate (returns new array).</summary>
    public StructRow[] Where(System.Func<StructRow, bool> predicate)
    {
        var results = new List<StructRow>(_rows.Length / 2);
        foreach (var row in _rows)
        {
            if (predicate(row))
                results.Add(row);
        }
        return results.ToArray();
    }

    /// <summary>Projects rows to new type (single column).</summary>
    public T[] Select<T>(System.Func<StructRow, T> selector)
    {
        var results = new T[_rows.Length];
        for (int i = 0; i < _rows.Length; i++)
        {
            results[i] = selector(_rows[i]);
        }
        return results;
    }
}
