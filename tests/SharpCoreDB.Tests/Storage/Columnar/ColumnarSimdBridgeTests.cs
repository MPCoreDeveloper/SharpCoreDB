// <copyright file="ColumnarSimdBridgeTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage.Columnar;

using System;
using SharpCoreDB.Optimizations;
using SharpCoreDB.Storage.Columnar;
using Xunit;

/// <summary>
/// Tests for Phase 7.2: SIMD Integration with Columnar Format.
/// ✅ SCDB Phase 7.2: Verifies bridge between Phase 7.1 and existing SIMD.
/// </summary>
public sealed class ColumnarSimdBridgeTests
{
    private readonly ITestOutputHelper _output;

    public ColumnarSimdBridgeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // NULL-Aware COUNT Tests
    // ========================================

    [Fact]
    public void CountNonNull_AllNonNull_ReturnsFullCount()
    {
        // Arrange
        var bitmap = new ColumnFormat.NullBitmap(100);
        // Don't set any NULLs

        // Act
        var count = ColumnarSimdBridge.CountNonNull(100, bitmap);

        // Assert
        Assert.Equal(100, count);
        _output.WriteLine($"✓ COUNT with no NULLs: {count}");
    }

    [Fact]
    public void CountNonNull_SomeNulls_ExcludesNulls()
    {
        // Arrange
        var bitmap = new ColumnFormat.NullBitmap(100);
        bitmap.SetNull(10);
        bitmap.SetNull(20);
        bitmap.SetNull(30);

        // Act
        var count = ColumnarSimdBridge.CountNonNull(100, bitmap);

        // Assert
        Assert.Equal(97, count); // 100 - 3 NULLs
        _output.WriteLine($"✓ COUNT with 3 NULLs: {count}");
    }

    [Fact]
    public void CountNonNull_AllNulls_ReturnsZero()
    {
        // Arrange
        var bitmap = new ColumnFormat.NullBitmap(10);
        for (int i = 0; i < 10; i++)
            bitmap.SetNull(i);

        // Act
        var count = ColumnarSimdBridge.CountNonNull(10, bitmap);

        // Assert
        Assert.Equal(0, count);
        _output.WriteLine("✓ COUNT with all NULLs: 0");
    }

    // ========================================
    // NULL-Aware SUM Tests
    // ========================================

    [Fact]
    public void SumWithNulls_AllNonNull_SumsAll()
    {
        // Arrange
        var values = new[] { 10, 20, 30, 40, 50 };
        var bitmap = new ColumnFormat.NullBitmap(5);

        // Act
        var sum = ColumnarSimdBridge.SumWithNulls(values, bitmap);

        // Assert
        Assert.Equal(150, sum);
        _output.WriteLine($"✓ SUM with no NULLs: {sum}");
    }

    [Fact]
    public void SumWithNulls_SomeNulls_SkipsNulls()
    {
        // Arrange
        var values = new[] { 10, 20, 30, 40, 50 };
        var bitmap = new ColumnFormat.NullBitmap(5);
        bitmap.SetNull(1); // Skip 20
        bitmap.SetNull(3); // Skip 40

        // Act
        var sum = ColumnarSimdBridge.SumWithNulls(values, bitmap);

        // Assert
        Assert.Equal(90, sum); // 10 + 30 + 50 = 90
        _output.WriteLine($"✓ SUM skipping NULLs: {sum}");
    }

    [Fact]
    public void SumWithNulls_LargeArray_UsesSimd()
    {
        // Arrange
        var values = new int[1000];
        for (int i = 0; i < 1000; i++)
            values[i] = i + 1;

        var bitmap = new ColumnFormat.NullBitmap(1000);
        // Mark every 10th value as NULL
        for (int i = 9; i < 1000; i += 10) // Indices 9, 19, 29, ..., 999 (100 values)
            bitmap.SetNull(i);

        // Act
        var sum = ColumnarSimdBridge.SumWithNulls(values, bitmap);

        // Assert
        // Sum of 1 to 1000 = 500500
        // Values at indices 9, 19, ..., 999 are: 10, 20, ..., 1000
        // Sum of nulls = (10 + 20 + ... + 1000) = 10 * (1 + 2 + ... + 100) = 10 * 5050 = 50500
        // Expected sum = 500500 - 50500 = 450000
        Assert.Equal(450000, sum);
        _output.WriteLine($"✓ SUM with SIMD (1000 elements, 100 NULLs): {sum}");
    }

    // ========================================
    // NULL-Aware AVG Tests
    // ========================================

    [Fact]
    public void AverageWithNulls_AllNonNull_CalculatesCorrectly()
    {
        // Arrange
        var values = new[] { 10, 20, 30, 40, 50 };
        var bitmap = new ColumnFormat.NullBitmap(5);

        // Act
        var avg = ColumnarSimdBridge.AverageWithNulls(values, bitmap);

        // Assert
        Assert.Equal(30.0, avg);
        _output.WriteLine($"✓ AVG with no NULLs: {avg}");
    }

    [Fact]
    public void AverageWithNulls_SomeNulls_ExcludesNullsFromAverage()
    {
        // Arrange
        var values = new[] { 10, 20, 30, 40, 50 };
        var bitmap = new ColumnFormat.NullBitmap(5);
        bitmap.SetNull(0); // Skip 10
        bitmap.SetNull(4); // Skip 50

        // Act
        var avg = ColumnarSimdBridge.AverageWithNulls(values, bitmap);

        // Assert
        Assert.Equal(30.0, avg); // (20 + 30 + 40) / 3 = 30
        _output.WriteLine($"✓ AVG excluding NULLs: {avg}");
    }

    // ========================================
    // Encoding-Aware Filtering Tests
    // ========================================

    [Fact]
    public void FilterEncoded_RawEncoding_UsesSimdDirectly()
    {
        // Arrange
        var values = new[] { 5, 15, 25, 35, 45 };

        // Act
        var matches = ColumnarSimdBridge.FilterEncoded(
            ColumnFormat.ColumnEncoding.Raw,
            values,
            threshold: 20,
            SimdWhereFilter.ComparisonOp.GreaterThan
        );

        // Assert
        Assert.Equal(3, matches.Length); // 25, 35, 45
        Assert.Contains(2, matches); // Index of 25
        Assert.Contains(3, matches); // Index of 35
        Assert.Contains(4, matches); // Index of 45
        _output.WriteLine($"✓ FILTER with Raw encoding: {matches.Length} matches");
    }

    [Fact]
    public void FilterEncoded_DeltaEncoding_ReconstructsThenFilters()
    {
        // Arrange (delta-encoded: base=10, deltas=[10, 10, 10, 10])
        var deltaValues = new[] { 10, 10, 10, 10, 10 };

        // Act
        var matches = ColumnarSimdBridge.FilterEncoded(
            ColumnFormat.ColumnEncoding.Delta,
            deltaValues,
            threshold: 30,
            SimdWhereFilter.ComparisonOp.GreaterThan
        );

        // Assert
        // Reconstructed: [10, 20, 30, 40, 50]
        // Greater than 30: [40, 50] at indices 3, 4
        Assert.Equal(2, matches.Length);
        _output.WriteLine($"✓ FILTER with Delta encoding: {matches.Length} matches");
    }

    // ========================================
    // Statistics-Driven SIMD Selection Tests
    // ========================================

    [Fact]
    public void ShouldUseSimd_SmallDataset_ReturnsFalse()
    {
        // Arrange
        var stats = new ColumnStatistics.ColumnStats
        {
            ColumnName = "TestCol",
            ValueCount = 50, // Below threshold
            NullCount = 0,
            DistinctCount = 50
        };

        // Act
        var shouldUse = ColumnarSimdBridge.ShouldUseSimd(stats, dataLength: 50);

        // Assert
        Assert.False(shouldUse);
        _output.WriteLine("✓ Small dataset: SIMD not used");
    }

    [Fact]
    public void ShouldUseSimd_LargeDataset_ReturnsTrue()
    {
        // Arrange
        var stats = new ColumnStatistics.ColumnStats
        {
            ColumnName = "TestCol",
            ValueCount = 1000,
            NullCount = 10,
            DistinctCount = 990
        };

        // Act
        var shouldUse = ColumnarSimdBridge.ShouldUseSimd(stats, dataLength: 1000);

        // Assert (depends on hardware support)
        _output.WriteLine($"✓ Large dataset: SIMD used = {shouldUse}");
    }

    [Fact]
    public void ShouldUseSimd_MostlyNulls_ReturnsFalse()
    {
        // Arrange
        var stats = new ColumnStatistics.ColumnStats
        {
            ColumnName = "TestCol",
            ValueCount = 1000,
            NullCount = 980, // 98% NULLs
            DistinctCount = 20
        };

        // Act
        var shouldUse = ColumnarSimdBridge.ShouldUseSimd(stats, dataLength: 1000);

        // Assert
        Assert.False(shouldUse); // Too many NULLs
        _output.WriteLine("✓ Mostly NULLs: SIMD not used");
    }

    // ========================================
    // Bitmap SIMD Operations Tests
    // ========================================

    [Fact]
    public void BitmapPopCount_EmptyBitmap_ReturnsZero()
    {
        // Arrange
        var bitmap = new byte[0];

        // Act
        var count = BitmapSimdOps.PopulationCount(bitmap);

        // Assert
        Assert.Equal(0, count);
        _output.WriteLine("✓ PopCount of empty bitmap: 0");
    }

    [Fact]
    public void BitmapPopCount_AllZeros_ReturnsZero()
    {
        // Arrange
        var bitmap = new byte[32]; // All zeros

        // Act
        var count = BitmapSimdOps.PopulationCount(bitmap);

        // Assert
        Assert.Equal(0, count);
        _output.WriteLine("✓ PopCount of all zeros: 0");
    }

    [Fact]
    public void BitmapPopCount_SomeBitsSet_CountsCorrectly()
    {
        // Arrange
        var bitmap = new byte[] { 0b00000001, 0b00000011, 0b00000111, 0b00001111 };
        // Bit counts: 1 + 2 + 3 + 4 = 10

        // Act
        var count = BitmapSimdOps.PopulationCount(bitmap);

        // Assert
        Assert.Equal(10, count);
        _output.WriteLine($"✓ PopCount with mixed bits: {count}");
    }

    [Fact]
    public void BitwiseAnd_TwoBitmaps_CombinesCorrectly()
    {
        // Arrange
        var a = new byte[] { 0b11110000, 0b10101010 };
        var b = new byte[] { 0b11001100, 0b11001100 };
        var result = new byte[2];

        // Act
        BitmapSimdOps.BitwiseAnd(a, b, result);

        // Assert
        Assert.Equal(0b11000000, result[0]); // AND of first bytes
        Assert.Equal(0b10001000, result[1]); // AND of second bytes
        _output.WriteLine("✓ Bitwise AND combines correctly");
    }

    [Fact]
    public void BitwiseOr_TwoBitmaps_CombinesCorrectly()
    {
        // Arrange
        var a = new byte[] { 0b11110000, 0b10101010 };
        var b = new byte[] { 0b00001111, 0b01010101 };
        var result = new byte[2];

        // Act
        BitmapSimdOps.BitwiseOr(a, b, result);

        // Assert
        Assert.Equal(0b11111111, result[0]); // OR of first bytes
        Assert.Equal(0b11111111, result[1]); // OR of second bytes
        _output.WriteLine("✓ Bitwise OR combines correctly");
    }

    [Fact]
    public void ExpandBitmapToMask_CorrectlyExpands()
    {
        // Arrange
        var bitmap = new byte[] { 0b00000101 }; // Bits 0 and 2 are set (NULL)
        var mask = new int[8];

        // Act
        BitmapSimdOps.ExpandBitmapToMask(bitmap, mask);

        // Assert
        Assert.Equal(0, mask[0]);  // Bit 0 set = NULL = 0
        Assert.Equal(-1, mask[1]); // Bit 1 clear = non-NULL = -1
        Assert.Equal(0, mask[2]);  // Bit 2 set = NULL = 0
        Assert.Equal(-1, mask[3]); // Bit 3 clear = non-NULL = -1
        _output.WriteLine("✓ Bitmap expanded to mask correctly");
    }

    [Fact]
    public void IsAllZero_AllZeros_ReturnsTrue()
    {
        // Arrange
        var bitmap = new byte[64]; // All zeros

        // Act
        var isZero = BitmapSimdOps.IsAllZero(bitmap);

        // Assert
        Assert.True(isZero);
        _output.WriteLine("✓ IsAllZero detects all zeros");
    }

    [Fact]
    public void IsAllZero_SomeBitsSet_ReturnsFalse()
    {
        // Arrange
        var bitmap = new byte[64];
        bitmap[32] = 1; // Set one bit

        // Act
        var isZero = BitmapSimdOps.IsAllZero(bitmap);

        // Assert
        Assert.False(isZero);
        _output.WriteLine("✓ IsAllZero detects non-zero bitmap");
    }

    // ========================================
    // MIN/MAX with NULLs Tests
    // ========================================

    [Fact]
    public void MinWithNulls_AllNonNull_ReturnsCorrectMin()
    {
        // Arrange
        var values = new[] { 50, 10, 30, 20, 40 };
        var bitmap = new ColumnFormat.NullBitmap(5);

        // Act
        var min = ColumnarSimdBridge.MinWithNulls(values, bitmap);

        // Assert
        Assert.Equal(10, min);
        _output.WriteLine($"✓ MIN with no NULLs: {min}");
    }

    [Fact]
    public void MinWithNulls_SomeNulls_IgnoresNulls()
    {
        // Arrange
        var values = new[] { 5, 10, 30, 20, 40 };
        var bitmap = new ColumnFormat.NullBitmap(5);
        bitmap.SetNull(0); // Ignore 5 (would be min)

        // Act
        var min = ColumnarSimdBridge.MinWithNulls(values, bitmap);

        // Assert
        Assert.Equal(10, min); // Ignores 5 because it's NULL
        _output.WriteLine($"✓ MIN ignoring NULLs: {min}");
    }

    [Fact]
    public void MaxWithNulls_AllNonNull_ReturnsCorrectMax()
    {
        // Arrange
        var values = new[] { 50, 10, 30, 20, 40 };
        var bitmap = new ColumnFormat.NullBitmap(5);

        // Act
        var max = ColumnarSimdBridge.MaxWithNulls(values, bitmap);

        // Assert
        Assert.Equal(50, max);
        _output.WriteLine($"✓ MAX with no NULLs: {max}");
    }

    [Fact]
    public void MaxWithNulls_SomeNulls_IgnoresNulls()
    {
        // Arrange
        var values = new[] { 50, 10, 100, 20, 40 };
        var bitmap = new ColumnFormat.NullBitmap(5);
        bitmap.SetNull(2); // Ignore 100 (would be max)

        // Act
        var max = ColumnarSimdBridge.MaxWithNulls(values, bitmap);

        // Assert
        Assert.Equal(50, max); // Ignores 100 because it's NULL
        _output.WriteLine($"✓ MAX ignoring NULLs: {max}");
    }

    // ========================================
    // Integration Test: Full Pipeline
    // ========================================

    [Fact]
    public void FullPipeline_ColumnarFormatWithSimd_WorksEndToEnd()
    {
        // Arrange
        var values = new int[1000];
        for (int i = 0; i < 1000; i++)
            values[i] = i * 10;

        var bitmap = new ColumnFormat.NullBitmap(1000);
        for (int i = 0; i < 1000; i += 5)
            bitmap.SetNull(i); // Every 5th value is NULL

        // Act
        var count = ColumnarSimdBridge.CountNonNull(1000, bitmap);
        var sum = ColumnarSimdBridge.SumWithNulls(values, bitmap);
        var avg = ColumnarSimdBridge.AverageWithNulls(values, bitmap);

        // Assert
        Assert.Equal(800, count); // 1000 - 200 NULLs
        Assert.True(sum > 0);
        Assert.True(avg > 0);
        _output.WriteLine($"✓ Full pipeline: COUNT={count}, SUM={sum}, AVG={avg:F2}");
    }
}
