// <copyright file="SimdFilterTierTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB.Query;
using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// <see cref="SimdFilter"/> was rewritten from the portable <c>System.Numerics.Vector&lt;T&gt;</c> path to
/// explicit multi-tier intrinsics (AVX-512 → AVX2 → SSE2 → scalar, per <c>.github/SIMD_STANDARDS.md</c>).
/// The mask is now extracted with <c>ExtractMostSignificantBits</c> and walked with a bit-scan loop, which is
/// exactly where a rewrite can silently drop or duplicate an index. Every public filter is therefore pinned
/// against a scalar reference at — and one element either side of — every tier width (4/8/16/32/64) and the
/// 1024-element AVX-512 floor, so the tier actually taken by the machine under test is covered.
/// </summary>
public sealed class SimdFilterTierTests
{
    /// <summary>Tier widths, the AVX-512 floor, and the remainders immediately around both.</summary>
    private static readonly int[] Lengths =
    [
        0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129,
        255, 256, 257, 511, 512, 513, 1023, 1024, 1025, 2047, 2048, 2049, 4096, 5000,
    ];

    private static int[] BuildInts(int length, int seed)
    {
        var values = new int[length];
        var random = new Random(seed);
        for (int i = 0; i < length; i++)
        {
            values[i] = random.Next(-50, 51);
        }

        return values;
    }

    private static double[] BuildDoubles(int length, int seed)
    {
        var values = new double[length];
        var random = new Random(seed);
        for (int i = 0; i < length; i++)
        {
            values[i] = Math.Round((random.NextDouble() * 100.0) - 50.0, 3);
        }

        return values;
    }

    private static int[] ReferenceEquals(int[] values, int target)
    {
        var matches = new List<int>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == target)
            {
                matches.Add(i);
            }
        }

        return [.. matches];
    }

    private static int[] ReferenceGreaterThan(int[] values, int threshold)
    {
        var matches = new List<int>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > threshold)
            {
                matches.Add(i);
            }
        }

        return [.. matches];
    }

    private static int[] ReferenceRange(int[] values, int min, int max)
    {
        var matches = new List<int>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] >= min && values[i] < max)
            {
                matches.Add(i);
            }
        }

        return [.. matches];
    }

    private static int[] ReferenceGreaterThan(double[] values, double threshold)
    {
        var matches = new List<int>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > threshold)
            {
                matches.Add(i);
            }
        }

        return [.. matches];
    }

    private static int[] ReferenceRange(double[] values, double min, double max)
    {
        var matches = new List<int>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] >= min && values[i] < max)
            {
                matches.Add(i);
            }
        }

        return [.. matches];
    }

    [Fact]
    public void FilterEquals_LengthsAcrossTierBoundaries_MatchesScalarReference()
    {
        foreach (int length in Lengths)
        {
            int[] values = BuildInts(length, 17);

            Assert.Equal(ReferenceEquals(values, 7), SimdFilter.FilterEquals(values, 7));
            Assert.Equal(ReferenceEquals(values, 0), SimdFilter.FilterEquals(values, 0));
            Assert.Equal(ReferenceEquals(values, -50), SimdFilter.FilterEquals(values, -50));
        }
    }

    [Fact]
    public void FilterGreaterThan_LengthsAcrossTierBoundaries_MatchesScalarReference()
    {
        foreach (int length in Lengths)
        {
            int[] values = BuildInts(length, 23);

            Assert.Equal(ReferenceGreaterThan(values, 0), SimdFilter.FilterGreaterThan(values, 0));
            Assert.Equal(ReferenceGreaterThan(values, int.MinValue), SimdFilter.FilterGreaterThan(values, int.MinValue));
            Assert.Equal(ReferenceGreaterThan(values, int.MaxValue), SimdFilter.FilterGreaterThan(values, int.MaxValue));
        }
    }

    [Fact]
    public void FilterRange_LengthsAcrossTierBoundaries_MatchesScalarReference()
    {
        foreach (int length in Lengths)
        {
            int[] values = BuildInts(length, 31);

            Assert.Equal(ReferenceRange(values, -10, 10), SimdFilter.FilterRange(values, -10, 10));
            Assert.Equal(ReferenceRange(values, int.MinValue, int.MaxValue), SimdFilter.FilterRange(values, int.MinValue, int.MaxValue));
            Assert.Equal(ReferenceRange(values, 0, 0), SimdFilter.FilterRange(values, 0, 0));
        }
    }

    [Fact]
    public void FilterGreaterThan_DoublesAcrossTierBoundaries_MatchesScalarReference()
    {
        foreach (int length in Lengths)
        {
            double[] values = BuildDoubles(length, 41);

            Assert.Equal(ReferenceGreaterThan(values, 0.0), SimdFilter.FilterGreaterThan(values, 0.0));
            Assert.Equal(ReferenceGreaterThan(values, 25.5), SimdFilter.FilterGreaterThan(values, 25.5));
            Assert.Equal(ReferenceGreaterThan(values, double.NegativeInfinity), SimdFilter.FilterGreaterThan(values, double.NegativeInfinity));
        }
    }

    [Fact]
    public void FilterRange_DoublesAcrossTierBoundaries_MatchesScalarReference()
    {
        foreach (int length in Lengths)
        {
            double[] values = BuildDoubles(length, 47);

            Assert.Equal(ReferenceRange(values, -10.0, 10.0), SimdFilter.FilterRange(values, -10.0, 10.0));
            Assert.Equal(ReferenceRange(values, 0.0, 0.0), SimdFilter.FilterRange(values, 0.0, 0.0));
            Assert.Equal(ReferenceRange(values, double.NegativeInfinity, double.PositiveInfinity), SimdFilter.FilterRange(values, double.NegativeInfinity, double.PositiveInfinity));
        }
    }

    /// <summary>NaN fails both comparisons in the scalar reference; the mask tiers must agree, not "fix" it.</summary>
    [Fact]
    public void FilterRange_DoublesWithNaNs_MatchesScalarReference()
    {
        var values = new double[2048];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = i % 5 == 0 ? double.NaN : i - 1024;
        }

        Assert.Equal(ReferenceRange(values, -1.0, 1.0), SimdFilter.FilterRange(values, -1.0, 1.0));
        Assert.Equal(ReferenceGreaterThan(values, -1.0), SimdFilter.FilterGreaterThan(values, -1.0));
    }

    [Fact]
    public void FilterEquals_AllElementsMatch_ReturnsEveryIndex()
    {
        var values = new int[2048];
        Array.Fill(values, 42);

        int[] indices = SimdFilter.FilterEquals(values, 42);

        Assert.Equal(values.Length, indices.Length);
        for (int i = 0; i < indices.Length; i++)
        {
            Assert.Equal(i, indices[i]);
        }
    }

    [Fact]
    public void FilterEquals_NoElementMatches_ReturnsEmptyWithoutAllocatedSlack()
    {
        int[] values = BuildInts(2048, 59);

        int[] indices = SimdFilter.FilterEquals(values, 1111);

        Assert.Empty(indices);
    }

    /// <summary>The reported width must be the widest tier the hardware can actually execute.</summary>
    [Fact]
    public void VectorSize_Int32AndDouble_MatchesWidestSupportedTier()
    {
        int intSize = SimdFilter.VectorSize<int>();
        int doubleSize = SimdFilter.VectorSize<double>();

        if (!SimdFilter.IsSimdSupported)
        {
            Assert.Equal(1, intSize);
            Assert.Equal(1, doubleSize);
            return;
        }

        Assert.True(intSize > 1, $"expected a vectorized int32 width, got {intSize}");
        Assert.Equal(intSize / 2, doubleSize);
    }
}
