// <copyright file="SimdFilter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Query;

using System;
using System.Numerics; // BitOperations only — System.Numerics.Vector<T> is banned by .github/SIMD_STANDARDS.md
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// SIMD-accelerated filtering for WHERE clause evaluation.
/// Uses explicit multi-tier intrinsics (AVX-512 → AVX2 → SSE2 → scalar) as required by
/// <c>.github/SIMD_STANDARDS.md</c>; the portable <c>System.Numerics.Vector&lt;T&gt;</c> API is
/// deliberately not used, so codegen is deterministic under NativeAOT.
///
/// ✅ SCDB Phase 7.1: Advanced Query Optimization - SIMD Filtering
///
/// Purpose:
/// - Vectorized comparisons for int32 and double columns
/// - Batch processing for cache efficiency
/// - Mask-based index extraction (<c>ExtractMostSignificantBits</c> + bit scan) instead of per-lane probing
/// - Scalar tail for every remainder, and a full scalar path when no x86 tier is available
/// </summary>
public static class SimdFilter
{
    /// <summary>AVX-512 element floor below which the 512-bit tier is not worth its frequency cost.</summary>
    private const int Avx512MinElements = 1024;

    /// <summary>Mask predicate applied by the tiered kernels.</summary>
    private enum MaskOp
    {
        /// <summary>Matches <c>value == lo</c>.</summary>
        Equal,

        /// <summary>Matches <c>value &gt; lo</c>.</summary>
        GreaterThan,

        /// <summary>Matches the half-open range <c>lo &lt;= value &lt; hi</c>.</summary>
        Range,
    }

    /// <summary>Gets whether an x86 SIMD tier is available on this hardware.</summary>
    public static bool IsSimdSupported => Avx512F.IsSupported || Avx2.IsSupported || Sse2.IsSupported;

    /// <summary>Gets the SIMD vector width, in elements of <typeparamref name="T"/>, for the widest supported tier.</summary>
    /// <typeparam name="T">Element type to size the vector for.</typeparam>
    /// <returns>Element count of the widest supported vector, or 1 when only the scalar path is available.</returns>
    public static int VectorSize<T>()
        where T : struct
        => Avx512F.IsSupported ? Vector512<T>.Count
            : Avx2.IsSupported ? Vector256<T>.Count
            : Sse2.IsSupported ? Vector128<T>.Count
            : 1;

    // ========================================
    // Integer Filters
    // ========================================

    /// <summary>Filters integers where value == target (SIMD accelerated).</summary>
    /// <param name="values">Source values.</param>
    /// <param name="target">Value to match.</param>
    /// <returns>Indices of matching elements, ascending.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int[] FilterEquals(int[] values, int target) => FilterInt32(values, target, 0, MaskOp.Equal);

    /// <summary>Filters integers where value &gt; threshold (SIMD accelerated).</summary>
    /// <param name="values">Source values.</param>
    /// <param name="threshold">Exclusive lower bound.</param>
    /// <returns>Indices of matching elements, ascending.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int[] FilterGreaterThan(int[] values, int threshold) => FilterInt32(values, threshold, 0, MaskOp.GreaterThan);

    /// <summary>Filters integers in the half-open range [min, max) (SIMD accelerated).</summary>
    /// <param name="values">Source values.</param>
    /// <param name="min">Inclusive lower bound.</param>
    /// <param name="max">Exclusive upper bound.</param>
    /// <returns>Indices of matching elements, ascending.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int[] FilterRange(int[] values, int min, int max) => FilterInt32(values, min, max, MaskOp.Range);

    // ========================================
    // Double Filters
    // ========================================

    /// <summary>Filters doubles where value &gt; threshold (SIMD accelerated).</summary>
    /// <param name="values">Source values.</param>
    /// <param name="threshold">Exclusive lower bound.</param>
    /// <returns>Indices of matching elements, ascending.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int[] FilterGreaterThan(double[] values, double threshold) => FilterDouble(values, threshold, 0, MaskOp.GreaterThan);

    /// <summary>Filters doubles in the half-open range [min, max) (SIMD accelerated).</summary>
    /// <param name="values">Source values.</param>
    /// <param name="min">Inclusive lower bound.</param>
    /// <param name="max">Exclusive upper bound.</param>
    /// <returns>Indices of matching elements, ascending.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int[] FilterRange(double[] values, double min, double max) => FilterDouble(values, min, max, MaskOp.Range);

    // ========================================
    // Index-buffer entry points
    // ========================================

    private static int[] FilterInt32(int[] values, int lo, int hi, MaskOp op)
    {
        var indices = new int[values.Length];
        int count = FilterInt32Core(values, lo, hi, op, indices);

        if (count != indices.Length)
        {
            Array.Resize(ref indices, count);
        }

        return indices;
    }

    private static int[] FilterDouble(double[] values, double lo, double hi, MaskOp op)
    {
        var indices = new int[values.Length];
        int count = FilterDoubleCore(values, lo, hi, op, indices);

        if (count != indices.Length)
        {
            Array.Resize(ref indices, count);
        }

        return indices;
    }

    // ========================================
    // int32 tiered scan
    // ========================================

    /// <summary>Scans int32 values with the widest supported tier; the scalar tail is unconditional.</summary>
    private static int FilterInt32Core(int[] values, int lo, int hi, MaskOp op, int[] indices)
    {
        int len = values.Length;
        int count = 0;
        int i = 0;

        if (Avx512F.IsSupported && len >= Avx512MinElements)
        {
            count = FilterInt32Avx512(values, lo, hi, op, indices, count, ref i);
        }

        if (Avx2.IsSupported && len - i >= Vector256<int>.Count)
        {
            count = FilterInt32Avx2(values, lo, hi, op, indices, count, ref i);
        }

        if (Sse2.IsSupported && len - i >= Vector128<int>.Count)
        {
            count = FilterInt32Sse2(values, lo, hi, op, indices, count, ref i);
        }

        for (; i < len; i++)
        {
            if (Matches(values[i], lo, hi, op))
            {
                indices[count++] = i;
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int FilterInt32Avx512(int[] values, int lo, int hi, MaskOp op, int[] indices, int count, ref int i)
    {
        ref int reference = ref MemoryMarshal.GetReference<int>(values);
        Vector512<int> loVector = Vector512.Create(lo);
        Vector512<int> hiVector = Vector512.Create(hi);

        for (; i <= values.Length - Vector512<int>.Count; i += Vector512<int>.Count)
        {
            Vector512<int> vector = Vector512.LoadUnsafe(ref Unsafe.Add(ref reference, i));
            count = Accumulate(Compare(vector, loVector, hiVector, op).ExtractMostSignificantBits(), i, indices, count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int FilterInt32Avx2(int[] values, int lo, int hi, MaskOp op, int[] indices, int count, ref int i)
    {
        ref int reference = ref MemoryMarshal.GetReference<int>(values);
        Vector256<int> loVector = Vector256.Create(lo);
        Vector256<int> hiVector = Vector256.Create(hi);

        for (; i <= values.Length - Vector256<int>.Count; i += Vector256<int>.Count)
        {
            Vector256<int> vector = Vector256.LoadUnsafe(ref Unsafe.Add(ref reference, i));
            count = Accumulate(Compare(vector, loVector, hiVector, op).ExtractMostSignificantBits(), i, indices, count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int FilterInt32Sse2(int[] values, int lo, int hi, MaskOp op, int[] indices, int count, ref int i)
    {
        ref int reference = ref MemoryMarshal.GetReference<int>(values);
        Vector128<int> loVector = Vector128.Create(lo);
        Vector128<int> hiVector = Vector128.Create(hi);

        for (; i <= values.Length - Vector128<int>.Count; i += Vector128<int>.Count)
        {
            Vector128<int> vector = Vector128.LoadUnsafe(ref Unsafe.Add(ref reference, i));
            count = Accumulate(Compare(vector, loVector, hiVector, op).ExtractMostSignificantBits(), i, indices, count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Compare(Vector128<int> value, Vector128<int> lo, Vector128<int> hi, MaskOp op) => op switch
    {
        MaskOp.Equal => Vector128.Equals(value, lo),
        MaskOp.GreaterThan => Vector128.GreaterThan(value, lo),
        _ => Vector128.BitwiseAnd(Vector128.GreaterThanOrEqual(value, lo), Vector128.LessThan(value, hi)),
    };

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> Compare(Vector256<int> value, Vector256<int> lo, Vector256<int> hi, MaskOp op) => op switch
    {
        MaskOp.Equal => Vector256.Equals(value, lo),
        MaskOp.GreaterThan => Vector256.GreaterThan(value, lo),
        _ => Vector256.BitwiseAnd(Vector256.GreaterThanOrEqual(value, lo), Vector256.LessThan(value, hi)),
    };

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static Vector512<int> Compare(Vector512<int> value, Vector512<int> lo, Vector512<int> hi, MaskOp op) => op switch
    {
        MaskOp.Equal => Vector512.Equals(value, lo),
        MaskOp.GreaterThan => Vector512.GreaterThan(value, lo),
        _ => Vector512.BitwiseAnd(Vector512.GreaterThanOrEqual(value, lo), Vector512.LessThan(value, hi)),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Matches(int value, int lo, int hi, MaskOp op) => op switch
    {
        MaskOp.Equal => value == lo,
        MaskOp.GreaterThan => value > lo,
        _ => value >= lo && value < hi,
    };

    // ========================================
    // double tiered scan
    // ========================================

    /// <summary>Scans double values with the widest supported tier; the scalar tail is unconditional.</summary>
    private static int FilterDoubleCore(double[] values, double lo, double hi, MaskOp op, int[] indices)
    {
        int len = values.Length;
        int count = 0;
        int i = 0;

        if (Avx512F.IsSupported && len >= Avx512MinElements)
        {
            count = FilterDoubleAvx512(values, lo, hi, op, indices, count, ref i);
        }

        if (Avx2.IsSupported && len - i >= Vector256<double>.Count)
        {
            count = FilterDoubleAvx2(values, lo, hi, op, indices, count, ref i);
        }

        if (Sse2.IsSupported && len - i >= Vector128<double>.Count)
        {
            count = FilterDoubleSse2(values, lo, hi, op, indices, count, ref i);
        }

        for (; i < len; i++)
        {
            if (Matches(values[i], lo, hi, op))
            {
                indices[count++] = i;
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int FilterDoubleAvx512(double[] values, double lo, double hi, MaskOp op, int[] indices, int count, ref int i)
    {
        ref double reference = ref MemoryMarshal.GetReference<double>(values);
        Vector512<double> loVector = Vector512.Create(lo);
        Vector512<double> hiVector = Vector512.Create(hi);

        for (; i <= values.Length - Vector512<double>.Count; i += Vector512<double>.Count)
        {
            Vector512<double> vector = Vector512.LoadUnsafe(ref Unsafe.Add(ref reference, i));
            count = Accumulate(Compare(vector, loVector, hiVector, op).ExtractMostSignificantBits(), i, indices, count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int FilterDoubleAvx2(double[] values, double lo, double hi, MaskOp op, int[] indices, int count, ref int i)
    {
        ref double reference = ref MemoryMarshal.GetReference<double>(values);
        Vector256<double> loVector = Vector256.Create(lo);
        Vector256<double> hiVector = Vector256.Create(hi);

        for (; i <= values.Length - Vector256<double>.Count; i += Vector256<double>.Count)
        {
            Vector256<double> vector = Vector256.LoadUnsafe(ref Unsafe.Add(ref reference, i));
            count = Accumulate(Compare(vector, loVector, hiVector, op).ExtractMostSignificantBits(), i, indices, count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int FilterDoubleSse2(double[] values, double lo, double hi, MaskOp op, int[] indices, int count, ref int i)
    {
        ref double reference = ref MemoryMarshal.GetReference<double>(values);
        Vector128<double> loVector = Vector128.Create(lo);
        Vector128<double> hiVector = Vector128.Create(hi);

        for (; i <= values.Length - Vector128<double>.Count; i += Vector128<double>.Count)
        {
            Vector128<double> vector = Vector128.LoadUnsafe(ref Unsafe.Add(ref reference, i));
            count = Accumulate(Compare(vector, loVector, hiVector, op).ExtractMostSignificantBits(), i, indices, count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> Compare(Vector128<double> value, Vector128<double> lo, Vector128<double> hi, MaskOp op) => op switch
    {
        MaskOp.Equal => Vector128.Equals(value, lo),
        MaskOp.GreaterThan => Vector128.GreaterThan(value, lo),
        _ => Vector128.BitwiseAnd(Vector128.GreaterThanOrEqual(value, lo), Vector128.LessThan(value, hi)),
    };

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Compare(Vector256<double> value, Vector256<double> lo, Vector256<double> hi, MaskOp op) => op switch
    {
        MaskOp.Equal => Vector256.Equals(value, lo),
        MaskOp.GreaterThan => Vector256.GreaterThan(value, lo),
        _ => Vector256.BitwiseAnd(Vector256.GreaterThanOrEqual(value, lo), Vector256.LessThan(value, hi)),
    };

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static Vector512<double> Compare(Vector512<double> value, Vector512<double> lo, Vector512<double> hi, MaskOp op) => op switch
    {
        MaskOp.Equal => Vector512.Equals(value, lo),
        MaskOp.GreaterThan => Vector512.GreaterThan(value, lo),
        _ => Vector512.BitwiseAnd(Vector512.GreaterThanOrEqual(value, lo), Vector512.LessThan(value, hi)),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Matches(double value, double lo, double hi, MaskOp op) => op switch
    {
        MaskOp.Equal => value == lo,
        MaskOp.GreaterThan => value > lo,
        _ => value >= lo && value < hi,
    };

    // ========================================
    // Mask accumulation
    // ========================================

    /// <summary>Appends every set bit of a 128/256-bit comparison mask (block offsets), returning the new match count.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static int Accumulate(uint mask, int blockStart, int[] indices, int count)
    {
        while (mask != 0)
        {
            indices[count++] = blockStart + BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
        }

        return count;
    }

    /// <summary>Appends every set bit of a 512-bit comparison mask (block offsets), returning the new match count.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static int Accumulate(ulong mask, int blockStart, int[] indices, int count)
    {
        while (mask != 0)
        {
            indices[count++] = blockStart + BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
        }

        return count;
    }
}
