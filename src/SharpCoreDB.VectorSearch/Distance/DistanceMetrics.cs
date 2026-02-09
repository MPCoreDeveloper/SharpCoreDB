// <copyright file="DistanceMetrics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// SIMD-accelerated vector distance calculations using System.Numerics.Vector&lt;float&gt;.
/// Automatically uses the widest SIMD available: AVX-512 (16 floats) → AVX2 (8) → SSE2/NEON (4) → scalar.
/// Zero external dependencies — pure BCL.
/// </summary>
public static class DistanceMetrics
{
    /// <summary>
    /// Cosine distance: 1 - (a · b) / (‖a‖ × ‖b‖).
    /// Result range: [0, 2] where 0 = identical, 1 = orthogonal, 2 = opposite.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions must match: {a.Length} vs {b.Length}");

        float dot = 0f, normA = 0f, normB = 0f;
        int i = 0;
        int simdWidth = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && a.Length >= simdWidth)
        {
            var vDot = Vector<float>.Zero;
            var vNormA = Vector<float>.Zero;
            var vNormB = Vector<float>.Zero;

            var spanA = MemoryMarshal.Cast<float, Vector<float>>(a);
            var spanB = MemoryMarshal.Cast<float, Vector<float>>(b);

            for (int j = 0; j < spanA.Length; j++)
            {
                vDot += spanA[j] * spanB[j];
                vNormA += spanA[j] * spanA[j];
                vNormB += spanB[j] * spanB[j];
            }

            dot = Vector.Sum(vDot);
            normA = Vector.Sum(vNormA);
            normB = Vector.Sum(vNormB);
            i = spanA.Length * simdWidth;
        }

        // Scalar tail for remaining elements
        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

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
        int simdWidth = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && a.Length >= simdWidth)
        {
            var vSum = Vector<float>.Zero;
            var spanA = MemoryMarshal.Cast<float, Vector<float>>(a);
            var spanB = MemoryMarshal.Cast<float, Vector<float>>(b);

            for (int j = 0; j < spanA.Length; j++)
            {
                var diff = spanA[j] - spanB[j];
                vSum += diff * diff;
            }

            sum = Vector.Sum(vSum);
            i = spanA.Length * simdWidth;
        }

        for (; i < a.Length; i++)
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
        int simdWidth = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && a.Length >= simdWidth)
        {
            var vDot = Vector<float>.Zero;
            var spanA = MemoryMarshal.Cast<float, Vector<float>>(a);
            var spanB = MemoryMarshal.Cast<float, Vector<float>>(b);

            for (int j = 0; j < spanA.Length; j++)
            {
                vDot += spanA[j] * spanB[j];
            }

            dot = Vector.Sum(vDot);
            i = spanA.Length * simdWidth;
        }

        for (; i < a.Length; i++)
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
    /// L2-normalizes a vector in-place: v / ‖v‖.
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
        int simdWidth = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && vector.Length >= simdWidth)
        {
            var vNorm = new Vector<float>(norm);
            var spanSrc = MemoryMarshal.Cast<float, Vector<float>>(vector);
            var spanDst = MemoryMarshal.Cast<float, Vector<float>>(result.AsSpan());

            for (int j = 0; j < spanSrc.Length; j++)
            {
                spanDst[j] = spanSrc[j] / vNorm;
            }

            i = spanSrc.Length * simdWidth;
        }

        for (; i < vector.Length; i++)
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
}
