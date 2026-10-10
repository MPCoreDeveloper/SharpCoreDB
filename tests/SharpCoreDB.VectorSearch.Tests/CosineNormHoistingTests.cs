// <copyright file="CosineNormHoistingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Guards the hoisted-query-norm cosine path used by the DiskANN
// beam search: reusing the query's squared norm must not change a single bit of the result.
// </copyright>

using SharpCoreDB.VectorSearch;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

public class CosineNormHoistingTests
{
    /// <summary>
    /// Every SIMD tier and the scalar tail: the dims list deliberately straddles the AVX-512
    /// threshold (64), the AVX2 threshold (8) and the SSE threshold (4), plus non-multiples.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(100)]
    [InlineData(128)]
    [InlineData(257)]
    public void CosineDistance_WithHoistedQueryNorm_IsBitIdentical(int dims)
    {
        var rng = new Random(dims);
        for (int trial = 0; trial < 25; trial++)
        {
            var a = new float[dims];
            var b = new float[dims];
            for (int i = 0; i < dims; i++)
            {
                a[i] = (float)((rng.NextDouble() * 2) - 1);
                b[i] = (float)((rng.NextDouble() * 2) - 1);
            }

            float expected = DistanceMetrics.CosineDistance(a, b);
            float actual = DistanceMetrics.CosineDistanceWithQuerySquaredNorm(
                a, b, DistanceMetrics.SquaredNorm(a));

            Assert.Equal(
                BitConverter.SingleToInt32Bits(expected),
                BitConverter.SingleToInt32Bits(actual));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(129)]
    public void SquaredNorm_MatchesScalarReference(int dims)
    {
        var rng = new Random(dims + 1000);
        var v = new float[dims];
        double expected = 0;
        for (int i = 0; i < dims; i++)
        {
            v[i] = (float)((rng.NextDouble() * 2) - 1);
            expected += (double)v[i] * v[i];
        }

        float actual = DistanceMetrics.SquaredNorm(v);

        Assert.Equal(expected, actual, 4); // relative-ish tolerance: different summation order
    }

    [Fact]
    public void CosineDistance_WithHoistedNorm_HandlesZeroVector()
    {
        float[] a = [0f, 0f, 0f, 0f];
        float[] b = [1f, 2f, 3f, 4f];

        Assert.Equal(
            DistanceMetrics.CosineDistance(a, b),
            DistanceMetrics.CosineDistanceWithQuerySquaredNorm(a, b, DistanceMetrics.SquaredNorm(a)));
    }

    [Fact]
    public void CosineDistanceWithQuerySquaredNorm_MismatchedDimensions_Throws()
    {
        _ = Assert.Throws<ArgumentException>(
            () => DistanceMetrics.CosineDistanceWithQuerySquaredNorm(new float[4], new float[5], 1f));
    }
}
