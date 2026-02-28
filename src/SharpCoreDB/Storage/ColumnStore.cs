// <copyright file="ColumnStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.ColumnStorage;

using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Linq.Expressions;

/// <summary>
/// Generic columnar storage engine with SIMD-optimized aggregates.
/// Transposes row-oriented data to column-oriented for fast analytics.
/// Target: Aggregates on 10k records in less than 2ms.
/// 
/// SPLIT INTO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - ColumnStore.cs: Core class definition, Transpose, GetColumn
/// - ColumnStore.Aggregates.cs: SIMD-optimized aggregate implementations (SUM, AVG, MIN, MAX)
/// - ColumnStore.Buffers.cs: Column buffer interface and concrete buffer types
/// </summary>
/// <typeparam name="T">The entity type to store in columnar format.</typeparam>
public sealed partial class ColumnStore<T> : IDisposable where T : class
{
    private readonly Dictionary<string, IColumnBuffer> _columns = [];
    private int _rowCount;
    private bool _disposed;

    // Cache compiled property accessors for performance
    private static readonly Dictionary<string, Func<T, object?>> _propertyAccessors = CachePropertyAccessors();

    /// <summary>
    /// Gets the number of rows stored.
    /// </summary>
    public int RowCount => _rowCount;

    /// <summary>
    /// Gets the column names.
    /// </summary>
    public IReadOnlyCollection<string> ColumnNames => _columns.Keys;

    /// <summary>
    /// Caches compiled property accessors for type T to avoid reflection overhead.
    /// This is done once per type and reused for all instances.
    /// </summary>
    private static Dictionary<string, Func<T, object?>> CachePropertyAccessors()
    {
        var accessors = new Dictionary<string, Func<T, object?>>();
        var properties = typeof(T).GetProperties();

        foreach (var prop in properties)
        {
            // Build: (T obj) => (object?)obj.PropertyName
            var param = Expression.Parameter(typeof(T), "obj");
            var propAccess = Expression.Property(param, prop);
            var convert = Expression.Convert(propAccess, typeof(object));
            var lambda = Expression.Lambda<Func<T, object?>>(convert, param);
            accessors[prop.Name] = lambda.Compile();
        }

        return accessors;
    }

    /// <summary>
    /// Transposes row-oriented data to columnar format.
    /// This is the key operation for converting row-store to column-store.
    /// OPTIMIZED: Uses compiled expression trees + array indexing to avoid enumerator overhead.
    /// </summary>
    /// <param name="rows">The rows to transpose.</param>
    public void Transpose(IEnumerable<T> rows)
    {
        // Convert to array for fastest access (avoids List<T> bounds checking overhead)
        var rowArray = rows as T[] ?? rows.ToArray();
        _rowCount = rowArray.Length;

        if (_rowCount == 0)
            return;

        // Get properties (cached metadata, not used in inner loop)
        var properties = typeof(T).GetProperties();

        // Column-first iteration (best for sequential writes to buffers)
        foreach (var prop in properties)
        {
            var propType = prop.PropertyType;

            // Create appropriate column buffer based on type
            IColumnBuffer buffer = propType switch
            {
                Type t when t == typeof(int) => new Int32ColumnBuffer(_rowCount),
                Type t when t == typeof(long) => new Int64ColumnBuffer(_rowCount),
                Type t when t == typeof(double) => new DoubleColumnBuffer(_rowCount),
                Type t when t == typeof(decimal) => new DecimalColumnBuffer(_rowCount),
                Type t when t == typeof(string) => new StringColumnBuffer(_rowCount),
                Type t when t == typeof(DateTime) => new DateTimeColumnBuffer(_rowCount),
                Type t when t == typeof(bool) => new BoolColumnBuffer(_rowCount),
                _ => new ObjectColumnBuffer(_rowCount)
            };

            // Get cached compiled accessor
            var accessor = _propertyAccessors[prop.Name];

            // Fill column sequentially (optimal for cache + memory writes)
            for (int i = 0; i < _rowCount; i++)
            {
                var value = accessor(rowArray[i]);
                buffer.SetValue(i, value);
            }

            _columns[prop.Name] = buffer;
        }
    }

    /// <summary>
    /// Gets a typed column buffer for fast access.
    /// </summary>
    /// <typeparam name="TColumn">The column data type.</typeparam>
    /// <param name="columnName">The column name.</param>
    /// <returns>The typed column buffer.</returns>
    public ColumnBuffer<TColumn> GetColumn<TColumn>(string columnName) where TColumn : struct
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer switch
        {
            Int32ColumnBuffer intBuf when typeof(TColumn) == typeof(int) => 
                (ColumnBuffer<TColumn>)(object)intBuf,
            Int64ColumnBuffer longBuf when typeof(TColumn) == typeof(long) => 
                (ColumnBuffer<TColumn>)(object)longBuf,
            DoubleColumnBuffer doubleBuf when typeof(TColumn) == typeof(double) => 
                (ColumnBuffer<TColumn>)(object)doubleBuf,
            _ => throw new InvalidCastException($"Cannot cast column to {typeof(TColumn)}")
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;

        foreach (var column in _columns.Values)
        {
            column.Dispose();
        }

        _columns.Clear();
        _disposed = true;
    }
}
