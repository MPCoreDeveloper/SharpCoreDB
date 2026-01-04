// <copyright file="SimdWhereFilter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Optimizations;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// SIMD-accelerated WHERE clause filtering using explicit intrinsics.
/// NativeAOT-optimized: zero reflection, zero dynamic dispatch, aggressive inlining.
/// AVX-512 enabled only for batches ≥1024 elements to amortize transition overhead.
/// </summary>
public static class SimdWhereFilter
{
    private const int AVX512_MIN_ELEMENTS = 1024;

    /// <summary>Comparison operator enumeration.</summary>
    public enum ComparisonOp : byte
    {
        /// <summary>Greater than.</summary>
        GreaterThan = 0,
        /// <summary>Less than.</summary>
        LessThan = 1,
        /// <summary>Greater than or equal.</summary>
        GreaterOrEqual = 2,
        /// <summary>Less than or equal.</summary>
        LessOrEqual = 3,
        /// <summary>Equal.</summary>
        Equal = 4,
        /// <summary>Not equal.</summary>
        NotEqual = 5
    }

    /// <summary>Filters int32 array using SIMD acceleration.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int[] FilterInt32(ReadOnlySpan<int> values, int threshold, ComparisonOp op)
    {
        var matches = new List<int>(values.Length >> 1);

        if (Avx512F.IsSupported && values.Length >= AVX512_MIN_ELEMENTS)
        {
            FilterInt32Avx512(values, threshold, op, matches);
        }
        else if (Avx2.IsSupported && values.Length >= 8)
        {
            FilterInt32Avx2(values, threshold, op, matches);
        }
        else if (Sse2.IsSupported && values.Length >= 4)
        {
            FilterInt32Sse2(values, threshold, op, matches);
        }
        else
        {
            FilterInt32Scalar(values, threshold, op, matches);
        }

        return matches.ToArray();
    }

    /// <summary>Filters int64 array using SIMD acceleration.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int[] FilterInt64(ReadOnlySpan<long> values, long threshold, ComparisonOp op)
    {
        var matches = new List<int>(values.Length >> 1);

        if (Avx512F.IsSupported && values.Length >= AVX512_MIN_ELEMENTS)
        {
            FilterInt64Avx512(values, threshold, op, matches);
        }
        else if (Avx2.IsSupported && values.Length >= 4)
        {
            FilterInt64Avx2(values, threshold, op, matches);
        }
        else if (Sse2.IsSupported && values.Length >= 2)
        {
            FilterInt64Sse2(values, threshold, op, matches);
        }
        else
        {
            FilterInt64Scalar(values, threshold, op, matches);
        }

        return matches.ToArray();
    }

    /// <summary>Filters double array using SIMD acceleration.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int[] FilterDouble(ReadOnlySpan<double> values, double threshold, ComparisonOp op)
    {
        var matches = new List<int>(values.Length >> 1);

        if (Avx512F.IsSupported && values.Length >= AVX512_MIN_ELEMENTS)
        {
            FilterDoubleAvx512(values, threshold, op, matches);
        }
        else if (Avx2.IsSupported && values.Length >= 4)
        {
            FilterDoubleAvx2(values, threshold, op, matches);
        }
        else if (Sse2.IsSupported && values.Length >= 2)
        {
            FilterDoubleSse2(values, threshold, op, matches);
        }
        else
        {
            FilterDoubleScalar(values, threshold, op, matches);
        }

        return matches.ToArray();
    }

    /// <summary>Filters decimal array using SIMD acceleration.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int[] FilterDecimal(ReadOnlySpan<decimal> values, decimal threshold, ComparisonOp op)
    {
        int len = values.Length;
        if (len > 512)
        {
            double[] doubleValues = new double[len];
            for (int i = 0; i < len; i++)
            {
                doubleValues[i] = (double)values[i];
            }
            return FilterDouble(doubleValues, (double)threshold, op);
        }
        else
        {
            Span<double> doubleValues = stackalloc double[len];
            for (int i = 0; i < len; i++)
            {
                doubleValues[i] = (double)values[i];
            }
            return FilterDouble(doubleValues, (double)threshold, op);
        }
    }

    #region AVX-512 Kernels

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterInt32Avx512(ReadOnlySpan<int> values, int threshold, ComparisonOp op, List<int> matches)
    {
        fixed (int* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~15;

            Vector512<int> thresholdVec = Vector512.Create(threshold);

            for (; i < vecLen; i += 16)
            {
                Vector512<int> vec = Avx512F.LoadVector512(ptr + i);
                Vector512<int> cmp = CompareInt32Avx512(vec, thresholdVec, op);

                ulong mask = cmp.ExtractMostSignificantBits();
                if (mask != 0)
                {
                    AccumulateIndicesAvx512(matches, i, mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarInt32(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterInt64Avx512(ReadOnlySpan<long> values, long threshold, ComparisonOp op, List<int> matches)
    {
        fixed (long* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~7;

            Vector512<long> thresholdVec = Vector512.Create(threshold);

            for (; i < vecLen; i += 8)
            {
                Vector512<long> vec = Avx512F.LoadVector512(ptr + i);
                Vector512<long> cmp = CompareInt64Avx512(vec, thresholdVec, op);

                byte mask = (byte)cmp.AsDouble().ExtractMostSignificantBits();
                if (mask != 0)
                {
                    AccumulateIndicesAvx512Byte(matches, i, mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarInt64(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterDoubleAvx512(ReadOnlySpan<double> values, double threshold, ComparisonOp op, List<int> matches)
    {
        fixed (double* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~7;

            Vector512<double> thresholdVec = Vector512.Create(threshold);

            for (; i < vecLen; i += 8)
            {
                Vector512<double> vec = Avx512F.LoadVector512(ptr + i);
                Vector512<double> cmp = CompareDoubleAvx512(vec, thresholdVec, op);

                byte mask = (byte)cmp.ExtractMostSignificantBits();
                if (mask != 0)
                {
                    AccumulateIndicesAvx512Byte(matches, i, mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarDouble(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    #endregion

    #region AVX2 Kernels

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterInt32Avx2(ReadOnlySpan<int> values, int threshold, ComparisonOp op, List<int> matches)
    {
        fixed (int* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~7;

            Vector256<int> thresholdVec = Vector256.Create(threshold);

            for (; i < vecLen; i += 8)
            {
                Vector256<int> vec = Avx.LoadVector256(ptr + i);
                Vector256<int> cmp = CompareInt32Avx2(vec, thresholdVec, op);

                int mask = Avx2.MoveMask(cmp.AsByte());
                if (mask != 0)
                {
                    AccumulateIndicesAvx2Int32(matches, i, mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarInt32(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterInt64Avx2(ReadOnlySpan<long> values, long threshold, ComparisonOp op, List<int> matches)
    {
        fixed (long* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~3;

            Vector256<long> thresholdVec = Vector256.Create(threshold);

            for (; i < vecLen; i += 4)
            {
                Vector256<long> vec = Avx.LoadVector256(ptr + i);
                Vector256<long> cmp = CompareInt64Avx2(vec, thresholdVec, op);

                int mask = Avx2.MoveMask(cmp.AsDouble());
                if (mask != 0)
                {
                    AccumulateIndicesAvx2Nibble(matches, i, (uint)mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarInt64(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterDoubleAvx2(ReadOnlySpan<double> values, double threshold, ComparisonOp op, List<int> matches)
    {
        fixed (double* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~3;

            Vector256<double> thresholdVec = Vector256.Create(threshold);

            for (; i < vecLen; i += 4)
            {
                Vector256<double> vec = Avx.LoadVector256(ptr + i);
                Vector256<double> cmp = CompareDoubleAvx2(vec, thresholdVec, op);

                int mask = Avx.MoveMask(cmp);
                if (mask != 0)
                {
                    AccumulateIndicesAvx2Nibble(matches, i, (uint)mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarDouble(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    #endregion

    #region SSE2 Kernels

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterInt32Sse2(ReadOnlySpan<int> values, int threshold, ComparisonOp op, List<int> matches)
    {
        fixed (int* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~3;

            Vector128<int> thresholdVec = Vector128.Create(threshold);

            for (; i < vecLen; i += 4)
            {
                Vector128<int> vec = Sse2.LoadVector128(ptr + i);
                Vector128<int> cmp = CompareInt32Sse2(vec, thresholdVec, op);

                int mask = Sse2.MoveMask(cmp.AsByte());
                if (mask != 0)
                {
                    AccumulateIndicesSse2Int32(matches, i, mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarInt32(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterInt64Sse2(ReadOnlySpan<long> values, long threshold, ComparisonOp op, List<int> matches)
    {
        fixed (long* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~1;

            Vector128<long> thresholdVec = Vector128.Create(threshold);

            for (; i < vecLen; i += 2)
            {
                Vector128<long> vec = Sse2.LoadVector128(ptr + i);
                Vector128<long> cmp = CompareInt64Sse2(vec, thresholdVec, op);

                int mask = Sse2.MoveMask(cmp.AsDouble());
                if (mask != 0)
                {
                    AccumulateIndicesSse2Nibble(matches, i, (uint)mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarInt64(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static unsafe void FilterDoubleSse2(ReadOnlySpan<double> values, double threshold, ComparisonOp op, List<int> matches)
    {
        fixed (double* ptr = values)
        {
            int i = 0;
            int len = values.Length;
            int vecLen = len & ~1;

            Vector128<double> thresholdVec = Vector128.Create(threshold);

            for (; i < vecLen; i += 2)
            {
                Vector128<double> vec = Sse2.LoadVector128(ptr + i);
                Vector128<double> cmp = CompareDoubleSse2(vec, thresholdVec, op);

                int mask = Sse2.MoveMask(cmp);
                if (mask != 0)
                {
                    AccumulateIndicesSse2Nibble(matches, i, (uint)mask);
                }
            }

            for (; i < len; i++)
            {
                if (CompareScalarDouble(ptr[i], threshold, op))
                {
                    matches.Add(i);
                }
            }
        }
    }

    #endregion

    #region Scalar Kernels

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FilterInt32Scalar(ReadOnlySpan<int> values, int threshold, ComparisonOp op, List<int> matches)
    {
        ref int valuesRef = ref MemoryMarshal.GetReference(values);
        int len = values.Length;

        for (int i = 0; i < len; i++)
        {
            if (CompareScalarInt32(Unsafe.Add(ref valuesRef, i), threshold, op))
            {
                matches.Add(i);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FilterInt64Scalar(ReadOnlySpan<long> values, long threshold, ComparisonOp op, List<int> matches)
    {
        ref long valuesRef = ref MemoryMarshal.GetReference(values);
        int len = values.Length;

        for (int i = 0; i < len; i++)
        {
            if (CompareScalarInt64(Unsafe.Add(ref valuesRef, i), threshold, op))
            {
                matches.Add(i);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FilterDoubleScalar(ReadOnlySpan<double> values, double threshold, ComparisonOp op, List<int> matches)
    {
        ref double valuesRef = ref MemoryMarshal.GetReference(values);
        int len = values.Length;

        for (int i = 0; i < len; i++)
        {
            if (CompareScalarDouble(Unsafe.Add(ref valuesRef, i), threshold, op))
            {
                matches.Add(i);
            }
        }
    }

    #endregion

    #region Comparison Operations (Static Dispatch)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector512<int> CompareInt32Avx512(Vector512<int> vec, Vector512<int> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Avx512F.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Avx512F.CompareLessThan(vec, threshold),
            ComparisonOp.GreaterOrEqual => Avx512F.CompareGreaterThanOrEqual(vec, threshold),
            ComparisonOp.LessOrEqual => Avx512F.CompareLessThanOrEqual(vec, threshold),
            ComparisonOp.Equal => Avx512F.CompareEqual(vec, threshold),
            _ => Avx512F.CompareNotEqual(vec, threshold)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector512<long> CompareInt64Avx512(Vector512<long> vec, Vector512<long> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Avx512F.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Avx512F.CompareLessThan(vec, threshold),
            ComparisonOp.GreaterOrEqual => Avx512F.CompareGreaterThanOrEqual(vec, threshold),
            ComparisonOp.LessOrEqual => Avx512F.CompareLessThanOrEqual(vec, threshold),
            ComparisonOp.Equal => Avx512F.CompareEqual(vec, threshold),
            _ => Avx512F.CompareNotEqual(vec, threshold)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector512<double> CompareDoubleAvx512(Vector512<double> vec, Vector512<double> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Avx512F.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Avx512F.CompareLessThan(vec, threshold),
            ComparisonOp.GreaterOrEqual => Avx512F.CompareGreaterThanOrEqual(vec, threshold),
            ComparisonOp.LessOrEqual => Avx512F.CompareLessThanOrEqual(vec, threshold),
            ComparisonOp.Equal => Avx512F.CompareEqual(vec, threshold),
            _ => Avx512F.CompareNotEqual(vec, threshold)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> CompareInt32Avx2(Vector256<int> vec, Vector256<int> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Avx2.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Avx2.CompareGreaterThan(threshold, vec),
            ComparisonOp.GreaterOrEqual => Avx2.Or(Avx2.CompareGreaterThan(vec, threshold), Avx2.CompareEqual(vec, threshold)),
            ComparisonOp.LessOrEqual => Avx2.Or(Avx2.CompareGreaterThan(threshold, vec), Avx2.CompareEqual(vec, threshold)),
            ComparisonOp.Equal => Avx2.CompareEqual(vec, threshold),
            _ => Avx2.AndNot(Avx2.CompareEqual(vec, threshold), Vector256.Create(-1))
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<long> CompareInt64Avx2(Vector256<long> vec, Vector256<long> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Avx2.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Avx2.CompareGreaterThan(threshold, vec),
            ComparisonOp.GreaterOrEqual => Avx2.Or(Avx2.CompareGreaterThan(vec, threshold), Avx2.CompareEqual(vec, threshold)),
            ComparisonOp.LessOrEqual => Avx2.Or(Avx2.CompareGreaterThan(threshold, vec), Avx2.CompareEqual(vec, threshold)),
            ComparisonOp.Equal => Avx2.CompareEqual(vec, threshold),
            _ => Avx2.AndNot(Avx2.CompareEqual(vec, threshold), Vector256.Create(-1L))
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> CompareDoubleAvx2(Vector256<double> vec, Vector256<double> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Avx.Compare(vec, threshold, FloatComparisonMode.OrderedGreaterThanNonSignaling),
            ComparisonOp.LessThan => Avx.Compare(vec, threshold, FloatComparisonMode.OrderedLessThanNonSignaling),
            ComparisonOp.GreaterOrEqual => Avx.Compare(vec, threshold, FloatComparisonMode.OrderedGreaterThanOrEqualNonSignaling),
            ComparisonOp.LessOrEqual => Avx.Compare(vec, threshold, FloatComparisonMode.OrderedLessThanOrEqualNonSignaling),
            ComparisonOp.Equal => Avx.Compare(vec, threshold, FloatComparisonMode.OrderedEqualNonSignaling),
            _ => Avx.Compare(vec, threshold, FloatComparisonMode.OrderedNotEqualNonSignaling)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> CompareInt32Sse2(Vector128<int> vec, Vector128<int> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Sse2.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Sse2.CompareGreaterThan(threshold, vec),
            ComparisonOp.GreaterOrEqual => Sse2.Or(Sse2.CompareGreaterThan(vec, threshold), Sse2.CompareEqual(vec, threshold)),
            ComparisonOp.LessOrEqual => Sse2.Or(Sse2.CompareGreaterThan(threshold, vec), Sse2.CompareEqual(vec, threshold)),
            ComparisonOp.Equal => Sse2.CompareEqual(vec, threshold),
            _ => Sse2.AndNot(Sse2.CompareEqual(vec, threshold), Vector128.Create(-1))
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<long> CompareInt64Sse2(Vector128<long> vec, Vector128<long> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Sse42.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Sse42.CompareGreaterThan(threshold, vec),
            ComparisonOp.GreaterOrEqual => Sse2.Or(Sse42.CompareGreaterThan(vec, threshold), Sse41.CompareEqual(vec, threshold)),
            ComparisonOp.LessOrEqual => Sse2.Or(Sse42.CompareGreaterThan(threshold, vec), Sse41.CompareEqual(vec, threshold)),
            ComparisonOp.Equal => Sse41.CompareEqual(vec, threshold),
            _ => Sse2.AndNot(Sse41.CompareEqual(vec, threshold), Vector128.Create(-1L))
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> CompareDoubleSse2(Vector128<double> vec, Vector128<double> threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => Sse2.CompareGreaterThan(vec, threshold),
            ComparisonOp.LessThan => Sse2.CompareLessThan(vec, threshold),
            ComparisonOp.GreaterOrEqual => Sse2.CompareGreaterThanOrEqual(vec, threshold),
            ComparisonOp.LessOrEqual => Sse2.CompareLessThanOrEqual(vec, threshold),
            ComparisonOp.Equal => Sse2.CompareEqual(vec, threshold),
            _ => Sse2.CompareNotEqual(vec, threshold)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CompareScalarInt32(int value, int threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => value > threshold,
            ComparisonOp.LessThan => value < threshold,
            ComparisonOp.GreaterOrEqual => value >= threshold,
            ComparisonOp.LessOrEqual => value <= threshold,
            ComparisonOp.Equal => value == threshold,
            _ => value != threshold
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CompareScalarInt64(long value, long threshold, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.GreaterThan => value > threshold,
            ComparisonOp.LessThan => value < threshold,
            ComparisonOp.GreaterOrEqual => value >= threshold,
            ComparisonOp.LessOrEqual => value <= threshold,
            ComparisonOp.Equal => value == threshold,
            _ => value != threshold
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CompareScalarDouble(double value, double threshold, ComparisonOp op)
    {
#pragma warning disable S1244
        return op switch
        {
            ComparisonOp.GreaterThan => value > threshold,
            ComparisonOp.LessThan => value < threshold,
            ComparisonOp.GreaterOrEqual => value >= threshold,
            ComparisonOp.LessOrEqual => value <= threshold,
            ComparisonOp.Equal => value == threshold,
            _ => value != threshold
        };
#pragma warning restore S1244
    }

    #endregion

    #region Mask Accumulation (Branch-Free)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateIndicesAvx512(List<int> matches, int baseIndex, ulong mask)
    {
        int count = BitOperations.PopCount(mask);
        matches.EnsureCapacity(matches.Count + count);

        if (Bmi1.X64.IsSupported)
        {
            while (mask != 0)
            {
                int offset = BitOperations.TrailingZeroCount(mask);
                matches.Add(baseIndex + offset);
                mask = Bmi1.X64.ResetLowestSetBit(mask);
            }
        }
        else
        {
            while (mask != 0)
            {
                int offset = BitOperations.TrailingZeroCount(mask);
                matches.Add(baseIndex + offset);
                mask &= mask - 1;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateIndicesAvx512Byte(List<int> matches, int baseIndex, byte mask)
    {
        int count = BitOperations.PopCount((uint)mask);
        matches.EnsureCapacity(matches.Count + count);

        uint workMask = mask;
        if (Bmi1.IsSupported)
        {
            while (workMask != 0)
            {
                int offset = BitOperations.TrailingZeroCount(workMask);
                matches.Add(baseIndex + offset);
                workMask = Bmi1.ResetLowestSetBit(workMask);
            }
        }
        else
        {
            while (workMask != 0)
            {
                int offset = BitOperations.TrailingZeroCount(workMask);
                matches.Add(baseIndex + offset);
                workMask &= workMask - 1;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateIndicesAvx2Int32(List<int> matches, int baseIndex, int mask)
    {
        for (int lane = 0; lane < 8; lane++)
        {
            int laneMask = (mask >> (lane << 2)) & 0xF;
            if (laneMask != 0)
            {
                matches.Add(baseIndex + lane);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateIndicesAvx2Nibble(List<int> matches, int baseIndex, uint mask)
    {
        int count = BitOperations.PopCount(mask);
        matches.EnsureCapacity(matches.Count + count);

        if (Bmi1.IsSupported)
        {
            while (mask != 0)
            {
                int offset = BitOperations.TrailingZeroCount(mask);
                matches.Add(baseIndex + offset);
                mask = Bmi1.ResetLowestSetBit(mask);
            }
        }
        else
        {
            while (mask != 0)
            {
                int offset = BitOperations.TrailingZeroCount(mask);
                matches.Add(baseIndex + offset);
                mask &= mask - 1;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateIndicesSse2Int32(List<int> matches, int baseIndex, int mask)
    {
        for (int lane = 0; lane < 4; lane++)
        {
            int laneMask = (mask >> (lane << 2)) & 0xF;
            if (laneMask != 0)
            {
                matches.Add(baseIndex + lane);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateIndicesSse2Nibble(List<int> matches, int baseIndex, uint mask)
    {
        // Reuse AVX2 nibble accumulation logic (identical implementation)
        AccumulateIndicesAvx2Nibble(matches, baseIndex, mask);
    }

    #endregion

    /// <summary>Parses a comparison operator from string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComparisonOp ParseOperator(string op)
    {
        return op.Trim() switch
        {
            ">" => ComparisonOp.GreaterThan,
            "<" => ComparisonOp.LessThan,
            ">=" => ComparisonOp.GreaterOrEqual,
            "<=" => ComparisonOp.LessOrEqual,
            "=" => ComparisonOp.Equal,
            "!=" or "<>" => ComparisonOp.NotEqual,
            _ => throw new ArgumentException($"Unsupported operator: {op}")
        };
    }
}
