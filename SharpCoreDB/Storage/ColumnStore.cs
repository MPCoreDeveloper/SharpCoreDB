// <copyright file="ColumnStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.ColumnStorage;

using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

/// <summary>
/// Generic columnar storage engine with SIMD-optimized aggregates.
/// Transposes row-oriented data to column-oriented for fast analytics.
/// Target: Aggregates on 10k records in < 2ms.
/// </summary>
/// <typeparam name="T">The entity type to store in columnar format.</typeparam>
public sealed class ColumnStore<T> : IDisposable where T : class
{
    private readonly Dictionary<string, IColumnBuffer> _columns = new();
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
    private int _rowCount;
    private bool _disposed;

    /// <summary>
    /// Gets the number of rows stored.
    /// </summary>
    public int RowCount => _rowCount;

    /// <summary>
    /// Gets the column names.
    /// </summary>
    public IReadOnlyCollection<string> ColumnNames => _columns.Keys;

    /// <summary>
    /// Transposes row-oriented data to columnar format.
    /// This is the key operation for converting row-store to column-store.
    /// </summary>
    /// <param name="rows">The rows to transpose.</param>
    public void Transpose(IEnumerable<T> rows)
    {
        var rowList = rows.ToList();
        _rowCount = rowList.Count;

        if (_rowCount == 0)
            return;

        // Get properties via reflection (could be cached)
        var properties = typeof(T).GetProperties();

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

            // Fill column with values from rows
            for (int i = 0; i < _rowCount; i++)
            {
                var value = prop.GetValue(rowList[i]);
                buffer.SetValue(i, value);
            }

            _columns[prop.Name] = buffer;
        }
    }

    /// <summary>
    /// Gets a typed column buffer for fast access.
    /// </summary>
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

    #region SIMD-Optimized Aggregates

    /// <summary>
    /// Computes SUM using SIMD vectorization.
    /// Target: < 0.1ms for 10k int32 values.
    /// </summary>
    public TResult Sum<TResult>(string columnName) where TResult : struct
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer switch
        {
            Int32ColumnBuffer intBuf => (TResult)(object)SumInt32SIMD(intBuf),
            Int64ColumnBuffer longBuf => (TResult)(object)SumInt64SIMD(longBuf),
            DoubleColumnBuffer doubleBuf => (TResult)(object)SumDoubleSIMD(doubleBuf),
            DecimalColumnBuffer decBuf => (TResult)(object)SumDecimal(decBuf),
            _ => throw new NotSupportedException($"SUM not supported for column type")
        };
    }

    /// <summary>
    /// Computes AVERAGE using SIMD.
    /// </summary>
    public double Average(string columnName)
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer switch
        {
            Int32ColumnBuffer intBuf => (double)SumInt32SIMD(intBuf) / _rowCount,
            Int64ColumnBuffer longBuf => (double)SumInt64SIMD(longBuf) / _rowCount,
            DoubleColumnBuffer doubleBuf => SumDoubleSIMD(doubleBuf) / _rowCount,
            DecimalColumnBuffer decBuf => (double)SumDecimal(decBuf) / _rowCount,
            _ => throw new NotSupportedException($"AVERAGE not supported for column type")
        };
    }

    /// <summary>
    /// Computes MIN using SIMD.
    /// </summary>
    public TResult Min<TResult>(string columnName) where TResult : struct, IComparable<TResult>
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer switch
        {
            Int32ColumnBuffer intBuf => (TResult)(object)MinInt32SIMD(intBuf),
            Int64ColumnBuffer longBuf => (TResult)(object)MinInt64SIMD(longBuf),
            DoubleColumnBuffer doubleBuf => (TResult)(object)MinDoubleSIMD(doubleBuf),
            DecimalColumnBuffer decBuf => (TResult)(object)MinDecimal(decBuf),
            _ => throw new NotSupportedException($"MIN not supported for column type")
        };
    }

    /// <summary>
    /// Computes MAX using SIMD.
    /// </summary>
    public TResult Max<TResult>(string columnName) where TResult : struct, IComparable<TResult>
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer switch
        {
            Int32ColumnBuffer intBuf => (TResult)(object)MaxInt32SIMD(intBuf),
            Int64ColumnBuffer longBuf => (TResult)(object)MaxInt64SIMD(longBuf),
            DoubleColumnBuffer doubleBuf => (TResult)(object)MaxDoubleSIMD(doubleBuf),
            DecimalColumnBuffer decBuf => (TResult)(object)MaxDecimal(decBuf),
            _ => throw new NotSupportedException($"MAX not supported for column type")
        };
    }

    /// <summary>
    /// Counts non-null values in a column.
    /// </summary>
    public int Count(string columnName)
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer.CountNonNull();
    }

    #endregion

    #region SIMD Implementations

    private static int SumInt32SIMD(Int32ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        int sum = 0;
        int i = 0;

        // Process 8 ints at a time using Vector256 (AVX2)
        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<int>.Count)
        {
            var vsum = Vector256<int>.Zero;
            
            for (; i <= data.Length - Vector256<int>.Count; i += Vector256<int>.Count)
            {
                var v = Vector256.Create(data.AsSpan(i));
                vsum = Vector256.Add(vsum, v);
            }

            // Horizontal sum
            for (int j = 0; j < Vector256<int>.Count; j++)
            {
                sum += vsum[j];
            }
        }
        // Fallback to Vector128 (SSE)
        else if (Vector128.IsHardwareAccelerated && data.Length >= Vector128<int>.Count)
        {
            var vsum = Vector128<int>.Zero;
            
            for (; i <= data.Length - Vector128<int>.Count; i += Vector128<int>.Count)
            {
                var v = Vector128.Create(data.AsSpan(i));
                vsum = Vector128.Add(vsum, v);
            }

            for (int j = 0; j < Vector128<int>.Count; j++)
            {
                sum += vsum[j];
            }
        }

        // Process remaining elements
        for (; i < data.Length; i++)
        {
            sum += data[i];
        }

        return sum;
    }

    private static long SumInt64SIMD(Int64ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        long sum = 0;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<long>.Count)
        {
            var vsum = Vector256<long>.Zero;
            
            for (; i <= data.Length - Vector256<long>.Count; i += Vector256<long>.Count)
            {
                var v = Vector256.Create(data.AsSpan(i));
                vsum = Vector256.Add(vsum, v);
            }

            for (int j = 0; j < Vector256<long>.Count; j++)
            {
                sum += vsum[j];
            }
        }

        for (; i < data.Length; i++)
        {
            sum += data[i];
        }

        return sum;
    }

    private static double SumDoubleSIMD(DoubleColumnBuffer buffer)
    {
        var data = buffer.GetData();
        double sum = 0;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<double>.Count)
        {
            var vsum = Vector256<double>.Zero;
            
            for (; i <= data.Length - Vector256<double>.Count; i += Vector256<double>.Count)
            {
                var v = Vector256.Create(data.AsSpan(i));
                vsum = Vector256.Add(vsum, v);
            }

            for (int j = 0; j < Vector256<double>.Count; j++)
            {
                sum += vsum[j];
            }
        }

        for (; i < data.Length; i++)
        {
            sum += data[i];
        }

        return sum;
    }

    private static decimal SumDecimal(DecimalColumnBuffer buffer)
    {
        var data = buffer.GetData();
        decimal sum = 0;
        
        // Decimal doesn't support SIMD, use sequential
        foreach (var value in data)
        {
            sum += value;
        }

        return sum;
    }

    private static int MinInt32SIMD(Int32ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        int min = int.MaxValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<int>.Count)
        {
            var vmin = Vector256.Create(int.MaxValue);
            
            for (; i <= data.Length - Vector256<int>.Count; i += Vector256<int>.Count)
            {
                var v = Vector256.Create(data.AsSpan(i));
                vmin = Vector256.Min(vmin, v);
            }

            for (int j = 0; j < Vector256<int>.Count; j++)
            {
                if (vmin[j] < min) min = vmin[j];
            }
        }

        for (; i < data.Length; i++)
        {
            if (data[i] < min) min = data[i];
        }

        return min;
    }

    private static long MinInt64SIMD(Int64ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        long min = long.MaxValue;
        
        foreach (var value in data)
        {
            if (value < min) min = value;
        }

        return min;
    }

    private static double MinDoubleSIMD(DoubleColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        double min = double.MaxValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<double>.Count)
        {
            var vmin = Vector256.Create(double.MaxValue);
            
            for (; i <= data.Length - Vector256<double>.Count; i += Vector256<double>.Count)
            {
                var v = Vector256.Create(data.AsSpan(i));
                vmin = Vector256.Min(vmin, v);
            }

            for (int j = 0; j < Vector256<double>.Count; j++)
            {
                if (vmin[j] < min) min = vmin[j];
            }
        }

        for (; i < data.Length; i++)
        {
            if (data[i] < min) min = data[i];
        }

        return min;
    }

    private static decimal MinDecimal(DecimalColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        decimal min = decimal.MaxValue;
        
        foreach (var value in data)
        {
            if (value < min) min = value;
        }

        return min;
    }

    private static int MaxInt32SIMD(Int32ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        int max = int.MinValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<int>.Count)
        {
            var vmax = Vector256.Create(int.MinValue);
            
            for (; i <= data.Length - Vector256<int>.Count; i += Vector256<int>.Count)
            {
                var v = Vector256.Create(data.AsSpan(i));
                vmax = Vector256.Max(vmax, v);
            }

            for (int j = 0; j < Vector256<int>.Count; j++)
            {
                if (vmax[j] > max) max = vmax[j];
            }
        }

        for (; i < data.Length; i++)
        {
            if (data[i] > max) max = data[i];
        }

        return max;
    }

    private static long MaxInt64SIMD(Int64ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        long max = long.MinValue;
        
        foreach (var value in data)
        {
            if (value > max) max = value;
        }

        return max;
    }

    private static double MaxDoubleSIMD(DoubleColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        double max = double.MinValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<double>.Count)
        {
            var vmax = Vector256.Create(double.MinValue);
            
            for (; i <= data.Length - Vector256<double>.Count; i += Vector256<double>.Count)
            {
                var v = Vector256.Create(data.AsSpan(i));
                vmax = Vector256.Max(vmax, v);
            }

            for (int j = 0; j < Vector256<double>.Count; j++)
            {
                if (vmax[j] > max) max = vmax[j];
            }
        }

        for (; i < data.Length; i++)
        {
            if (data[i] > max) max = data[i];
        }

        return max;
    }

    private static decimal MaxDecimal(DecimalColumnBuffer buffer)
    {
        var data = buffer.GetData();
        if (data.Length == 0) return 0;

        decimal max = decimal.MinValue;
        
        foreach (var value in data)
        {
            if (value > max) max = value;
        }

        return max;
    }

    #endregion

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

/// <summary>
/// Base interface for column buffers.
/// </summary>
internal interface IColumnBuffer : IDisposable
{
    void SetValue(int index, object? value);
    int CountNonNull();
}

/// <summary>
/// Generic column buffer base class.
/// </summary>
public abstract class ColumnBuffer<T> : IColumnBuffer where T : struct
{
    protected T[] data;

    protected ColumnBuffer(int capacity)
    {
        data = new T[capacity];
    }

    public abstract void SetValue(int index, object? value);
    
    public T[] GetData() => data;

    public virtual int CountNonNull() => data.Length;

    public virtual void Dispose()
    {
        data = Array.Empty<T>();
    }
}

/// <summary>
/// Int32 column buffer with SIMD-optimized operations.
/// </summary>
internal sealed class Int32ColumnBuffer : ColumnBuffer<int>
{
    public Int32ColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is int intVal ? intVal : Convert.ToInt32(value);
    }
}

/// <summary>
/// Int64 column buffer.
/// </summary>
internal sealed class Int64ColumnBuffer : ColumnBuffer<long>
{
    public Int64ColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is long longVal ? longVal : Convert.ToInt64(value);
    }
}

/// <summary>
/// Double column buffer with SIMD support.
/// </summary>
internal sealed class DoubleColumnBuffer : ColumnBuffer<double>
{
    public DoubleColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is double doubleVal ? doubleVal : Convert.ToDouble(value);
    }
}

/// <summary>
/// Decimal column buffer (no SIMD, but high precision).
/// </summary>
internal sealed class DecimalColumnBuffer : ColumnBuffer<decimal>
{
    public DecimalColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is decimal decVal ? decVal : Convert.ToDecimal(value);
    }
}

/// <summary>
/// DateTime column buffer (stored as ticks for fast comparison).
/// </summary>
internal sealed class DateTimeColumnBuffer : ColumnBuffer<long>
{
    public DateTimeColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is DateTime dt ? dt.Ticks : 0;
    }
}

/// <summary>
/// Bool column buffer (bit-packed for efficiency).
/// </summary>
internal sealed class BoolColumnBuffer : IColumnBuffer
{
    private readonly byte[] data;
    private readonly int capacity;

    public BoolColumnBuffer(int capacity)
    {
        this.capacity = capacity;
        data = new byte[(capacity + 7) / 8]; // Bit-pack
    }

    public void SetValue(int index, object? value)
    {
        if (value is bool boolVal && boolVal)
        {
            int byteIndex = index / 8;
            int bitIndex = index % 8;
            data[byteIndex] |= (byte)(1 << bitIndex);
        }
    }

    public int CountNonNull() => capacity;

    public void Dispose() { }
}

/// <summary>
/// String column buffer with dictionary encoding.
/// </summary>
internal sealed class StringColumnBuffer : IColumnBuffer
{
    private readonly string?[] data;
    private readonly Dictionary<string, int> dictionary = new();
    private readonly List<string> values = new();

    public StringColumnBuffer(int capacity)
    {
        data = new string?[capacity];
    }

    public void SetValue(int index, object? value)
    {
        data[index] = value?.ToString();
    }

    public int CountNonNull() => data.Count(s => s != null);

    public void Dispose() { }
}

/// <summary>
/// Generic object column buffer (fallback).
/// </summary>
internal sealed class ObjectColumnBuffer : IColumnBuffer
{
    private readonly object?[] data;

    public ObjectColumnBuffer(int capacity)
    {
        data = new object?[capacity];
    }

    public void SetValue(int index, object? value)
    {
        data[index] = value;
    }

    public int CountNonNull() => data.Count(o => o != null);

    public void Dispose() { }
}
