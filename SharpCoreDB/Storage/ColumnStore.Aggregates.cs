// <copyright file="ColumnStore.Aggregates.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.ColumnStorage;

using System.Numerics;
using System.Runtime.Intrinsics;

/// <summary>
/// SIMD-optimized aggregate function implementations for ColumnStore.
/// Provides high-performance SUM, AVG, MIN, MAX operations using Vector256/Vector128 instructions.
/// </summary>
public sealed partial class ColumnStore<T>
{
    #region Public Aggregate Methods

    /// <summary>
    /// Computes SUM using SIMD vectorization.
    /// Target: Less than 0.1ms for 10k int32 values.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="columnName">The column name.</param>
    /// <returns>The sum of all values in the column.</returns>
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
    /// <param name="columnName">The column name.</param>
    /// <returns>The average of all values in the column.</returns>
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
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="columnName">The column name.</param>
    /// <returns>The minimum value in the column.</returns>
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
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="columnName">The column name.</param>
    /// <returns>The maximum value in the column.</returns>
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
    /// <param name="columnName">The column name.</param>
    /// <returns>The count of non-null values.</returns>
    public int Count(string columnName)
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer.CountNonNull();
    }

    #endregion

    #region SIMD Implementations - SUM

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
        
        // Decimal doesn't support SIMD, use LINQ sum
        return data.Sum();
    }

    #endregion

    #region SIMD Implementations - MIN

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

        return data.Min();
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

        return data.Min();
    }

    #endregion

    #region SIMD Implementations - MAX

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

        return data.Max();
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

        return data.Max();
    }

    #endregion
}
