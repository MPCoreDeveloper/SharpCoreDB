// <copyright file="ColumnStore.Aggregates.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.ColumnStorage;

using System.Numerics;
using System.Runtime.Intrinsics;
using SharpCoreDB.Constants;

/// <summary>
/// SIMD-optimized aggregate function implementations for ColumnStore.
/// Provides high-performance SUM, AVG, MIN, MAX operations using Vector512/Vector256/Vector128 instructions.
/// Adaptive parallel+SIMD for datasets >= 50k rows.
/// </summary>
public sealed partial class ColumnStore<T>
{
    #region Public Aggregate Methods

    /// <summary>
    /// Computes SUM aggregate using adaptive SIMD vectorization.
    /// Automatically uses parallel+SIMD for datasets >= 50k rows.
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
            Int32ColumnBuffer intBuf => (TResult)(object)SumInt32Adaptive(intBuf),
            Int64ColumnBuffer longBuf => (TResult)(object)SumInt64Adaptive(longBuf),
            DoubleColumnBuffer doubleBuf => (TResult)(object)DoubleColumnAdaptive(doubleBuf),
            DecimalColumnBuffer decBuf => (TResult)(object)SumDecimal(decBuf),
            _ => throw new NotSupportedException($"SUM not supported for column type")
        };
    }

    /// <summary>
    /// Computes AVERAGE aggregate using SIMD.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>The average of all values in the column.</returns>
    public double Average(string columnName)
    {
        if (!_columns.TryGetValue(columnName, out var buffer))
            throw new KeyNotFoundException($"Column '{columnName}' not found");

        return buffer switch
        {
            Int32ColumnBuffer intBuf => (double)SumInt32Adaptive(intBuf) / _rowCount,
            Int64ColumnBuffer longBuf => (double)SumInt64Adaptive(longBuf) / _rowCount,
            DoubleColumnBuffer doubleBuf => DoubleColumnAdaptive(doubleBuf) / _rowCount,
            DecimalColumnBuffer decBuf => (double)SumDecimal(decBuf) / _rowCount,
            _ => throw new NotSupportedException($"AVERAGE not supported for column type")
        };
    }

    /// <summary>
    /// Computes MIN aggregate using SIMD.
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
            Int32ColumnBuffer intBuf => (TResult)(object)MinInt32Adaptive(intBuf),
            Int64ColumnBuffer longBuf => (TResult)(object)MinInt64Adaptive(longBuf),
            DoubleColumnBuffer doubleBuf => (TResult)(object)MinDoubleAdaptive(doubleBuf),
            DecimalColumnBuffer decBuf => (TResult)(object)MinDecimal(decBuf),
            _ => throw new NotSupportedException($"MIN not supported for column type")
        };
    }

    /// <summary>
    /// Computes MAX aggregate using SIMD.
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
            Int32ColumnBuffer intBuf => (TResult)(object)MaxInt32Adaptive(intBuf),
            Int64ColumnBuffer longBuf => (TResult)(object)MaxInt64Adaptive(longBuf),
            DoubleColumnBuffer doubleBuf => (TResult)(object)MaxDoubleAdaptive(doubleBuf),
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

    #region Adaptive Methods (Choose Parallel vs Single-threaded)

    private static int SumInt32Adaptive(Int32ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? SumInt32ParallelSIMD(data)
            : SumInt32SIMDDirect(data);
    }

    private static long SumInt64Adaptive(Int64ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? SumInt64ParallelSIMD(data)
            : SumInt64SIMDDirect(data);
    }

    private static double DoubleColumnAdaptive(DoubleColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? SumDoubleParallelSIMD(data)
            : SumDoubleSIMDDirect(data);
    }

    private static int MinInt32Adaptive(Int32ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? MinInt32ParallelSIMD(data)
            : MinInt32SIMDDirect(data);
    }

    private static long MinInt64Adaptive(Int64ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? MinInt64ParallelSIMD(data)
            : MinInt64SIMDDirect(data);
    }

    private static double MinDoubleAdaptive(DoubleColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? MinDoubleParallelSIMD(data)
            : MinDoubleSIMDDirect(data);
    }

    private static int MaxInt32Adaptive(Int32ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? MaxInt32ParallelSIMD(data)
            : MaxInt32SIMDDirect(data);
    }

    private static long MaxInt64Adaptive(Int64ColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? MaxInt64ParallelSIMD(data)
            : MaxInt64SIMDDirect(data);
    }

    private static double MaxDoubleAdaptive(DoubleColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length >= BufferConstants.PARALLEL_SIMD_THRESHOLD
            ? MaxDoubleParallelSIMD(data)
            : MaxDoubleSIMDDirect(data);
    }

    private static decimal SumDecimal(DecimalColumnBuffer buffer) => buffer.GetData().Sum();
    private static decimal MinDecimal(DecimalColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length == 0 ? 0 : data.Min();
    }
    private static decimal MaxDecimal(DecimalColumnBuffer buffer)
    {
        var data = buffer.GetData();
        return data.Length == 0 ? 0 : data.Max();
    }

    #endregion

    #region Parallel SIMD Implementations

    private static int SumInt32ParallelSIMD(int[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return SumInt32SIMDDirect(data);

        var partialSums = new long[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            long partialSum = 0;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<int>.Count)
            {
                var vsum = Vector256<int>.Zero;
                for (; i <= end - Vector256<int>.Count; i += Vector256<int>.Count)
                    vsum = Vector256.Add(vsum, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<int>.Count; j++)
                    partialSum += vsum[j];
            }
            for (; i < end; i++) partialSum += data[i];
            partialSums[threadId] = partialSum;
        });
        return unchecked((int)partialSums.Sum());
    }

    private static long SumInt64ParallelSIMD(long[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return SumInt64SIMDDirect(data);

        var partialSums = new long[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            long partialSum = 0;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<long>.Count)
            {
                var vsum = Vector256<long>.Zero;
                for (; i <= end - Vector256<long>.Count; i += Vector256<long>.Count)
                    vsum = Vector256.Add(vsum, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<long>.Count; j++)
                    partialSum += vsum[j];
            }
            for (; i < end; i++) partialSum += data[i];
            partialSums[threadId] = partialSum;
        });
        return partialSums.Sum();
    }

    private static double SumDoubleParallelSIMD(double[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return SumDoubleSIMDDirect(data);

        var partialSums = new double[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            double partialSum = 0;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<double>.Count)
            {
                var vsum = Vector256<double>.Zero;
                for (; i <= end - Vector256<double>.Count; i += Vector256<double>.Count)
                    vsum = Vector256.Add(vsum, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<double>.Count; j++)
                    partialSum += vsum[j];
            }
            for (; i < end; i++) partialSum += data[i];
            partialSums[threadId] = partialSum;
        });
        return partialSums.Sum();
    }

    private static int MinInt32ParallelSIMD(int[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return MinInt32SIMDDirect(data);

        var partialMins = new int[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            int partialMin = int.MaxValue;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<int>.Count)
            {
                var vmin = Vector256.Create(int.MaxValue);
                for (; i <= end - Vector256<int>.Count; i += Vector256<int>.Count)
                    vmin = Vector256.Min(vmin, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<int>.Count; j++)
                    if (vmin[j] < partialMin) partialMin = vmin[j];
            }
            for (; i < end; i++)
                if (data[i] < partialMin) partialMin = data[i];
            partialMins[threadId] = partialMin;
        });
        return partialMins.Min();
    }

    private static long MinInt64ParallelSIMD(long[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return MinInt64SIMDDirect(data);

        var partialMins = new long[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            long partialMin = long.MaxValue;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<long>.Count)
            {
                var vmin = Vector256.Create(long.MaxValue);
                for (; i <= end - Vector256<long>.Count; i += Vector256<long>.Count)
                    vmin = Vector256.Min(vmin, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<long>.Count; j++)
                    if (vmin[j] < partialMin) partialMin = vmin[j];
            }
            for (; i < end; i++)
                if (data[i] < partialMin) partialMin = data[i];
            partialMins[threadId] = partialMin;
        });
        return partialMins.Min();
    }

    private static double MinDoubleParallelSIMD(double[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return MinDoubleSIMDDirect(data);

        var partialMins = new double[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            double partialMin = double.MaxValue;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<double>.Count)
            {
                var vmin = Vector256.Create(double.MaxValue);
                for (; i <= end - Vector256<double>.Count; i += Vector256<double>.Count)
                    vmin = Vector256.Min(vmin, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<double>.Count; j++)
                    if (vmin[j] < partialMin) partialMin = vmin[j];
            }
            for (; i < end; i++)
                if (data[i] < partialMin) partialMin = data[i];
            partialMins[threadId] = partialMin;
        });
        return partialMins.Min();
    }

    private static int MaxInt32ParallelSIMD(int[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return MaxInt32SIMDDirect(data);

        var partialMaxs = new int[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            int partialMax = int.MinValue;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<int>.Count)
            {
                var vmax = Vector256.Create(int.MinValue);
                for (; i <= end - Vector256<int>.Count; i += Vector256<int>.Count)
                    vmax = Vector256.Max(vmax, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<int>.Count; j++)
                    if (vmax[j] > partialMax) partialMax = vmax[j];
            }
            for (; i < end; i++)
                if (data[i] > partialMax) partialMax = data[i];
            partialMaxs[threadId] = partialMax;
        });
        return partialMaxs.Max();
    }

    private static long MaxInt64ParallelSIMD(long[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return MaxInt64SIMDDirect(data);

        var partialMaxs = new long[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            long partialMax = long.MinValue;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<long>.Count)
            {
                var vmax = Vector256.Create(long.MinValue);
                for (; i <= end - Vector256<long>.Count; i += Vector256<long>.Count)
                    vmax = Vector256.Max(vmax, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<long>.Count; j++)
                    if (vmax[j] > partialMax) partialMax = vmax[j];
            }
            for (; i < end; i++)
                if (data[i] > partialMax) partialMax = data[i];
            partialMaxs[threadId] = partialMax;
        });
        return partialMaxs.Max();
    }

    private static double MaxDoubleParallelSIMD(double[] data)
    {
        int partitionCount = Math.Min(BufferConstants.MAX_PARALLEL_PARTITIONS,
            data.Length / BufferConstants.MIN_PARALLEL_PARTITION_SIZE);
        if (partitionCount <= 1) return MaxDoubleSIMDDirect(data);

        var partialMaxs = new double[partitionCount];
        Parallel.For(0, partitionCount, threadId =>
        {
            int start = (data.Length / partitionCount) * threadId;
            int end = threadId == partitionCount - 1 ? data.Length : start + (data.Length / partitionCount);
            double partialMax = double.MinValue;
            int i = start;

            if (Vector256.IsHardwareAccelerated && (end - start) >= Vector256<double>.Count)
            {
                var vmax = Vector256.Create(double.MinValue);
                for (; i <= end - Vector256<double>.Count; i += Vector256<double>.Count)
                    vmax = Vector256.Max(vmax, Vector256.Create(data.AsSpan(i)));
                for (int j = 0; j < Vector256<double>.Count; j++)
                    if (vmax[j] > partialMax) partialMax = vmax[j];
            }
            for (; i < end; i++)
                if (data[i] > partialMax) partialMax = data[i];
            partialMaxs[threadId] = partialMax;
        });
        return partialMaxs.Max();
    }

    #endregion

    #region Single-threaded SIMD Direct Implementations

    private static int SumInt32SIMDDirect(int[] data)
    {
        int sum = 0;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<int>.Count)
        {
            var vsum = Vector256<int>.Zero;
            for (; i <= data.Length - Vector256<int>.Count; i += Vector256<int>.Count)
                vsum = Vector256.Add(vsum, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<int>.Count; j++)
                sum += vsum[j];
        }
        else if (Vector128.IsHardwareAccelerated && data.Length >= Vector128<int>.Count)
        {
            var vsum = Vector128<int>.Zero;
            for (; i <= data.Length - Vector128<int>.Count; i += Vector128<int>.Count)
                vsum = Vector128.Add(vsum, Vector128.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector128<int>.Count; j++)
                sum += vsum[j];
        }

        for (; i < data.Length; i++)
            sum += data[i];
        return sum;
    }

    private static long SumInt64SIMDDirect(long[] data)
    {
        long sum = 0;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<long>.Count)
        {
            var vsum = Vector256<long>.Zero;
            for (; i <= data.Length - Vector256<long>.Count; i += Vector256<long>.Count)
                vsum = Vector256.Add(vsum, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<long>.Count; j++)
                sum += vsum[j];
        }

        for (; i < data.Length; i++)
            sum += data[i];
        return sum;
    }

    private static double SumDoubleSIMDDirect(double[] data)
    {
        double sum = 0;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<double>.Count)
        {
            var vsum = Vector256<double>.Zero;
            for (; i <= data.Length - Vector256<double>.Count; i += Vector256<double>.Count)
                vsum = Vector256.Add(vsum, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<double>.Count; j++)
                sum += vsum[j];
        }

        for (; i < data.Length; i++)
            sum += data[i];
        return sum;
    }

    private static int MinInt32SIMDDirect(int[] data)
    {
        if (data.Length == 0) return 0;
        int min = int.MaxValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<int>.Count)
        {
            var vmin = Vector256.Create(int.MaxValue);
            for (; i <= data.Length - Vector256<int>.Count; i += Vector256<int>.Count)
                vmin = Vector256.Min(vmin, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<int>.Count; j++)
                if (vmin[j] < min) min = vmin[j];
        }

        for (; i < data.Length; i++)
            if (data[i] < min) min = data[i];
        return min;
    }

    private static long MinInt64SIMDDirect(long[] data)
    {
        if (data.Length == 0) return 0;
        long min = long.MaxValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<long>.Count)
        {
            var vmin = Vector256.Create(long.MaxValue);
            for (; i <= data.Length - Vector256<long>.Count; i += Vector256<long>.Count)
                vmin = Vector256.Min(vmin, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<long>.Count; j++)
                if (vmin[j] < min) min = vmin[j];
        }

        for (; i < data.Length; i++)
            if (data[i] < min) min = data[i];
        return min;
    }

    private static double MinDoubleSIMDDirect(double[] data)
    {
        if (data.Length == 0) return 0;
        double min = double.MaxValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<double>.Count)
        {
            var vmin = Vector256.Create(double.MaxValue);
            for (; i <= data.Length - Vector256<double>.Count; i += Vector256<double>.Count)
                vmin = Vector256.Min(vmin, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<double>.Count; j++)
                if (vmin[j] < min) min = vmin[j];
        }

        for (; i < data.Length; i++)
            if (data[i] < min) min = data[i];
        return min;
    }

    private static int MaxInt32SIMDDirect(int[] data)
    {
        if (data.Length == 0) return 0;
        int max = int.MinValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<int>.Count)
        {
            var vmax = Vector256.Create(int.MinValue);
            for (; i <= data.Length - Vector256<int>.Count; i += Vector256<int>.Count)
                vmax = Vector256.Max(vmax, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<int>.Count; j++)
                if (vmax[j] > max) max = vmax[j];
        }

        for (; i < data.Length; i++)
            if (data[i] > max) max = data[i];
        return max;
    }

    private static long MaxInt64SIMDDirect(long[] data)
    {
        if (data.Length == 0) return 0;
        long max = long.MinValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<long>.Count)
        {
            var vmax = Vector256.Create(long.MinValue);
            for (; i <= data.Length - Vector256<long>.Count; i += Vector256<long>.Count)
                vmax = Vector256.Max(vmax, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<long>.Count; j++)
                if (vmax[j] > max) max = vmax[j];
        }

        for (; i < data.Length; i++)
            if (data[i] > max) max = data[i];
        return max;
    }

    private static double MaxDoubleSIMDDirect(double[] data)
    {
        if (data.Length == 0) return 0;
        double max = double.MinValue;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<double>.Count)
        {
            var vmax = Vector256.Create(double.MinValue);
            for (; i <= data.Length - Vector256<double>.Count; i += Vector256<double>.Count)
                vmax = Vector256.Max(vmax, Vector256.Create(data.AsSpan(i)));
            for (int j = 0; j < Vector256<double>.Count; j++)
                if (vmax[j] > max) max = vmax[j];
        }

        for (; i < data.Length; i++)
            if (data[i] > max) max = data[i];
        return max;
    }

    #endregion
}
