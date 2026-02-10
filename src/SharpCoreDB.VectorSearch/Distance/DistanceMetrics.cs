// <copyright file="DistanceMetrics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// SIMD-accelerated vector distance calculations using explicit System.Runtime.Intrinsics.
/// NativeAOT-optimized: zero reflection, zero dynamic dispatch, aggressive inlining.
/// Multi-tier dispatch: AVX-512 (16 floats) → AVX2 (8) → SSE (4) → scalar.
/// Uses FMA where available for fused multiply-add (better throughput + precision).
/// Zero external dependencies — pure BCL.
/// </summary>
public static class DistanceMetrics
{
    // AVX-512 threshold — amortize AVX-512 transition overhead for small vectors
    private const int Avx512MinElements = 64;

    /// <summary>
    /// Cosine distance: 1 - (a · b) / (‖a‖ × ‖b‖).
    /// Result range: [0, 2] where 0 = identical, 1 = orthogonal, 2 = opposite.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions must match: {a.Length} vs {b.Length}");

        // PERF: Fused single-pass computes dot + normA + normB together for cache efficiency
        DotAndNorms(a, b, out float dot, out float normA, out float normB);

        float denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        if (denominator < float.Epsilon)
            return 1f; // Zero-vector fallback: treat as orthogonal

        return 1f - (dot / denominator);
    }

    /// <summary>
    /// Euclidean (L2) distance: √Σ(aᵢ - bᵢ)².
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        return MathF.Sqrt(EuclideanDistanceSquared(a, b));
    }

    /// <summary>
    /// Squared Euclidean distance: Σ(aᵢ - bᵢ)². Avoids sqrt for comparison-only use (faster).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float EuclideanDistanceSquared(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions must match: {a.Length} vs {b.Length}");

        float sum = 0f;
        int i = 0;
        int len = a.Length;

        ref float refA = ref MemoryMarshal.GetReference(a);
        ref float refB = ref MemoryMarshal.GetReference(b);

        if (Avx512F.IsSupported && len >= Avx512MinElements)
        {
            var vSum = Vector512<float>.Zero;
            int vecLen = len & ~15;

            for (; i < vecLen; i += 16)
            {
                var va = Vector512.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector512.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                var diff = va - vb;
                vSum = Avx512F.FusedMultiplyAdd(diff, diff, vSum);
            }

            sum = Vector512.Sum(vSum);
        }
        else if (Avx2.IsSupported && len >= 8)
        {
            var vSum = Vector256<float>.Zero;
            int vecLen = len & ~7;

            for (; i < vecLen; i += 8)
            {
                var va = Vector256.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector256.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                var diff = va - vb;
                vSum = Fma.IsSupported
                    ? Fma.MultiplyAdd(diff, diff, vSum)
                    : vSum + (diff * diff);
            }

            sum = Vector256.Sum(vSum);
        }
        else if (Sse.IsSupported && len >= 4)
        {
            var vSum = Vector128<float>.Zero;
            int vecLen = len & ~3;

            for (; i < vecLen; i += 4)
            {
                var va = Vector128.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector128.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                var diff = va - vb;
                vSum = Fma.IsSupported
                    ? Fma.MultiplyAdd(diff, diff, vSum)
                    : vSum + (diff * diff);
            }

            sum = Vector128.Sum(vSum);
        }

        // Scalar tail
        for (; i < len; i++)
        {
            float diff = a[i] - b[i];
            sum += diff * diff;
        }

        return sum;
    }

    /// <summary>
    /// Dot product (inner product): Σ(aᵢ × bᵢ). Higher = more similar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions must match: {a.Length} vs {b.Length}");

        float dot = 0f;
        int i = 0;
        int len = a.Length;

        ref float refA = ref MemoryMarshal.GetReference(a);
        ref float refB = ref MemoryMarshal.GetReference(b);

        if (Avx512F.IsSupported && len >= Avx512MinElements)
        {
            var vDot = Vector512<float>.Zero;
            int vecLen = len & ~15;

            for (; i < vecLen; i += 16)
            {
                var va = Vector512.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector512.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                vDot = Avx512F.FusedMultiplyAdd(va, vb, vDot);
            }

            dot = Vector512.Sum(vDot);
        }
        else if (Avx2.IsSupported && len >= 8)
        {
            var vDot = Vector256<float>.Zero;
            int vecLen = len & ~7;

            for (; i < vecLen; i += 8)
            {
                var va = Vector256.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector256.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                vDot = Fma.IsSupported
                    ? Fma.MultiplyAdd(va, vb, vDot)
                    : vDot + (va * vb);
            }

            dot = Vector256.Sum(vDot);
        }
        else if (Sse.IsSupported && len >= 4)
        {
            var vDot = Vector128<float>.Zero;
            int vecLen = len & ~3;

            for (; i < vecLen; i += 4)
            {
                var va = Vector128.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector128.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                vDot = Fma.IsSupported
                    ? Fma.MultiplyAdd(va, vb, vDot)
                    : vDot + (va * vb);
            }

            dot = Vector128.Sum(vDot);
        }

        // Scalar tail
        for (; i < len; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }

    /// <summary>
    /// Negative dot product for ORDER BY compatibility (lower = more similar).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NegativeDotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        return -DotProduct(a, b);
    }

    /// <summary>
    /// L2-normalizes a vector: v / ‖v‖. Returns a new array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float[] Normalize(ReadOnlySpan<float> vector)
    {
        float norm = MathF.Sqrt(DotProduct(vector, vector));
        var result = new float[vector.Length];

        if (norm < float.Epsilon)
        {
            vector.CopyTo(result);
            return result;
        }

        int i = 0;
        int len = vector.Length;

        ref float refSrc = ref MemoryMarshal.GetReference(vector);
        ref float refDst = ref MemoryMarshal.GetReference(result.AsSpan());

        if (Avx512F.IsSupported && len >= Avx512MinElements)
        {
            var vNorm = Vector512.Create(norm);
            int vecLen = len & ~15;

            for (; i < vecLen; i += 16)
            {
                var v = Vector512.LoadUnsafe(ref Unsafe.Add(ref refSrc, i));
                (v / vNorm).StoreUnsafe(ref Unsafe.Add(ref refDst, i));
            }
        }
        else if (Avx2.IsSupported && len >= 8)
        {
            var vNorm = Vector256.Create(norm);
            int vecLen = len & ~7;

            for (; i < vecLen; i += 8)
            {
                var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref refSrc, i));
                (v / vNorm).StoreUnsafe(ref Unsafe.Add(ref refDst, i));
            }
        }
        else if (Sse.IsSupported && len >= 4)
        {
            var vNorm = Vector128.Create(norm);
            int vecLen = len & ~3;

            for (; i < vecLen; i += 4)
            {
                var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref refSrc, i));
                (v / vNorm).StoreUnsafe(ref Unsafe.Add(ref refDst, i));
            }
        }

        // Scalar tail
        for (; i < len; i++)
        {
            result[i] = vector[i] / norm;
        }

        return result;
    }

    /// <summary>
    /// Computes distance using the specified distance function.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Compute(ReadOnlySpan<float> a, ReadOnlySpan<float> b, DistanceFunction function)
    {
        return function switch
        {
            DistanceFunction.Cosine => CosineDistance(a, b),
            DistanceFunction.Euclidean => EuclideanDistance(a, b),
            DistanceFunction.DotProduct => NegativeDotProduct(a, b),
            _ => throw new ArgumentException($"Unknown distance function: {function}"),
        };
    }

    /// <summary>
    /// PERF: Computes dot product and both norms in a single pass (3 reductions instead of 3 separate passes).
    /// Fused to maximize cache utilization on large embedding vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void DotAndNorms(ReadOnlySpan<float> a, ReadOnlySpan<float> b,
        out float dot, out float normA, out float normB)
    {
        dot = 0f;
        normA = 0f;
        normB = 0f;
        int i = 0;
        int len = a.Length;

        ref float refA = ref MemoryMarshal.GetReference(a);
        ref float refB = ref MemoryMarshal.GetReference(b);

        if (Avx512F.IsSupported && len >= Avx512MinElements)
        {
            var vDot = Vector512<float>.Zero;
            var vNormA = Vector512<float>.Zero;
            var vNormB = Vector512<float>.Zero;
            int vecLen = len & ~15;

            for (; i < vecLen; i += 16)
            {
                var va = Vector512.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector512.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                vDot = Avx512F.FusedMultiplyAdd(va, vb, vDot);
                vNormA = Avx512F.FusedMultiplyAdd(va, va, vNormA);
                vNormB = Avx512F.FusedMultiplyAdd(vb, vb, vNormB);
            }

            dot = Vector512.Sum(vDot);
            normA = Vector512.Sum(vNormA);
            normB = Vector512.Sum(vNormB);
        }
        else if (Avx2.IsSupported && len >= 8)
        {
            var vDot = Vector256<float>.Zero;
            var vNormA = Vector256<float>.Zero;
            var vNormB = Vector256<float>.Zero;
            int vecLen = len & ~7;

            for (; i < vecLen; i += 8)
            {
                var va = Vector256.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector256.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                if (Fma.IsSupported)
                {
                    vDot = Fma.MultiplyAdd(va, vb, vDot);
                    vNormA = Fma.MultiplyAdd(va, va, vNormA);
                    vNormB = Fma.MultiplyAdd(vb, vb, vNormB);
                }
                else
                {
                    vDot += va * vb;
                    vNormA += va * va;
                    vNormB += vb * vb;
                }
            }

            dot = Vector256.Sum(vDot);
            normA = Vector256.Sum(vNormA);
            normB = Vector256.Sum(vNormB);
        }
        else if (Sse.IsSupported && len >= 4)
        {
            var vDot = Vector128<float>.Zero;
            var vNormA = Vector128<float>.Zero;
            var vNormB = Vector128<float>.Zero;
            int vecLen = len & ~3;

            for (; i < vecLen; i += 4)
            {
                var va = Vector128.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector128.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                if (Fma.IsSupported)
                {
                    vDot = Fma.MultiplyAdd(va, vb, vDot);
                    vNormA = Fma.MultiplyAdd(va, va, vNormA);
                    vNormB = Fma.MultiplyAdd(vb, vb, vNormB);
                }
                else
                {
                    vDot += va * vb;
                    vNormA += va * va;
                    vNormB += vb * vb;
                }
            }

            dot = Vector128.Sum(vDot);
            normA = Vector128.Sum(vNormA);
            normB = Vector128.Sum(vNormB);
        }

        // Scalar tail
        for (; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
    }
}
