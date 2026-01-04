// <copyright file="SimdHelper.Arithmetic.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;

/// <summary>
/// SimdHelper - SIMD-accelerated arithmetic operations.
/// Contains high-performance vectorized arithmetic for numeric arrays.
/// Part of the SimdHelper partial class.
/// THRESHOLDS: Apply SIMD for arrays >= 128 elements to justify overhead.
/// </summary>
public static partial class SimdHelper
{
    /// <summary>
    /// Minimum array size to justify SIMD overhead for arithmetic operations.
    /// For smaller arrays, scalar operations are faster due to setup cost.
    /// </summary>
    private const int ARITHMETIC_SIMD_THRESHOLD = 128;

    /// <summary>
    /// Adds two int32 arrays element-wise using SIMD acceleration.
    /// result[i] = left[i] + right[i]
    /// THRESHOLD: Use for arrays >= 128 elements.
    /// </summary>
    /// <param name="left">First array.</param>
    /// <param name="right">Second array.</param>
    /// <param name="result">Result array (must be same length).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void AddInt32(ReadOnlySpan<int> left, ReadOnlySpan<int> right, Span<int> result)
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("All arrays must have the same length");

        if (left.IsEmpty)
            return;

        // For small arrays, use scalar
        if (left.Length < ARITHMETIC_SIMD_THRESHOLD)
        {
            AddInt32Scalar(left, right, result);
            return;
        }

        // AVX2: 8 int32s at a time (256 bits / 32 bits = 8)
        if (Avx2.IsSupported && left.Length >= 8)
        {
            AddInt32Avx2(left, right, result);
            return;
        }

        // SSE2: 4 int32s at a time (128 bits / 32 bits = 4)
        if (Sse2.IsSupported && left.Length >= 4)
        {
            AddInt32Sse2(left, right, result);
            return;
        }

        // ARM NEON: 4 int32s at a time
        if (AdvSimd.IsSupported && left.Length >= 4)
        {
            AddInt32Neon(left, right, result);
            return;
        }

        // Scalar fallback
        AddInt32Scalar(left, right, result);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void AddInt32Avx2(ReadOnlySpan<int> left, ReadOnlySpan<int> right, Span<int> result)
    {
        fixed (int* leftPtr = left)
        fixed (int* rightPtr = right)
        fixed (int* resultPtr = result)
        {
            int i = 0;
            int vectorizedLength = left.Length & ~7;

            for (; i < vectorizedLength; i += 8)
            {
                Vector256<int> leftVec = Avx.LoadVector256(leftPtr + i);
                Vector256<int> rightVec = Avx.LoadVector256(rightPtr + i);
                Vector256<int> sum = Avx2.Add(leftVec, rightVec);
                Avx.Store(resultPtr + i, sum);
            }

            // Process remaining elements
            for (; i < left.Length; i++)
            {
                resultPtr[i] = leftPtr[i] + rightPtr[i];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void AddInt32Sse2(ReadOnlySpan<int> left, ReadOnlySpan<int> right, Span<int> result)
    {
        fixed (int* leftPtr = left)
        fixed (int* rightPtr = right)
        fixed (int* resultPtr = result)
        {
            int i = 0;
            int vectorizedLength = left.Length & ~3;

            for (; i < vectorizedLength; i += 4)
            {
                Vector128<int> leftVec = Sse2.LoadVector128(leftPtr + i);
                Vector128<int> rightVec = Sse2.LoadVector128(rightPtr + i);
                Vector128<int> sum = Sse2.Add(leftVec, rightVec);
                Sse2.Store(resultPtr + i, sum);
            }

            for (; i < left.Length; i++)
            {
                resultPtr[i] = leftPtr[i] + rightPtr[i];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void AddInt32Neon(ReadOnlySpan<int> left, ReadOnlySpan<int> right, Span<int> result)
    {
        fixed (int* leftPtr = left)
        fixed (int* rightPtr = right)
        fixed (int* resultPtr = result)
        {
            int i = 0;
            int vectorizedLength = left.Length & ~3;

            for (; i < vectorizedLength; i += 4)
            {
                Vector128<int> leftVec = AdvSimd.LoadVector128(leftPtr + i);
                Vector128<int> rightVec = AdvSimd.LoadVector128(rightPtr + i);
                Vector128<int> sum = AdvSimd.Add(leftVec, rightVec);
                AdvSimd.Store(resultPtr + i, sum);
            }

            for (; i < left.Length; i++)
            {
                resultPtr[i] = leftPtr[i] + rightPtr[i];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void AddInt32Scalar(ReadOnlySpan<int> left, ReadOnlySpan<int> right, Span<int> result)
    {
        for (int i = 0; i < left.Length; i++)
        {
            result[i] = left[i] + right[i];
        }
    }

    /// <summary>
    /// Multiplies two double arrays element-wise using SIMD acceleration.
    /// result[i] = left[i] * right[i]
    /// THRESHOLD: Use for arrays >= 128 elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void MultiplyDouble(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> result)
    {
        if (left.Length != right.Length || left.Length != result.Length)
            throw new ArgumentException("All arrays must have the same length");

        if (left.IsEmpty)
            return;

        if (left.Length < ARITHMETIC_SIMD_THRESHOLD)
        {
            MultiplyDoubleScalar(left, right, result);
            return;
        }

        // AVX2: 4 doubles at a time (256 bits / 64 bits = 4)
        if (Avx2.IsSupported && left.Length >= 4)
        {
            MultiplyDoubleAvx2(left, right, result);
            return;
        }

        // SSE2: 2 doubles at a time
        if (Sse2.IsSupported && left.Length >= 2)
        {
            MultiplyDoubleSse2(left, right, result);
            return;
        }

        // ARM NEON: 2 doubles at a time
        if (AdvSimd.IsSupported && left.Length >= 2)
        {
            MultiplyDoubleNeon(left, right, result);
            return;
        }

        MultiplyDoubleScalar(left, right, result);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void MultiplyDoubleAvx2(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> result)
    {
        fixed (double* leftPtr = left)
        fixed (double* rightPtr = right)
        fixed (double* resultPtr = result)
        {
            int i = 0;
            int vectorizedLength = left.Length & ~3;

            for (; i < vectorizedLength; i += 4)
            {
                Vector256<double> leftVec = Avx.LoadVector256(leftPtr + i);
                Vector256<double> rightVec = Avx.LoadVector256(rightPtr + i);
                Vector256<double> product = Avx.Multiply(leftVec, rightVec);
                Avx.Store(resultPtr + i, product);
            }

            for (; i < left.Length; i++)
            {
                resultPtr[i] = leftPtr[i] * rightPtr[i];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void MultiplyDoubleSse2(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> result)
    {
        fixed (double* leftPtr = left)
        fixed (double* rightPtr = right)
        fixed (double* resultPtr = result)
        {
            int i = 0;
            int vectorizedLength = left.Length & ~1;

            for (; i < vectorizedLength; i += 2)
            {
                Vector128<double> leftVec = Sse2.LoadVector128(leftPtr + i);
                Vector128<double> rightVec = Sse2.LoadVector128(rightPtr + i);
                Vector128<double> product = Sse2.Multiply(leftVec, rightVec);
                Sse2.Store(resultPtr + i, product);
            }

            for (; i < left.Length; i++)
            {
                resultPtr[i] = leftPtr[i] * rightPtr[i];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void MultiplyDoubleNeon(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> result)
    {
        fixed (double* leftPtr = left)
        fixed (double* rightPtr = right)
        fixed (double* resultPtr = result)
        {
            int i = 0;
            int vectorizedLength = left.Length & ~1;

            // ARM NEON doesn't have direct double multiply, use scalar fallback
            for (; i < vectorizedLength; i += 2)
            {
                resultPtr[i] = leftPtr[i] * rightPtr[i];
                resultPtr[i + 1] = leftPtr[i + 1] * rightPtr[i + 1];
            }

            for (; i < left.Length; i++)
            {
                resultPtr[i] = leftPtr[i] * rightPtr[i];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void MultiplyDoubleScalar(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> result)
    {
        for (int i = 0; i < left.Length; i++)
        {
            result[i] = left[i] * right[i];
        }
    }

    /// <summary>
    /// Finds the minimum value in an int32 array using SIMD acceleration.
    /// THRESHOLD: Use for arrays >= 128 elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int MinInt32(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty)
            throw new ArgumentException("Array cannot be empty");

        if (values.Length < ARITHMETIC_SIMD_THRESHOLD)
            return MinInt32Scalar(values);

        if (Avx2.IsSupported && values.Length >= 8)
            return MinInt32Avx2(values);

        if (Sse2.IsSupported && values.Length >= 4)
            return MinInt32Sse2(values);

        if (AdvSimd.IsSupported && values.Length >= 4)
            return MinInt32Neon(values);

        return MinInt32Scalar(values);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int MinInt32Avx2(ReadOnlySpan<int> values)
    {
        fixed (int* ptr = values)
        {
            int i = 0;
            int vectorizedLength = values.Length & ~7;

            // Initialize with first 8 values
            Vector256<int> minVec = Avx.LoadVector256(ptr);

            for (i = 8; i < vectorizedLength; i += 8)
            {
                Vector256<int> current = Avx.LoadVector256(ptr + i);
                minVec = Avx2.Min(minVec, current);
            }

            // Reduce vector to single value
            int min = int.MaxValue;
            for (int j = 0; j < 8; j++)
            {
                int val = minVec.GetElement(j);
                if (val < min) min = val;
            }

            // Process remaining elements
            for (; i < values.Length; i++)
            {
                if (ptr[i] < min) min = ptr[i];
            }

            return min;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int MinInt32Sse2(ReadOnlySpan<int> values)
    {
        fixed (int* ptr = values)
        {
            int i = 0;
            int vectorizedLength = values.Length & ~3;

            Vector128<int> minVec = Sse2.LoadVector128(ptr);

            for (i = 4; i < vectorizedLength; i += 4)
            {
                Vector128<int> current = Sse2.LoadVector128(ptr + i);
                
                // SSE4.1 has MinEpi32, but SSE2 doesn't - need to emulate
                Vector128<int> mask = Sse2.CompareLessThan(current, minVec);
                minVec = Sse2.Or(Sse2.And(mask, current), Sse2.AndNot(mask, minVec));
            }

            int min = int.MaxValue;
            for (int j = 0; j < 4; j++)
            {
                int val = minVec.GetElement(j);
                if (val < min) min = val;
            }

            for (; i < values.Length; i++)
            {
                if (ptr[i] < min) min = ptr[i];
            }

            return min;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int MinInt32Neon(ReadOnlySpan<int> values)
    {
        fixed (int* ptr = values)
        {
            int i = 0;
            int vectorizedLength = values.Length & ~3;

            Vector128<int> minVec = AdvSimd.LoadVector128(ptr);

            for (i = 4; i < vectorizedLength; i += 4)
            {
                Vector128<int> current = AdvSimd.LoadVector128(ptr + i);
                minVec = AdvSimd.Min(minVec, current);
            }

            int min = int.MaxValue;
            for (int j = 0; j < 4; j++)
            {
                int val = minVec.GetElement(j);
                if (val < min) min = val;
            }

            for (; i < values.Length; i++)
            {
                if (ptr[i] < min) min = ptr[i];
            }

            return min;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int MinInt32Scalar(ReadOnlySpan<int> values)
    {
        int min = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min)
                min = values[i];
        }
        return min;
    }

    /// <summary>
    /// Counts non-zero elements in a byte array using SIMD acceleration.
    /// Useful for counting valid/active flags in bitmaps.
    /// THRESHOLD: Use for arrays >= 256 elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int CountNonZero(ReadOnlySpan<byte> values)
    {
        if (values.IsEmpty)
            return 0;

        if (values.Length < 256)
            return CountNonZeroScalar(values);

        if (Avx2.IsSupported && values.Length >= 32)
            return CountNonZeroAvx2(values);

        if (Sse2.IsSupported && values.Length >= 16)
            return CountNonZeroSse2(values);

        if (AdvSimd.IsSupported && values.Length >= 16)
            return CountNonZeroNeon(values);

        return CountNonZeroScalar(values);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int CountNonZeroAvx2(ReadOnlySpan<byte> values)
    {
        fixed (byte* ptr = values)
        {
            int count = 0;
            int i = 0;
            int vectorizedLength = values.Length & ~31;

            Vector256<byte> zero = Vector256<byte>.Zero;

            for (; i < vectorizedLength; i += 32)
            {
                Vector256<byte> data = Avx.LoadVector256(ptr + i);
                Vector256<byte> cmp = Avx2.CompareEqual(data, zero);
                int mask = Avx2.MoveMask(cmp);
                
                // Count zeros and subtract from total
                count += 32 - BitOperations.PopCount((uint)mask);
            }

            for (; i < values.Length; i++)
            {
                if (ptr[i] != 0)
                    count++;
            }

            return count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int CountNonZeroSse2(ReadOnlySpan<byte> values)
    {
        fixed (byte* ptr = values)
        {
            int count = 0;
            int i = 0;
            int vectorizedLength = values.Length & ~15;

            Vector128<byte> zero = Vector128<byte>.Zero;

            for (; i < vectorizedLength; i += 16)
            {
                Vector128<byte> data = Sse2.LoadVector128(ptr + i);
                Vector128<byte> cmp = Sse2.CompareEqual(data, zero);
                int mask = Sse2.MoveMask(cmp);
                
                count += 16 - BitOperations.PopCount((uint)mask);
            }

            for (; i < values.Length; i++)
            {
                if (ptr[i] != 0)
                    count++;
            }

            return count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int CountNonZeroNeon(ReadOnlySpan<byte> values)
    {
        fixed (byte* ptr = values)
        {
            int count = 0;
            int i = 0;
            int vectorizedLength = values.Length & ~15;

            Vector128<byte> zero = Vector128<byte>.Zero;

            for (; i < vectorizedLength; i += 16)
            {
                Vector128<byte> data = AdvSimd.LoadVector128(ptr + i);
                Vector128<byte> cmp = AdvSimd.CompareEqual(data, zero);
                
                // Count non-zero (inverse of comparison result)
                for (int j = 0; j < 16; j++)
                {
                    if (cmp.GetElement(j) == 0)
                        count++;
                }
            }

            for (; i < values.Length; i++)
            {
                if (ptr[i] != 0)
                    count++;
            }

            return count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int CountNonZeroScalar(ReadOnlySpan<byte> values)
    {
        int count = 0;
        foreach (byte b in values)
        {
            if (b != 0)
                count++;
        }
        return count;
    }
}
