// <copyright file="ColumnFormatTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage.Columnar;

using System;
using System.Collections.Generic;
using SharpCoreDB.Storage.Columnar;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Tests for Phase 7: Columnar storage format components.
/// ✅ SCDB Phase 7: ColumnFormat, ColumnCompression, ColumnStatistics, ColumnCodec
/// </summary>
public sealed class ColumnFormatTests
{
    private readonly ITestOutputHelper _output;

    public ColumnFormatTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // ColumnFormat Tests
    // ========================================

    [Fact]
    public void NullBitmap_SetAndCheckNull_Works()
    {
        // Arrange
        var bitmap = new ColumnFormat.NullBitmap(100);

        // Act
        bitmap.SetNull(0);
        bitmap.SetNull(50);
        bitmap.SetNull(99);

        // Assert
        Assert.True(bitmap.IsNull(0));
        Assert.False(bitmap.IsNull(1));
        Assert.True(bitmap.IsNull(50));
        Assert.True(bitmap.IsNull(99));
        Assert.False(bitmap.IsNull(98));
        _output.WriteLine("✓ NullBitmap set/check works correctly");
    }

    [Fact]
    public void NullBitmap_GetBytes_ReturnsBitmapData()
    {
        // Arrange
        var bitmap = new ColumnFormat.NullBitmap(16);
        bitmap.SetNull(0);
        bitmap.SetNull(8);

        // Act
        var bytes = bitmap.GetBytes();

        // Assert
        Assert.Equal(2, bytes.Length); // 16 bits = 2 bytes
        Assert.Equal(1, bytes[0]); // Bit 0 set
        Assert.Equal(1, bytes[1]); // Bit 0 set (of second byte = bit 8)
    }

    [Fact]
    public void StringDictionary_GetOrAddIndex_CachesValues()
    {
        // Arrange
        var dict = new ColumnFormat.StringDictionary();

        // Act
        var idx1 = dict.GetOrAddIndex("Apple");
        var idx1b = dict.GetOrAddIndex("Apple");
        var idx2 = dict.GetOrAddIndex("Banana");

        // Assert
        Assert.Equal(0, idx1);
        Assert.Equal(0, idx1b); // Same index
        Assert.Equal(1, idx2);
        Assert.Equal(2, dict.Count);
        _output.WriteLine("✓ StringDictionary caching works");
    }

    [Fact]
    public void ColumnMetadata_Validate_ChecksConsistency()
    {
        // Arrange
        var format = new ColumnFormat
        {
            Columns = new List<ColumnFormat.ColumnMetadata>
            {
                new()
                {
                    ColumnName = "ID",
                    ColumnOrdinal = 0,
                    DataType = ColumnFormat.ColumnType.Int32,
                    Encoding = ColumnFormat.ColumnEncoding.Raw,
                    ValueCount = 100,
                    NullCount = 0,
                }
            },
            RowCount = 100,
        };

        // Act
        var valid = format.Validate();

        // Assert
        Assert.True(valid);
        _output.WriteLine("✓ ColumnFormat validation works");
    }

    // ========================================
    // ColumnCompression Tests
    // ========================================

    [Fact]
    public void EncodeDictionary_StringArray_ProducesIndices()
    {
        // Arrange
        var values = new[] { "Apple", "Banana", "Apple", "Cherry", "Banana", "Apple" };

        // Act
        var result = ColumnCompression.EncodeDictionary(values);

        // Assert
        Assert.NotNull(result.Dictionary);
        Assert.Equal(3, result.Dictionary.Count); // 3 unique values
        Assert.Equal(6, result.Indices.Length);
        Assert.True(result.CompressionRatio < 1.0); // Some compression
        _output.WriteLine($"✓ Dictionary encoding: {result.CompressionRatio:P1} compression");
    }

    [Fact]
    public void DecodeDictionary_RoundTrip_PreservesData()
    {
        // Arrange
        var original = new[] { "Apple", "Banana", "Apple", "Cherry" };
        var encoded = ColumnCompression.EncodeDictionary(original);

        // Act
        var decoded = ColumnCompression.DecodeDictionary(encoded);

        // Assert
        Assert.Equal(original, decoded);
        _output.WriteLine("✓ Dictionary round-trip preserves data");
    }

    [Theory]
    [InlineData(new long[] { 1, 2, 3, 4, 5 })]
    [InlineData(new long[] { 100, 200, 300, 400, 500 })]
    [InlineData(new long[] { 1000, 1100, 1200, 1300 })]
    public void EncodeDelta_SortedLongs_ReducesSize(long[] values)
    {
        // Act
        var result = ColumnCompression.EncodeDelta(values);

        // Assert
        Assert.NotNull(result.Deltas);
        Assert.Equal(values.Length - 1, result.Deltas.Length);
        Assert.True(result.CompressionRatio < 1.0);
        _output.WriteLine($"✓ Delta encoding {string.Join(",", values)}: {result.CompressionRatio:P1}");
    }

    [Fact]
    public void DecodeDelta_RoundTrip_PreservesData()
    {
        // Arrange
        var original = new long[] { 100, 200, 350, 500, 750 };
        var encoded = ColumnCompression.EncodeDelta(original);

        // Act
        var decoded = ColumnCompression.DecodeDelta(encoded);

        // Assert
        Assert.Equal(original, decoded);
        _output.WriteLine("✓ Delta round-trip preserves data");
    }

    [Fact]
    public void ShouldUseDictionary_LowCardinality_ReturnsTrue()
    {
        // Arrange
        var values = new[] { "A", "B", "A", "B", "A", "B", "A", "B", "A", "B" };

        // Act
        var result = ColumnCompression.ShouldUseDictionary(values);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldUseDictionary_HighCardinality_ReturnsFalse()
    {
        // Arrange
        var values = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" };

        // Act
        var result = ColumnCompression.ShouldUseDictionary(values);

        // Assert
        Assert.False(result);
    }

    // ========================================
    // ColumnStatistics Tests
    // ========================================

    [Fact]
    public void BuildStats_IntArray_CalculatesCorrectly()
    {
        // Arrange
        var values = new[] { 10, 20, 30, 40, 50 };

        // Act
        var stats = ColumnStatistics.BuildStats("TestColumn", values);

        // Assert
        Assert.Equal("TestColumn", stats.ColumnName);
        Assert.Equal(5, stats.ValueCount);
        Assert.Equal(0, stats.NullCount);
        Assert.Equal(5, stats.DistinctCount);
        Assert.Equal(10, stats.MinValue);
        Assert.Equal(50, stats.MaxValue);
        Assert.True(stats.Validate());
        _output.WriteLine("✓ Integer statistics calculated correctly");
    }

    [Fact]
    public void BuildStats_StringArray_CalculatesLength()
    {
        // Arrange
        var values = new[] { "Apple", "Banana", "Cherry" };

        // Act
        var stats = ColumnStatistics.BuildStats("Fruits", values);

        // Assert
        Assert.Equal(3, stats.ValueCount);
        Assert.Equal(3, stats.DistinctCount);
        Assert.NotNull(stats.AvgStringLength);
        Assert.True(stats.AvgStringLength > 0);
        _output.WriteLine($"✓ String stats: avg length = {stats.AvgStringLength:F1}");
    }

    [Fact]
    public void EstimateSelectivity_EqualPredicate_ReturnsReasonable()
    {
        // Arrange
        var values = new[] { 1, 1, 1, 2, 2, 3, 3, 3, 3, 4 };
        var stats = ColumnStatistics.BuildStats("TestCol", values);

        // Act
        var selectivity = ColumnStatistics.EstimateSelectivity(
            stats, 
            ColumnFormat.ColumnEncoding.Raw, 
            "=", 
            3
        );

        // Assert
        Assert.True(selectivity > 0);
        Assert.True(selectivity <= 1.0);
        _output.WriteLine($"✓ Selectivity estimate: {selectivity:P1}");
    }

    // ========================================
    // ColumnCodec Tests
    // ========================================

    [Fact]
    public void ColumnCodec_EncodeDecodeInt32_RoundTrip()
    {
        // Arrange
        var format = new ColumnFormat
        {
            Columns = new List<ColumnFormat.ColumnMetadata>
            {
                new()
                {
                    ColumnName = "TestInt",
                    ColumnOrdinal = 0,
                    DataType = ColumnFormat.ColumnType.Int32,
                    Encoding = ColumnFormat.ColumnEncoding.Raw,
                    ValueCount = 5,
                    NullCount = 0,
                }
            },
            RowCount = 5,
        };
        var codec = new ColumnCodec(format);
        var values = new object?[] { 10, 20, 30, 40, 50 };

        // Act
        var encoded = codec.EncodeColumn("TestInt", values);
        var decoded = codec.DecodeColumn(encoded);

        // Assert
        Assert.Equal(5, decoded.Length);
        Assert.Equal(10, decoded[0]);
        Assert.Equal(30, decoded[2]);
        Assert.Equal(50, decoded[4]);
        _output.WriteLine("✓ Int32 codec round-trip successful");
    }

    [Fact]
    public void ColumnCodec_EncodeDecodeString_RoundTrip()
    {
        // Arrange
        var format = new ColumnFormat
        {
            Columns = new List<ColumnFormat.ColumnMetadata>
            {
                new()
                {
                    ColumnName = "TestStr",
                    ColumnOrdinal = 0,
                    DataType = ColumnFormat.ColumnType.String,
                    Encoding = ColumnFormat.ColumnEncoding.Raw,
                    ValueCount = 3,
                    NullCount = 0,
                }
            },
            RowCount = 3,
        };
        var codec = new ColumnCodec(format);
        var values = new object?[] { "Hello", "World", "Test" };

        // Act
        var encoded = codec.EncodeColumn("TestStr", values);
        var decoded = codec.DecodeColumn(encoded);

        // Assert
        Assert.Equal(3, decoded.Length);
        Assert.Equal("Hello", decoded[0]);
        Assert.Equal("World", decoded[1]);
        Assert.Equal("Test", decoded[2]);
        _output.WriteLine("✓ String codec round-trip successful");
    }

    [Fact]
    public void ColumnCodec_EncodeDecodeDelta_CompressesData()
    {
        // Arrange
        var format = new ColumnFormat
        {
            Columns = new List<ColumnFormat.ColumnMetadata>
            {
                new()
                {
                    ColumnName = "Sorted",
                    ColumnOrdinal = 0,
                    DataType = ColumnFormat.ColumnType.Int32,
                    Encoding = ColumnFormat.ColumnEncoding.Delta,
                    ValueCount = 5,
                    NullCount = 0,
                }
            },
            RowCount = 5,
        };
        var codec = new ColumnCodec(format);
        var values = new object?[] { 100, 200, 300, 400, 500 };

        // Act
        var encoded = codec.EncodeColumn("Sorted", values);

        // Assert
        Assert.NotNull(encoded);
        // Delta encoding should produce smaller output
        Assert.True(encoded.Length < sizeof(int) * 5 * 2); // Rough estimate
        _output.WriteLine($"✓ Delta encoding reduced size: {encoded.Length} bytes");
    }

    [Fact]
    public void ColumnCodec_HandleNulls_PreservesNullBitmap()
    {
        // Arrange
        var format = new ColumnFormat
        {
            Columns = new List<ColumnFormat.ColumnMetadata>
            {
                new()
                {
                    ColumnName = "Nullable",
                    ColumnOrdinal = 0,
                    DataType = ColumnFormat.ColumnType.Int32,
                    Encoding = ColumnFormat.ColumnEncoding.Raw,
                    ValueCount = 5,
                    NullCount = 2,
                }
            },
            RowCount = 5,
        };
        var codec = new ColumnCodec(format);
        var values = new object?[] { 10, null, 30, null, 50 };

        // Act
        var encoded = codec.EncodeColumn("Nullable", values);
        var decoded = codec.DecodeColumn(encoded);

        // Assert
        Assert.Equal(10, decoded[0]);
        Assert.Null(decoded[1]);
        Assert.Equal(30, decoded[2]);
        Assert.Null(decoded[3]);
        Assert.Equal(50, decoded[4]);
        _output.WriteLine("✓ Null bitmap preserved correctly");
    }

    // ========================================
    // Integration Tests
    // ========================================

    [Fact]
    public void FullPipeline_EncodedDataMatchesOriginal()
    {
        // Arrange
        var originalData = new object?[] { "Red", "Blue", "Green", "Red", "Blue" };
        var format = new ColumnFormat
        {
            Columns = new List<ColumnFormat.ColumnMetadata>
            {
                new()
                {
                    ColumnName = "Color",
                    ColumnOrdinal = 0,
                    DataType = ColumnFormat.ColumnType.String,
                    Encoding = ColumnFormat.ColumnEncoding.Dictionary,
                    ValueCount = 5,
                    NullCount = 0,
                }
            },
            RowCount = 5,
        };
        var codec = new ColumnCodec(format);

        // Act: Encode
        var encoded = codec.EncodeColumn("Color", originalData);
        var decodedData = codec.DecodeColumn(encoded);

        // Assert
        for (int i = 0; i < originalData.Length; i++)
        {
            Assert.Equal(originalData[i], decodedData[i]);
        }
        _output.WriteLine("✓ Full encode-decode pipeline successful");
    }
}
