// <copyright file="JitOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Runtime.CompilerServices;

namespace SharpCoreDB.Optimization;

/// <summary>
/// Phase 2E Monday: JIT Optimization & Loop Unrolling
/// 
/// Helps JIT compiler generate optimal code by:
/// - Exposing instruction-level parallelism through loop unrolling
/// - Reducing branch prediction pressure
/// - Improving register allocation
/// - Enabling better CPU utilization
/// 
/// Modern CPUs can execute 3-6 instructions per cycle, but sequential loops
/// only generate 1-2 instructions per cycle. Unrolling helps JIT see parallelism.
/// 
/// Expected Improvement: 1.5-1.8x for CPU-bound operations
/// </summary>
public static class JitOptimizer
{
    /// <summary>
    /// Unroll-2 pattern: Process 2 items per iteration.
    /// Good for operations with moderate latency.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Sum_Unroll2(ReadOnlySpan<int> data)
    {
        long sum = 0;
        int i = 0;

        // Process 2 items per iteration (exposure some parallelism)
        for (; i < data.Length - 1; i += 2)
        {
            sum += data[i];
            sum += data[i + 1];
        }

        // Remainder
        if (i < data.Length)
            sum += data[i];

        return sum;
    }

    /// <summary>
    /// Unroll-4 pattern: Process 4 items per iteration.
    /// Good for most operations, good balance of parallelism and code size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Sum_Unroll4(ReadOnlySpan<int> data)
    {
        long sum = 0;
        int i = 0;

        // Process 4 items per iteration (4x parallelism potential)
        for (; i < data.Length - 3; i += 4)
        {
            sum += data[i];
            sum += data[i + 1];
            sum += data[i + 2];
            sum += data[i + 3];
        }

        // Remainder
        while (i < data.Length)
            sum += data[i++];

        return sum;
    }

    /// <summary>
    /// Unroll-4 with multiple accumulators.
    /// Reduces dependency chain, enables better instruction scheduling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Sum_Unroll4_MultiAccumulator(ReadOnlySpan<int> data)
    {
        // Two independent accumulators to reduce dependency chain
        long sum1 = 0, sum2 = 0;
        int i = 0;

        for (; i < data.Length - 3; i += 4)
        {
            sum1 += data[i];
            sum2 += data[i + 1];
            sum1 += data[i + 2];
            sum2 += data[i + 3];
        }

        // Remainder
        while (i < data.Length)
            sum1 += data[i++];

        return sum1 + sum2;
    }

    /// <summary>
    /// Unroll-8 pattern: Process 8 items per iteration.
    /// Good for vectorizable operations or when code size allows.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Sum_Unroll8(ReadOnlySpan<int> data)
    {
        long sum = 0;
        int i = 0;

        for (; i < data.Length - 7; i += 8)
        {
            sum += data[i];
            sum += data[i + 1];
            sum += data[i + 2];
            sum += data[i + 3];
            sum += data[i + 4];
            sum += data[i + 5];
            sum += data[i + 6];
            sum += data[i + 7];
        }

        // Remainder
        while (i < data.Length)
            sum += data[i++];

        return sum;
    }

    /// <summary>
    /// Unroll-8 with multiple accumulators (4 accumulators).
    /// Maximum parallelism with minimal dependency chains.
    /// Best for latency-tolerant operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Sum_Unroll8_QuadAccumulator(ReadOnlySpan<int> data)
    {
        // Four independent accumulators for maximum parallelism
        long sum1 = 0, sum2 = 0, sum3 = 0, sum4 = 0;
        int i = 0;

        for (; i < data.Length - 7; i += 8)
        {
            sum1 += data[i];
            sum2 += data[i + 1];
            sum3 += data[i + 2];
            sum4 += data[i + 3];
            sum1 += data[i + 4];
            sum2 += data[i + 5];
            sum3 += data[i + 6];
            sum4 += data[i + 7];
        }

        // Remainder
        while (i < data.Length)
            sum1 += data[i++];

        return sum1 + sum2 + sum3 + sum4;
    }

    /// <summary>
    /// Generic unroll-2 for any operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach_Unroll2<T>(ReadOnlySpan<T> items, Action<T> action)
    {
        int i = 0;

        for (; i < items.Length - 1; i += 2)
        {
            action(items[i]);
            action(items[i + 1]);
        }

        if (i < items.Length)
            action(items[i]);
    }

    /// <summary>
    /// Generic unroll-4 for any operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach_Unroll4<T>(ReadOnlySpan<T> items, Action<T> action)
    {
        int i = 0;

        for (; i < items.Length - 3; i += 4)
        {
            action(items[i]);
            action(items[i + 1]);
            action(items[i + 2]);
            action(items[i + 3]);
        }

        while (i < items.Length)
            action(items[i++]);
    }

    /// <summary>
    /// Comparison unroll-4: Count matches.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count_Unroll4(ReadOnlySpan<int> data, int threshold)
    {
        int count = 0;
        int i = 0;

        for (; i < data.Length - 3; i += 4)
        {
            if (data[i] > threshold) count++;
            if (data[i + 1] > threshold) count++;
            if (data[i + 2] > threshold) count++;
            if (data[i + 3] > threshold) count++;
        }

        while (i < data.Length)
        {
            if (data[i++] > threshold) count++;
        }

        return count;
    }

    /// <summary>
    /// Memory operation unroll-8: Fast buffer clear.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clear_Unroll8(Span<byte> buffer)
    {
        int i = 0;

        for (; i < buffer.Length - 7; i += 8)
        {
            buffer[i] = 0;
            buffer[i + 1] = 0;
            buffer[i + 2] = 0;
            buffer[i + 3] = 0;
            buffer[i + 4] = 0;
            buffer[i + 5] = 0;
            buffer[i + 6] = 0;
            buffer[i + 7] = 0;
        }

        while (i < buffer.Length)
            buffer[i++] = 0;
    }

    /// <summary>
    /// Multiply-accumulate unroll-4 with multiple accumulators.
    /// Good for fused operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MultiplyAccumulate_Unroll4(
        ReadOnlySpan<int> a,
        ReadOnlySpan<int> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Arrays must have same length");

        long sum1 = 0, sum2 = 0;
        int i = 0;

        for (; i < a.Length - 3; i += 4)
        {
            sum1 += (long)a[i] * b[i];
            sum2 += (long)a[i + 1] * b[i + 1];
            sum1 += (long)a[i + 2] * b[i + 2];
            sum2 += (long)a[i + 3] * b[i + 3];
        }

        while (i < a.Length)
            sum1 += (long)a[i] * b[i++];

        return sum1 + sum2;
    }

    /// <summary>
    /// Prefixes loop with JIT optimization hint.
    /// Tells JIT compiler this is a hot loop that should be optimized.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static TResult LoopWithOptimization<TResult>(Func<TResult> loopFunction)
    {
        return loopFunction();
    }

    /// <summary>
    /// Hint for JIT: This value is loop-invariant and should be hoisted.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T AsInvariant<T>(T value) => value;

    /// <summary>
    /// Hint for JIT: Likely to be within range for optimizations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LikelySmall(int value)
    {
        if (value < 0 || value > 10000)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}

/// <summary>
/// Phase 2E: Reduction operations with unrolling.
/// Optimized for parallel reduction with multiple accumulators.
/// </summary>
public static class ReductionOptimizer
{
    /// <summary>
    /// Parallel reduction with 4 accumulators.
    /// Reduces dependency chain latency.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Sum_ParallelReduction(ReadOnlySpan<long> data)
    {
        long sum1 = 0, sum2 = 0, sum3 = 0, sum4 = 0;
        int i = 0;

        for (; i < data.Length - 3; i += 4)
        {
            sum1 += data[i];
            sum2 += data[i + 1];
            sum3 += data[i + 2];
            sum4 += data[i + 3];
        }

        // Remaining elements
        while (i < data.Length)
            sum1 += data[i++];

        return sum1 + sum2 + sum3 + sum4;
    }

    /// <summary>
    /// Min/max reduction with unrolling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int min, int max) MinMax_Unroll4(ReadOnlySpan<int> data)
    {
        if (data.IsEmpty)
            throw new ArgumentException("Data cannot be empty");

        int min = data[0], max = data[0];
        int i = 1;

        for (; i < data.Length - 3; i += 4)
        {
            var v1 = data[i];
            var v2 = data[i + 1];
            var v3 = data[i + 2];
            var v4 = data[i + 3];

            if (v1 < min) min = v1; else if (v1 > max) max = v1;
            if (v2 < min) min = v2; else if (v2 > max) max = v2;
            if (v3 < min) min = v3; else if (v3 > max) max = v3;
            if (v4 < min) min = v4; else if (v4 > max) max = v4;
        }

        // Remainder
        while (i < data.Length)
        {
            var v = data[i++];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        return (min, max);
    }
}
