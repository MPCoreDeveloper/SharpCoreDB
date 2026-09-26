using SharpCoreDB.Analytics.Aggregation;

namespace SharpCoreDB.Analytics.Tests;

/// <summary>
/// Differential tests for <see cref="SimdAggregates"/>: every assertion compares the tiered vector
/// kernels against an independent scalar reference implemented in this file, over batch lengths that
/// straddle each tier boundary (one full 128/256/512-bit vector, and the 1024-element AVX-512 gate),
/// plus the length ranges that exercise the scalar tail.
/// </summary>
public class SimdAggregatesTests
{
    /// <summary>Lengths chosen around every tier boundary and its tail.</summary>
    public static TheoryData<int> Lengths =>
    [
        1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33,
        63, 64, 65, 127, 128, 129, 255, 256, 257, 1023, 1024, 1025, 4096
    ];

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Sum_Int32Batch_MatchesScalarReference(int length)
    {
        // Arrange
        var values = RandomInts(length, seed: 12345 + length);

        // Act
        var actual = SimdAggregates.Sum(values);

        // Assert
        Assert.Equal(ScalarSumInt32(values), actual);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Sum_Int64Batch_MatchesScalarReference(int length)
    {
        var values = RandomLongs(length, seed: 54321 + length);

        var actual = SimdAggregates.Sum(values);

        Assert.Equal(ScalarSumInt64(values), actual);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Sum_DoubleBatch_MatchesScalarReference(int length)
    {
        var values = RandomDoubles(length, seed: 999 + length);

        var actual = SimdAggregates.Sum(values);

        AssertClose(ScalarSumDouble(values), actual);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void SumOfSquares_DoubleBatch_MatchesScalarReference(int length)
    {
        var values = RandomDoubles(length, seed: 777 + length);

        var actual = SimdAggregates.SumOfSquares(values);

        AssertClose(ScalarSumOfSquares(values), actual);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Min_Int32Batch_MatchesScalarReference(int length)
    {
        var values = RandomInts(length, seed: 2468 + length);

        Assert.Equal(ScalarMinMaxInt32(values, isMax: false), SimdAggregates.Min(values));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Max_Int32Batch_MatchesScalarReference(int length)
    {
        var values = RandomInts(length, seed: 1357 + length);

        Assert.Equal(ScalarMinMaxInt32(values, isMax: true), SimdAggregates.Max(values));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Min_Int64Batch_MatchesScalarReference(int length)
    {
        var values = RandomLongs(length, seed: 8642 + length);

        Assert.Equal(ScalarMinMaxInt64(values, isMax: false), SimdAggregates.Min(values));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Max_Int64Batch_MatchesScalarReference(int length)
    {
        var values = RandomLongs(length, seed: 9753 + length);

        Assert.Equal(ScalarMinMaxInt64(values, isMax: true), SimdAggregates.Max(values));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Min_DoubleBatch_MatchesScalarReference(int length)
    {
        var values = RandomDoubles(length, seed: 3141 + length);

        AssertClose(ScalarMinMaxDouble(values, isMax: false), SimdAggregates.Min(values));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Max_DoubleBatch_MatchesScalarReference(int length)
    {
        var values = RandomDoubles(length, seed: 2718 + length);

        AssertClose(ScalarMinMaxDouble(values, isMax: true), SimdAggregates.Max(values));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Average_Batches_MatchesScalarReference(int length)
    {
        var ints = RandomInts(length, seed: 616 + length);
        var longs = RandomLongs(length, seed: 717 + length);
        var doubles = RandomDoubles(length, seed: 818 + length);

        AssertClose(ScalarSumInt32(ints) / (double)length, SimdAggregates.Average(ints));
        AssertClose(ScalarSumInt64(longs) / (double)length, SimdAggregates.Average(longs));
        AssertClose(ScalarSumDouble(doubles) / length, SimdAggregates.Average(doubles));
    }

    // ---------------------------------------------------------------------------------------------
    // Edge cases: empty batches, 64-bit accumulation, seed extremes, NaN and infinity propagation.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sum_EmptyBatch_ReturnsZero()
    {
        Assert.Equal(0L, SimdAggregates.Sum(Array.Empty<int>()));
        Assert.Equal(0L, SimdAggregates.Sum(Array.Empty<long>()));
        Assert.Equal(0.0, SimdAggregates.Sum(Array.Empty<double>()));
        Assert.Equal(0.0, SimdAggregates.SumOfSquares(Array.Empty<double>()));
    }

    [Fact]
    public void Average_EmptyBatch_ReturnsNaN()
    {
        Assert.True(double.IsNaN(SimdAggregates.Average(Array.Empty<int>())));
        Assert.True(double.IsNaN(SimdAggregates.Average(Array.Empty<long>())));
        Assert.True(double.IsNaN(SimdAggregates.Average(Array.Empty<double>())));
    }

    [Fact]
    public void MinMax_EmptyBatch_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SimdAggregates.Min(Array.Empty<int>()));
        Assert.Throws<ArgumentException>(() => SimdAggregates.Max(Array.Empty<int>()));
        Assert.Throws<ArgumentException>(() => SimdAggregates.Min(Array.Empty<long>()));
        Assert.Throws<ArgumentException>(() => SimdAggregates.Max(Array.Empty<long>()));
        Assert.Throws<ArgumentException>(() => SimdAggregates.Min(Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() => SimdAggregates.Max(Array.Empty<double>()));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(1024)]
    [InlineData(1025)]
    public void Sum_Int32BatchExceedingInt32Range_ReturnsWidenedTotal(int length)
    {
        // length * int.MaxValue overflows a 32-bit accumulator; the kernel must widen to 64 bits.
        var values = new int[length];
        Array.Fill(values, int.MaxValue);

        var expected = (long)length * int.MaxValue;

        Assert.Equal(expected, SimdAggregates.Sum(values));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(1024)]
    public void MinMax_AllEqualExtremeValues_ReturnsThatExtreme(int length)
    {
        // The vector accumulator is seeded with the type extreme; a batch made only of that extreme
        // must still return it, and a batch of the opposite extreme must win over the seed.
        var allMin = new int[length];
        Array.Fill(allMin, int.MinValue);
        var allMax = new int[length];
        Array.Fill(allMax, int.MaxValue);

        Assert.Equal(int.MinValue, SimdAggregates.Min(allMin));
        Assert.Equal(int.MinValue, SimdAggregates.Max(allMin));
        Assert.Equal(int.MaxValue, SimdAggregates.Max(allMax));
        Assert.Equal(int.MaxValue, SimdAggregates.Min(allMax));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(1024)]
    public void MinMax_DoubleBatchContainingNaN_ReturnsNaN(int length)
    {
        // Math.Min/Math.Max propagate NaN, and the vector fold must not swallow it — a plain
        // Vector*.Min would return the non-NaN operand here.
        var values = RandomDoubles(length, seed: 5);
        values[length - 1] = double.NaN;

        Assert.True(double.IsNaN(SimdAggregates.Min(values)));
        Assert.True(double.IsNaN(SimdAggregates.Max(values)));
    }

    [Fact]
    public void MinMax_DoubleBatchWithNaNAfterScanWindow_StillReturnsNaN()
    {
        // Leaves NaN in the scalar tail rather than in the vector body.
        var values = new double[20];
        Array.Fill(values, 1.0);
        values[19] = double.NaN;

        Assert.True(double.IsNaN(SimdAggregates.Min(values)));
        Assert.True(double.IsNaN(SimdAggregates.Max(values)));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(1024)]
    public void MinMax_DoubleBatchWithInfinity_ReturnsInfinity(int length)
    {
        var values = RandomDoubles(length, seed: 11);
        values[0] = double.NegativeInfinity;
        values[length - 1] = double.PositiveInfinity;

        Assert.Equal(double.NegativeInfinity, SimdAggregates.Min(values));
        Assert.Equal(double.PositiveInfinity, SimdAggregates.Max(values));
    }

    [Fact]
    public void TierFor_BatchLengths_ReportsExpectedTierCapability()
    {
        // Diagnostics must never claim a tier the running machine cannot execute.
        Assert.True(SimdAggregates.TierFor(1024) <= SimdAggregates.MaxTier);
        Assert.True(SimdAggregates.TierFor(64) <= SimdAggregates.MaxTier);
        Assert.True(SimdAggregates.TierFor(4) <= SimdAggregates.MaxTier);
        Assert.Equal(SimdAggregates.SimdTier.Scalar, SimdAggregates.TierFor(0));
        Assert.Equal(SimdAggregates.SimdTier.Scalar, SimdAggregates.TierFor(1));
        Assert.Equal(SimdAggregates.MaxTier != SimdAggregates.SimdTier.Scalar, SimdAggregates.IsHardwareAccelerated);
    }

    // ---------------------------------------------------------------------------------------------
    // Scalar reference implementations (independent of the production kernels).
    // ---------------------------------------------------------------------------------------------

    private static void AssertClose(double expected, double actual)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual), $"expected NaN but got {actual}");
            return;
        }

        var tolerance = 1e-9 * Math.Max(1.0, Math.Abs(expected));
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"expected {expected} but got {actual} (tolerance {tolerance})");
    }

    private static int[] RandomInts(int length, int seed)
    {
        var random = new Random(seed);
        var values = new int[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = random.Next(-1_000_000, 1_000_000);
        }
        return values;
    }

    private static long[] RandomLongs(int length, int seed)
    {
        var random = new Random(seed);
        var values = new long[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = random.NextInt64(-1_000_000_000_000L, 1_000_000_000_000L);
        }
        return values;
    }

    private static double[] RandomDoubles(int length, int seed)
    {
        var random = new Random(seed);
        var values = new double[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = (random.NextDouble() - 0.5) * 2_000_000.0;
        }
        return values;
    }

    private static long ScalarSumInt32(int[] values)
    {
        var sum = 0L;
        foreach (var value in values)
        {
            sum += value;
        }
        return sum;
    }

    private static long ScalarSumInt64(long[] values)
    {
        var sum = 0L;
        foreach (var value in values)
        {
            sum += value;
        }
        return sum;
    }

    private static double ScalarSumDouble(double[] values)
    {
        var sum = 0.0;
        foreach (var value in values)
        {
            sum += value;
        }
        return sum;
    }

    private static double ScalarSumOfSquares(double[] values)
    {
        var sum = 0.0;
        foreach (var value in values)
        {
            sum += value * value;
        }
        return sum;
    }

    private static int ScalarMinMaxInt32(int[] values, bool isMax)
    {
        var best = values[0];
        foreach (var value in values)
        {
            best = isMax ? Math.Max(best, value) : Math.Min(best, value);
        }
        return best;
    }

    private static long ScalarMinMaxInt64(long[] values, bool isMax)
    {
        var best = values[0];
        foreach (var value in values)
        {
            best = isMax ? Math.Max(best, value) : Math.Min(best, value);
        }
        return best;
    }

    private static double ScalarMinMaxDouble(double[] values, bool isMax)
    {
        var best = values[0];
        foreach (var value in values)
        {
            best = isMax ? Math.Max(best, value) : Math.Min(best, value);
        }
        return best;
    }
}
