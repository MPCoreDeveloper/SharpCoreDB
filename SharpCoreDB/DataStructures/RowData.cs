// <copyright file="RowData.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
/// Stack-allocated row representation for zero-allocation inserts.
/// Replaces Dictionary&lt;string,object&gt; to eliminate 10K heap allocations per 10K inserts.
/// Modern C# 14 with ref struct and Span support.
/// 
/// PERFORMANCE IMPACT:
/// - Before: 10K Dictionary allocations = ~4 MB
/// - After: Stack-allocated ref struct = 0 bytes heap
/// - Expected savings: -30% allocations, -15% CPU
/// </summary>
public readonly ref struct RowData
{
    private readonly ReadOnlySpan<object?> _values;
    private readonly ReadOnlySpan<int> _columnHashes;
    private readonly ReadOnlySpan<string> _columnNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="RowData"/> struct.
    /// </summary>
    /// <param name="columnHashes">Pre-computed column name hashes.</param>
    /// <param name="columnNames">Column names (for validation).</param>
    /// <param name="values">Row values.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowData(ReadOnlySpan<int> columnHashes, ReadOnlySpan<string> columnNames, ReadOnlySpan<object?> values)
    {
        _columnHashes = columnHashes;
        _columnNames = columnNames;
        _values = values;
    }

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public int ColumnCount => _values.Length;

    /// <summary>
    /// Gets value by column hash (O(n) but n is small, typically 3-20 columns).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetValue(int columnHash)
    {
        for (int i = 0; i < _columnHashes.Length; i++)
        {
            if (_columnHashes[i] == columnHash)
                return _values[i];
        }
        return null;
    }

    /// <summary>
    /// Gets value by column name.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetValue(string columnName)
    {
        int hash = columnName.GetHashCode();
        for (int i = 0; i < _columnHashes.Length; i++)
        {
            if (_columnHashes[i] == hash && _columnNames[i] == columnName)
                return _values[i];
        }
        return null;
    }

    /// <summary>
    /// Gets value at index (fastest access).
    /// </summary>
    public object? this[int index] => _values[index];

    /// <summary>
    /// Converts to Dictionary (for compatibility with existing code).
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>(_values.Length);
        for (int i = 0; i < _values.Length; i++)
        {
            if (_values[i] is not null)
            {
                dict[_columnNames[i]] = _values[i]!;
            }
        }
        return dict;
    }

    /// <summary>
    /// Creates RowData from pooled buffers (for batch operations).
    /// </summary>
    public static object?[] RentBuffer(int columnCount)
    {
        return ArrayPool<object?>.Shared.Rent(columnCount);
    }

    /// <summary>
    /// Creates RowData from pre-rented buffer.
    /// </summary>
    public static RowData FromBuffer(
        ReadOnlySpan<int> columnHashes,
        ReadOnlySpan<string> columnNames,
        object?[] buffer,
        int length)
    {
        return new RowData(columnHashes, columnNames, buffer.AsSpan(0, length));
    }

    /// <summary>
    /// Returns pooled buffer to ArrayPool.
    /// </summary>
    public static void ReturnBuffer(object?[] buffer)
    {
        ArrayPool<object?>.Shared.Return(buffer, clearArray: true);
    }
}

/// <summary>
/// Builder for creating RowData instances efficiently.
/// </summary>
public ref struct RowDataBuilder
{
    private readonly Span<object?> _values;
    private readonly ReadOnlySpan<int> _columnHashes;
    private readonly ReadOnlySpan<string> _columnNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="RowDataBuilder"/> struct.
    /// </summary>
    public RowDataBuilder(ReadOnlySpan<int> columnHashes, ReadOnlySpan<string> columnNames, Span<object?> valueBuffer)
    {
        _columnHashes = columnHashes;
        _columnNames = columnNames;
        _values = valueBuffer;
    }

    /// <summary>
    /// Adds value by column name.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(string columnName, object? value)
    {
        int hash = columnName.GetHashCode();
        for (int i = 0; i < _columnHashes.Length; i++)
        {
            if (_columnHashes[i] == hash && _columnNames[i] == columnName)
            {
                _values[i] = value;
                return;
            }
        }
        throw new ArgumentException($"Column '{columnName}' not found", nameof(columnName));
    }

    /// <summary>
    /// Adds value by index (fastest).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddByIndex(int index, object? value)
    {
        _values[index] = value;
    }

    /// <summary>
    /// Builds the final RowData.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowData Build() => new(_columnHashes, _columnNames, _values);
}
