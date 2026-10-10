// <copyright file="SimdAggregates.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Analytics.Aggregation;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// Hardware-accelerated aggregate kernels for the Analytics module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> The public aggregation contract in this module
/// (<see cref="IAggregateFunction.Aggregate(object?)"/>) takes exactly one boxed value per call, so it
/// has no sequential loop to vectorize: every value is boxed by the caller, unboxed here, and folded
/// through <see cref="Convert.ToDecimal(object)"/>. This type is the batch entry point that contract
/// lacks — the caller hands over a contiguous <see cref="ReadOnlySpan{T}"/>, and the reduction runs
/// over vector registers with no boxing and no per-value virtual dispatch.
/// </para>
/// <para>
/// <b>Compliance.</b> Follows <c>.github/SIMD_STANDARDS.md</c>: explicit
/// <c>System.Runtime.Intrinsics</c> intrinsics only (never <c>System.Numerics.Vector&lt;T&gt;</c>),
/// tiered dispatch AVX-512 → AVX2 → SSE2 → scalar, with a scalar tail that is always executed.
/// </para>
/// <para>
/// <b>Result equivalence.</b> The vector paths reproduce the scalar reference exactly, including NaN
/// propagation for <see cref="double"/> min/max (the kernels fold with <c>ConditionalSelect</c> over
/// <c>LessThan</c>/<c>GreaterThan</c>, which is the same predicate
/// <see cref="Math.Min(double, double)"/> uses — a plain <c>Vector*.Min</c> would return the non-NaN
/// operand instead, which is a *different* answer). Integer sums widen to <see cref="long"/> before
/// accumulating, so they cannot wrap the way a 32-bit accumulator would.
/// </para>
/// <para>
/// <b>Determinism caveat.</b> A vectorized reduction changes the *order* of floating-point additions,
/// so the last bit of <see cref="Sum(ReadOnlySpan{double})"/> may differ from a strictly sequential
/// scalar sum. The differential tests assert equality within 1e-9 relative tolerance for that reason,
/// and exact equality for the integer paths.
/// </para>
/// <para>
/// <b>FMA.</b> Deliberately absent from the pure Sum / Min / Max / Average reductions — none of them
/// contains a multiply-add. The one kernel that does, <see cref="SumOfSquares(ReadOnlySpan{double})"/>,
/// takes <c>Avx512F.FusedMultiplyAdd</c> on the 512-bit tier and <c>Fma.MultiplyAdd</c> on the 256-bit
/// tier (there is no 512-bit <c>Fma</c> overload in <c>System.Runtime.Intrinsics</c>; AVX-512 FMA lives
/// on <c>Avx512F</c>).
/// </para>
/// </remarks>
public static class SimdAggregates
{
    /// <summary>Minimum element count before the AVX-512 tier is entered.</summary>
    /// <remarks>
    /// Same amortization threshold <c>.github/SIMD_STANDARDS.md</c> sets for the WHERE filter: AVX-512
    /// can trigger a frequency transition, which only pays off on a long enough reduction.
    /// </remarks>
    private const int Avx512MinElements = 1024;

    /// <summary>Identifies which instruction set the reductions actually use on this machine.</summary>
    public enum SimdTier
    {
        /// <summary>Portable scalar loop (no supported vector ISA, or input shorter than one vector).</summary>
        Scalar = 0,

        /// <summary>128-bit <c>Sse2</c> tier.</summary>
        Sse2 = 1,

        /// <summary>256-bit <c>Avx2</c> tier.</summary>
        Avx2 = 2,

        /// <summary>512-bit <c>Avx512F</c> tier.</summary>
        Avx512 = 3
    }

    /// <summary>
    /// Gets the widest tier reachable on this machine, for diagnostics and benchmark attribution.
    /// </summary>
    /// <remarks>
    /// This reports capability, not the tier a specific call takes — a call on a 4-element span runs the
    /// scalar tail even on an AVX-512 host. Read it together with the length of the input.
    /// </remarks>
    public static SimdTier MaxTier =>
        Avx512F.IsSupported ? SimdTier.Avx512
        : Avx2.IsSupported ? SimdTier.Avx2
        : Sse2.IsSupported ? SimdTier.Sse2
        : SimdTier.Scalar;

    /// <summary>Gets a value indicating whether any hardware vector tier is available.</summary>
    public static bool IsHardwareAccelerated => MaxTier != SimdTier.Scalar;

    /// <summary>Gets the tier a reduction over <paramref name="length"/> elements would select.</summary>
    /// <param name="length">Number of elements in the batch.</param>
    /// <returns>The tier that would run.</returns>
    public static SimdTier TierFor(int length) =>
        length <= 0 ? SimdTier.Scalar
        : MaxTier switch
        {
            SimdTier.Avx512 when length >= Avx512MinElements => SimdTier.Avx512,
            >= SimdTier.Avx2 when length >= Vector256<long>.Count => SimdTier.Avx2,
            >= SimdTier.Sse2 when length >= Vector128<long>.Count => SimdTier.Sse2,
            _ => SimdTier.Scalar
        };

    #region Sum

    /// <summary>
    /// Sums a batch of 32-bit integers, widening to <see cref="long"/> so the result cannot wrap.
    /// </summary>
    /// <param name="values">Values to sum. An empty span yields 0.</param>
    /// <returns>The sum, accumulated in 64 bits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long Sum(ReadOnlySpan<int> values) => SumInt32(values);

    /// <summary>Sums a batch of 64-bit integers.</summary>
    /// <param name="values">Values to sum. An empty span yields 0.</param>
    /// <returns>The sum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long Sum(ReadOnlySpan<long> values) => SumInt64(values);

    /// <summary>Sums a batch of doubles.</summary>
    /// <param name="values">Values to sum. An empty span yields 0.</param>
    /// <returns>The sum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Sum(ReadOnlySpan<double> values) => SumDouble(values);

    /// <summary>
    /// Sums the squares of a batch of doubles — the kernel a two-pass variance/stddev needs.
    /// </summary>
    /// <param name="values">Values whose squares are summed. An empty span yields 0.</param>
    /// <returns>The sum of squares.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double SumOfSquares(ReadOnlySpan<double> values) => SumOfSquaresDouble(values);

    #endregion

    #region Min / Max

    /// <summary>Finds the smallest value in a batch of 32-bit integers.</summary>
    /// <param name="values">Values to scan.</param>
    /// <returns>The minimum.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Min(ReadOnlySpan<int> values) => MinInt32(values);

    /// <summary>Finds the smallest value in a batch of 64-bit integers.</summary>
    /// <param name="values">Values to scan.</param>
    /// <returns>The minimum.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long Min(ReadOnlySpan<long> values) => MinInt64(values);

    /// <summary>Finds the smallest double, propagating NaN like <see cref="Math.Min(double, double)"/>.</summary>
    /// <param name="values">Values to scan.</param>
    /// <returns>The minimum.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Min(ReadOnlySpan<double> values) => MinDouble(values);

    /// <summary>Finds the largest value in a batch of 32-bit integers.</summary>
    /// <param name="values">Values to scan.</param>
    /// <returns>The maximum.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Max(ReadOnlySpan<int> values) => MaxInt32(values);

    /// <summary>Finds the largest value in a batch of 64-bit integers.</summary>
    /// <param name="values">Values to scan.</param>
    /// <returns>The maximum.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long Max(ReadOnlySpan<long> values) => MaxInt64(values);

    /// <summary>Finds the largest double, propagating NaN like <see cref="Math.Max(double, double)"/>.</summary>
    /// <param name="values">Values to scan.</param>
    /// <returns>The maximum.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Max(ReadOnlySpan<double> values) => MaxDouble(values);

    #endregion

    #region Average

    /// <summary>Computes the arithmetic mean of a batch of 32-bit integers.</summary>
    /// <param name="values">Values to average.</param>
    /// <returns>The mean, or <see cref="double.NaN"/> when the span is empty.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Average(ReadOnlySpan<int> values) =>
        values.Length == 0 ? double.NaN : (double)SumInt32(values) / values.Length;

    /// <summary>Computes the arithmetic mean of a batch of 64-bit integers.</summary>
    /// <param name="values">Values to average.</param>
    /// <returns>The mean, or <see cref="double.NaN"/> when the span is empty.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Average(ReadOnlySpan<long> values) =>
        values.Length == 0 ? double.NaN : (double)SumInt64(values) / values.Length;

    /// <summary>Computes the arithmetic mean of a batch of doubles.</summary>
    /// <param name="values">Values to average.</param>
    /// <returns>The mean, or <see cref="double.NaN"/> when the span is empty.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Average(ReadOnlySpan<double> values) =>
        values.Length == 0 ? double.NaN : SumDouble(values) / values.Length;

    // ---------------------------------------------------------------------------------------------
    // Horizontal reductions and scalar reference paths.
    //
    // Every tier method finishes through the matching scalar helper, so the scalar tail is always
    // executed and its logic is defined exactly once.
    // ---------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumOf(Vector128<long> v) => v[0] + v[1];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumOf(Vector256<long> v) => v[0] + v[1] + v[2] + v[3];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumOf(Vector512<long> v) => SumOf(v.GetLower()) + SumOf(v.GetUpper());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SumOfD(Vector128<double> v) => v[0] + v[1];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SumOfD(Vector256<double> v) => v[0] + v[1] + v[2] + v[3];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SumOfD(Vector512<double> v) => SumOfD(v.GetLower()) + SumOfD(v.GetUpper());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumInt32Scalar(ReadOnlySpan<int> values, int start)
    {
        var sum = 0L;
        for (var i = start; i < values.Length; i++)
        {
            sum += values[i];
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumInt64Scalar(ReadOnlySpan<long> values, int start)
    {
        var sum = 0L;
        for (var i = start; i < values.Length; i++)
        {
            sum += values[i];
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SumDoubleScalar(ReadOnlySpan<double> values, int start)
    {
        var sum = 0.0;
        for (var i = start; i < values.Length; i++)
        {
            sum += values[i];
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SumOfSquaresScalar(ReadOnlySpan<double> values, int start)
    {
        var sum = 0.0;
        for (var i = start; i < values.Length; i++)
        {
            sum += values[i] * values[i];
        }
        return sum;
    }

    /// <summary>
    /// Scalar reference for the min/max reductions. This is the definition the vector tiers must match,
    /// NaN handling included: <see cref="Math.Min(double, double)"/> propagates NaN, so a NaN anywhere
    /// in the batch makes the scalar answer NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MinMaxInt32Scalar(ReadOnlySpan<int> values, int start, int seed, bool isMax)
    {
        var best = seed;
        for (var i = start; i < values.Length; i++)
        {
            best = isMax ? Math.Max(best, values[i]) : Math.Min(best, values[i]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long MinMaxInt64Scalar(ReadOnlySpan<long> values, int start, long seed, bool isMax)
    {
        var best = seed;
        for (var i = start; i < values.Length; i++)
        {
            best = isMax ? Math.Max(best, values[i]) : Math.Min(best, values[i]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double MinMaxDoubleScalar(ReadOnlySpan<double> values, int start, double seed, bool isMax)
    {
        var best = seed;
        for (var i = start; i < values.Length; i++)
        {
            best = isMax ? Math.Max(best, values[i]) : Math.Min(best, values[i]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HorizontalMinMax(Vector128<int> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector128<int>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HorizontalMinMax(Vector256<int> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector256<int>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HorizontalMinMax(Vector512<int> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector512<int>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HorizontalMinMax(Vector128<long> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector128<long>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HorizontalMinMax(Vector256<long> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector256<long>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HorizontalMinMax(Vector512<long> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector512<long>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double HorizontalMinMax(Vector128<double> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector128<double>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double HorizontalMinMax(Vector256<double> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector256<double>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double HorizontalMinMax(Vector512<double> v, bool isMax)
    {
        var best = v[0];
        for (var k = 1; k < Vector512<double>.Count; k++)
        {
            best = isMax ? Math.Max(best, v[k]) : Math.Min(best, v[k]);
        }
        return best;
    }

    // ---------------------------------------------------------------------------------------------
    // int32 sum: tiered dispatch. The 64-bit accumulator is deliberately used in every tier, so the
    // vector result and the scalar result agree even for batches that would overflow an int32 sum.
    // ---------------------------------------------------------------------------------------------

    private static long SumInt32(ReadOnlySpan<int> values)
    {
        if (Avx512F.IsSupported && Avx2.IsSupported && values.Length >= Avx512MinElements)
        {
            return SumInt32Avx512(values);
        }
        if (Avx2.IsSupported && values.Length >= Vector256<int>.Count)
        {
            return SumInt32Avx2(values);
        }
        if (Sse2.IsSupported && values.Length >= Vector128<int>.Count)
        {
            return SumInt32Sse2(values);
        }
        return SumInt32Scalar(values, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SumInt32Avx512(ReadOnlySpan<int> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var accLo = Vector512<long>.Zero;
        var accHi = Vector512<long>.Zero;
        var i = 0;
        var width = Vector512<int>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector512.LoadUnsafe(ref Unsafe.Add(ref start, i));
            accLo += Vector512.WidenLower(v);
            accHi += Vector512.WidenUpper(v);
        }
        return SumOf(accLo + accHi) + SumInt32Scalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SumInt32Avx2(ReadOnlySpan<int> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var accLo = Vector256<long>.Zero;
        var accHi = Vector256<long>.Zero;
        var i = 0;
        var width = Vector256<int>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
            accLo += Vector256.WidenLower(v);
            accHi += Vector256.WidenUpper(v);
        }
        return SumOf(accLo + accHi) + SumInt32Scalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SumInt32Sse2(ReadOnlySpan<int> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var accLo = Vector128<long>.Zero;
        var accHi = Vector128<long>.Zero;
        var i = 0;
        var width = Vector128<int>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref start, i));
            accLo += Vector128.WidenLower(v);
            accHi += Vector128.WidenUpper(v);
        }
        return SumOf(accLo + accHi) + SumInt32Scalar(values, i);
    }

    // ---------------------------------------------------------------------------------------------
    // int64 sum: tiered dispatch. 64-bit lanes, no widening needed.
    // ---------------------------------------------------------------------------------------------

    private static long SumInt64(ReadOnlySpan<long> values)
    {
        if (Avx512F.IsSupported && values.Length >= Avx512MinElements)
        {
            return SumInt64Avx512(values);
        }
        if (Avx2.IsSupported && values.Length >= Vector256<long>.Count)
        {
            return SumInt64Avx2(values);
        }
        if (Sse2.IsSupported && values.Length >= Vector128<long>.Count)
        {
            return SumInt64Sse2(values);
        }
        return SumInt64Scalar(values, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SumInt64Avx512(ReadOnlySpan<long> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector512<long>.Zero;
        var i = 0;
        var width = Vector512<long>.Count;
        for (; i + width <= values.Length; i += width)
        {
            acc += Vector512.LoadUnsafe(ref Unsafe.Add(ref start, i));
        }
        return SumOf(Vector256.Add(acc.GetLower(), acc.GetUpper())) + SumInt64Scalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SumInt64Avx2(ReadOnlySpan<long> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector256<long>.Zero;
        var i = 0;
        var width = Vector256<long>.Count;
        for (; i + width <= values.Length; i += width)
        {
            acc += Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
        }
        return SumOf(acc) + SumInt64Scalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SumInt64Sse2(ReadOnlySpan<long> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector128<long>.Zero;
        var i = 0;
        var width = Vector128<long>.Count;
        for (; i + width <= values.Length; i += width)
        {
            acc += Vector128.LoadUnsafe(ref Unsafe.Add(ref start, i));
        }
        return SumOf(acc) + SumInt64Scalar(values, i);
    }

    // ---------------------------------------------------------------------------------------------
    // double sum: tiered dispatch.
    // ---------------------------------------------------------------------------------------------

    private static double SumDouble(ReadOnlySpan<double> values)
    {
        if (Avx512F.IsSupported && values.Length >= Avx512MinElements)
        {
            return SumDoubleAvx512(values);
        }
        if (Avx2.IsSupported && values.Length >= Vector256<double>.Count)
        {
            return SumDoubleAvx2(values);
        }
        if (Sse2.IsSupported && values.Length >= Vector128<double>.Count)
        {
            return SumDoubleSse2(values);
        }
        return SumDoubleScalar(values, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double SumDoubleAvx512(ReadOnlySpan<double> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector512<double>.Zero;
        var i = 0;
        var width = Vector512<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            acc += Vector512.LoadUnsafe(ref Unsafe.Add(ref start, i));
        }
        return SumOfD(Vector256.Add(acc.GetLower(), acc.GetUpper())) + SumDoubleScalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double SumDoubleAvx2(ReadOnlySpan<double> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector256<double>.Zero;
        var i = 0;
        var width = Vector256<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            acc += Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
        }
        return SumOfD(acc) + SumDoubleScalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double SumDoubleSse2(ReadOnlySpan<double> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector128<double>.Zero;
        var i = 0;
        var width = Vector128<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            acc += Vector128.LoadUnsafe(ref Unsafe.Add(ref start, i));
        }
        return SumOfD(acc) + SumDoubleScalar(values, i);
    }

    // ---------------------------------------------------------------------------------------------
    // double sum of squares: tiered dispatch. The only kernel with a multiply-add, so it is the only
    // one that takes the FMA path when the CPU exposes it.
    // ---------------------------------------------------------------------------------------------

    private static double SumOfSquaresDouble(ReadOnlySpan<double> values)
    {
        if (Avx512F.IsSupported && Fma.IsSupported && values.Length >= Avx512MinElements)
        {
            return SumOfSquaresDoubleAvx512(values);
        }
        if (Avx2.IsSupported && values.Length >= Vector256<double>.Count)
        {
            return SumOfSquaresDoubleAvx2(values);
        }
        if (Sse2.IsSupported && values.Length >= Vector128<double>.Count)
        {
            return SumOfSquaresDoubleSse2(values);
        }
        return SumOfSquaresScalar(values, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double SumOfSquaresDoubleAvx512(ReadOnlySpan<double> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector512<double>.Zero;
        var i = 0;
        var width = Vector512<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector512.LoadUnsafe(ref Unsafe.Add(ref start, i));
            acc = Avx512F.FusedMultiplyAdd(v, v, acc);
        }
        return SumOfD(Vector256.Add(acc.GetLower(), acc.GetUpper())) + SumOfSquaresScalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double SumOfSquaresDoubleAvx2(ReadOnlySpan<double> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector256<double>.Zero;
        var i = 0;
        var width = Vector256<double>.Count;
        if (Fma.IsSupported)
        {
            for (; i + width <= values.Length; i += width)
            {
                var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
                acc = Fma.MultiplyAdd(v, v, acc);
            }
        }
        else
        {
            for (; i + width <= values.Length; i += width)
            {
                var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
                acc += v * v;
            }
        }
        return SumOfD(acc) + SumOfSquaresScalar(values, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double SumOfSquaresDoubleSse2(ReadOnlySpan<double> values)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector128<double>.Zero;
        var i = 0;
        var width = Vector128<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref start, i));
            acc += v * v;
        }
        return SumOfD(acc) + SumOfSquaresScalar(values, i);
    }

    // ---------------------------------------------------------------------------------------------
    // min/max: tiered dispatch. The fold uses ConditionalSelect over LessThan/GreaterThan rather than
    // Vector*.Min/Max, which returns whichever operand is not NaN. For the double kernels that would be
    // a different answer than Math.Min/Math.Max, which propagate NaN; the doubles therefore also carry
    // a NaN mask (see MinMaxDoubleAvx512) so a single NaN in the batch yields NaN, like the scalar
    // reference. Integer comparisons are an exact ordering, so the int/long kernels need no mask.
    // ---------------------------------------------------------------------------------------------

    private static int MinInt32(ReadOnlySpan<int> values) => MinMaxInt32(values, false);

    private static int MaxInt32(ReadOnlySpan<int> values) => MinMaxInt32(values, true);

    private static int MinMaxInt32(ReadOnlySpan<int> values, bool isMax)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("Cannot reduce an empty span.", nameof(values));
        }
        if (Avx512F.IsSupported && values.Length >= Avx512MinElements)
        {
            return MinMaxInt32Avx512(values, isMax);
        }
        if (Avx2.IsSupported && values.Length >= Vector256<int>.Count)
        {
            return MinMaxInt32Avx2(values, isMax);
        }
        if (Sse2.IsSupported && values.Length >= Vector128<int>.Count)
        {
            return MinMaxInt32Sse2(values, isMax);
        }
        return MinMaxInt32Scalar(values, 0, values[0], isMax);
    }

    // Every vector loop below is only entered when at least one full vector fits, so all lanes are
    // written from real elements and the horizontal result is never the initial seed.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int MinMaxInt32Avx512(ReadOnlySpan<int> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector512.Create(isMax ? int.MinValue : int.MaxValue);
        var i = 0;
        var width = Vector512<int>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector512.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector512.GreaterThan(v, acc) : Vector512.LessThan(v, acc);
            acc = Vector512.ConditionalSelect(better, v, acc);
        }
        return MinMaxInt32Scalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int MinMaxInt32Avx2(ReadOnlySpan<int> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector256.Create(isMax ? int.MinValue : int.MaxValue);
        var i = 0;
        var width = Vector256<int>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector256.GreaterThan(v, acc) : Vector256.LessThan(v, acc);
            acc = Vector256.ConditionalSelect(better, v, acc);
        }
        return MinMaxInt32Scalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int MinMaxInt32Sse2(ReadOnlySpan<int> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector128.Create(isMax ? int.MinValue : int.MaxValue);
        var i = 0;
        var width = Vector128<int>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector128.GreaterThan(v, acc) : Vector128.LessThan(v, acc);
            acc = Vector128.ConditionalSelect(better, v, acc);
        }
        return MinMaxInt32Scalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    private static long MinInt64(ReadOnlySpan<long> values) => MinMaxInt64(values, false);

    private static long MaxInt64(ReadOnlySpan<long> values) => MinMaxInt64(values, true);

    private static long MinMaxInt64(ReadOnlySpan<long> values, bool isMax)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("Cannot reduce an empty span.", nameof(values));
        }
        if (Avx512F.IsSupported && values.Length >= Avx512MinElements)
        {
            return MinMaxInt64Avx512(values, isMax);
        }
        if (Avx2.IsSupported && values.Length >= Vector256<long>.Count)
        {
            return MinMaxInt64Avx2(values, isMax);
        }
        if (Sse2.IsSupported && values.Length >= Vector128<long>.Count)
        {
            return MinMaxInt64Sse2(values, isMax);
        }
        return MinMaxInt64Scalar(values, 0, values[0], isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long MinMaxInt64Avx512(ReadOnlySpan<long> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector512.Create(isMax ? long.MinValue : long.MaxValue);
        var i = 0;
        var width = Vector512<long>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector512.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector512.GreaterThan(v, acc) : Vector512.LessThan(v, acc);
            acc = Vector512.ConditionalSelect(better, v, acc);
        }
        return MinMaxInt64Scalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long MinMaxInt64Avx2(ReadOnlySpan<long> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector256.Create(isMax ? long.MinValue : long.MaxValue);
        var i = 0;
        var width = Vector256<long>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector256.GreaterThan(v, acc) : Vector256.LessThan(v, acc);
            acc = Vector256.ConditionalSelect(better, v, acc);
        }
        return MinMaxInt64Scalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long MinMaxInt64Sse2(ReadOnlySpan<long> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector128.Create(isMax ? long.MinValue : long.MaxValue);
        var i = 0;
        var width = Vector128<long>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector128.GreaterThan(v, acc) : Vector128.LessThan(v, acc);
            acc = Vector128.ConditionalSelect(better, v, acc);
        }
        return MinMaxInt64Scalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    private static double MinDouble(ReadOnlySpan<double> values) => MinMaxDouble(values, false);

    private static double MaxDouble(ReadOnlySpan<double> values) => MinMaxDouble(values, true);

    private static double MinMaxDouble(ReadOnlySpan<double> values, bool isMax)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("Cannot reduce an empty span.", nameof(values));
        }
        if (Avx512F.IsSupported && values.Length >= Avx512MinElements)
        {
            return MinMaxDoubleAvx512(values, isMax);
        }
        if (Avx2.IsSupported && values.Length >= Vector256<double>.Count)
        {
            return MinMaxDoubleAvx2(values, isMax);
        }
        if (Sse2.IsSupported && values.Length >= Vector128<double>.Count)
        {
            return MinMaxDoubleSse2(values, isMax);
        }
        return MinMaxDoubleScalar(values, 0, values[0], isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double MinMaxDoubleAvx512(ReadOnlySpan<double> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector512.Create(isMax ? double.NegativeInfinity : double.PositiveInfinity);
        var nan = Vector512<double>.Zero;
        var i = 0;
        var width = Vector512<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector512.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector512.GreaterThan(v, acc) : Vector512.LessThan(v, acc);
            acc = Vector512.ConditionalSelect(better, v, acc);
            nan = Vector512.BitwiseOr(nan, ~Vector512.Equals(v, v));
        }
        if (!Vector512.EqualsAll(nan, Vector512<double>.Zero))
        {
            return double.NaN;
        }
        return MinMaxDoubleScalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double MinMaxDoubleAvx2(ReadOnlySpan<double> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector256.Create(isMax ? double.NegativeInfinity : double.PositiveInfinity);
        var nan = Vector256<double>.Zero;
        var i = 0;
        var width = Vector256<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector256.GreaterThan(v, acc) : Vector256.LessThan(v, acc);
            acc = Vector256.ConditionalSelect(better, v, acc);
            nan = Vector256.BitwiseOr(nan, ~Vector256.Equals(v, v));
        }
        if (!Vector256.EqualsAll(nan, Vector256<double>.Zero))
        {
            return double.NaN;
        }
        return MinMaxDoubleScalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double MinMaxDoubleSse2(ReadOnlySpan<double> values, bool isMax)
    {
        ref var start = ref MemoryMarshal.GetReference(values);
        var acc = Vector128.Create(isMax ? double.NegativeInfinity : double.PositiveInfinity);
        var nan = Vector128<double>.Zero;
        var i = 0;
        var width = Vector128<double>.Count;
        for (; i + width <= values.Length; i += width)
        {
            var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref start, i));
            var better = isMax ? Vector128.GreaterThan(v, acc) : Vector128.LessThan(v, acc);
            acc = Vector128.ConditionalSelect(better, v, acc);
            nan = Vector128.BitwiseOr(nan, ~Vector128.Equals(v, v));
        }
        if (!Vector128.EqualsAll(nan, Vector128<double>.Zero))
        {
            return double.NaN;
        }
        return MinMaxDoubleScalar(values, i, HorizontalMinMax(acc, isMax), isMax);
    }

    #endregion
}
