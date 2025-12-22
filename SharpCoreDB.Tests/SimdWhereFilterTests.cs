// <copyright file="SimdWhereFilterTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using SharpCoreDB.Optimizations;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Unit tests for SIMD-accelerated WHERE clause filtering.
/// </summary>
public class SimdWhereFilterTests
{
    #region Integer Filtering Tests

    [Fact]
    public void FilterInt32_GreaterThan_ReturnsCorrectMatches()
    {
        // Arrange
        int[] values = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
        int threshold = 50;

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, threshold, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Equal(5, matches.Length); // 60, 70, 80, 90, 100
        Assert.Contains(5, matches); // index of 60
        Assert.Contains(6, matches); // index of 70
        Assert.Contains(9, matches); // index of 100
    }

    [Fact]
    public void FilterInt32_LessThan_ReturnsCorrectMatches()
    {
        // Arrange
        int[] values = [10, 20, 30, 40, 50];
        int threshold = 35;

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, threshold, SimdWhereFilter.ComparisonOp.LessThan);

        // Assert
        Assert.Equal(3, matches.Length); // 10, 20, 30
        Assert.Contains(0, matches);
        Assert.Contains(1, matches);
        Assert.Contains(2, matches);
    }

    [Fact]
    public void FilterInt32_Equal_ReturnsCorrectMatches()
    {
        // Arrange
        int[] values = [10, 20, 30, 20, 50, 20];
        int threshold = 20;

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, threshold, SimdWhereFilter.ComparisonOp.Equal);

        // Assert
        Assert.Equal(3, matches.Length); // indices 1, 3, 5
        Assert.Contains(1, matches);
        Assert.Contains(3, matches);
        Assert.Contains(5, matches);
    }

    [Fact]
    public void FilterInt32_GreaterOrEqual_ReturnsCorrectMatches()
    {
        // Arrange
        int[] values = [10, 20, 30, 40, 50];
        int threshold = 30;

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, threshold, SimdWhereFilter.ComparisonOp.GreaterOrEqual);

        // Assert
        Assert.Equal(3, matches.Length); // 30, 40, 50
        Assert.Contains(2, matches);
        Assert.Contains(3, matches);
        Assert.Contains(4, matches);
    }

    [Fact]
    public void FilterInt32_NotEqual_ReturnsCorrectMatches()
    {
        // Arrange
        int[] values = [10, 20, 30, 20, 50];
        int threshold = 20;

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, threshold, SimdWhereFilter.ComparisonOp.NotEqual);

        // Assert
        Assert.Equal(3, matches.Length); // 10, 30, 50
        Assert.Contains(0, matches);
        Assert.Contains(2, matches);
        Assert.Contains(4, matches);
    }

    [Fact]
    public void FilterInt32_LargeDataset_PerformanceTest()
    {
        // Arrange
        int[] values = new int[10_000];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = i;
        }
        int threshold = 5000;

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, threshold, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Equal(4999, matches.Length); // 5001-9999
        Assert.Contains(5001, matches); // First match
        Assert.Contains(9999, matches); // Last match
    }

    #endregion

    #region Long Filtering Tests

    [Fact]
    public void FilterInt64_GreaterThan_ReturnsCorrectMatches()
    {
        // Arrange
        long[] values = [100L, 200L, 300L, 400L, 500L];
        long threshold = 250L;

        // Act
        var matches = SimdWhereFilter.FilterInt64(values, threshold, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Equal(3, matches.Length); // 300, 400, 500
        Assert.Contains(2, matches);
        Assert.Contains(3, matches);
        Assert.Contains(4, matches);
    }

    [Fact]
    public void FilterInt64_Equal_ReturnsCorrectMatches()
    {
        // Arrange
        long[] values = [1000L, 2000L, 1000L, 3000L, 1000L];
        long threshold = 1000L;

        // Act
        var matches = SimdWhereFilter.FilterInt64(values, threshold, SimdWhereFilter.ComparisonOp.Equal);

        // Assert
        Assert.Equal(3, matches.Length); // indices 0, 2, 4
        Assert.Contains(0, matches);
        Assert.Contains(2, matches);
        Assert.Contains(4, matches);
    }

    #endregion

    #region Double Filtering Tests

    [Fact]
    public void FilterDouble_GreaterThan_ReturnsCorrectMatches()
    {
        // Arrange
        double[] values = [10.5, 20.5, 30.5, 40.5, 50.5];
        double threshold = 25.0;

        // Act
        var matches = SimdWhereFilter.FilterDouble(values, threshold, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Equal(3, matches.Length); // 30.5, 40.5, 50.5
        Assert.Contains(2, matches);
        Assert.Contains(3, matches);
        Assert.Contains(4, matches);
    }

    [Fact]
    public void FilterDouble_LessOrEqual_ReturnsCorrectMatches()
    {
        // Arrange
        double[] values = [10.0, 20.0, 30.0, 40.0];
        double threshold = 30.0;

        // Act
        var matches = SimdWhereFilter.FilterDouble(values, threshold, SimdWhereFilter.ComparisonOp.LessOrEqual);

        // Assert
        Assert.Equal(3, matches.Length); // 10.0, 20.0, 30.0
        Assert.Contains(0, matches);
        Assert.Contains(1, matches);
        Assert.Contains(2, matches);
    }

    #endregion

    #region Decimal Filtering Tests

    [Fact]
    public void FilterDecimal_GreaterThan_ReturnsCorrectMatches()
    {
        // Arrange
        decimal[] values = [100.50m, 200.50m, 300.50m, 400.50m];
        decimal threshold = 250.00m;

        // Act
        var matches = SimdWhereFilter.FilterDecimal(values, threshold, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Equal(2, matches.Length); // 300.50, 400.50
        Assert.Contains(2, matches);
        Assert.Contains(3, matches);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void FilterInt32_EmptyArray_ReturnsEmpty()
    {
        // Arrange
        int[] values = [];

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, 50, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Empty(matches);
    }

    [Fact]
    public void FilterInt32_NoMatches_ReturnsEmpty()
    {
        // Arrange
        int[] values = [10, 20, 30];

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, 100, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Empty(matches);
    }

    [Fact]
    public void FilterInt32_AllMatch_ReturnsAllIndices()
    {
        // Arrange
        int[] values = [10, 20, 30, 40, 50];

        // Act
        var matches = SimdWhereFilter.FilterInt32(values, 5, SimdWhereFilter.ComparisonOp.GreaterThan);

        // Assert
        Assert.Equal(5, matches.Length); // All values > 5
    }

    #endregion

    #region Operator Parsing Tests

    [Fact]
    public void ParseOperator_ValidOperators_ParsesCorrectly()
    {
        Assert.Equal(SimdWhereFilter.ComparisonOp.GreaterThan, SimdWhereFilter.ParseOperator(">"));
        Assert.Equal(SimdWhereFilter.ComparisonOp.LessThan, SimdWhereFilter.ParseOperator("<"));
        Assert.Equal(SimdWhereFilter.ComparisonOp.Equal, SimdWhereFilter.ParseOperator("="));
        Assert.Equal(SimdWhereFilter.ComparisonOp.GreaterOrEqual, SimdWhereFilter.ParseOperator(">="));
        Assert.Equal(SimdWhereFilter.ComparisonOp.LessOrEqual, SimdWhereFilter.ParseOperator("<="));
        Assert.Equal(SimdWhereFilter.ComparisonOp.NotEqual, SimdWhereFilter.ParseOperator("!="));
        Assert.Equal(SimdWhereFilter.ComparisonOp.NotEqual, SimdWhereFilter.ParseOperator("<>"));
    }

    [Fact]
    public void ParseOperator_InvalidOperator_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => SimdWhereFilter.ParseOperator("??"));
    }

    #endregion

    #region WHERE Clause Analyzer Tests

    [Fact]
    public void WhereClauseAnalyzer_SimpleNumericWhere_ParsesCorrectly()
    {
        // Arrange
        var columns = new List<string> { "id", "name", "age", "salary" };
        var columnTypes = new List<DataType> 
        { 
            DataType.Integer, 
            DataType.String, 
            DataType.Integer, 
            DataType.Decimal 
        };

        // Act
        bool parsed = WhereClauseAnalyzer.TryParseSimpleNumericWhere(
            "salary > 50000", columns, columnTypes, out var metadata);

        // Assert
        Assert.True(parsed);
        Assert.NotNull(metadata);
        Assert.Equal("salary", metadata.ColumnName);
        Assert.Equal(SimdWhereFilter.ComparisonOp.GreaterThan, metadata.Operator);
        Assert.Equal("50000", metadata.ValueString);
        Assert.Equal(DataType.Decimal, metadata.ColumnType);
        Assert.True(metadata.IsSimdOptimizable);
    }

    [Fact]
    public void WhereClauseAnalyzer_NonNumericColumn_ReturnsFalse()
    {
        // Arrange
        var columns = new List<string> { "id", "name" };
        var columnTypes = new List<DataType> 
        { 
            DataType.Integer, 
            DataType.String 
        };

        // Act
        bool parsed = WhereClauseAnalyzer.TryParseSimpleNumericWhere(
            "name > 'Alice'", columns, columnTypes, out var metadata);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void WhereClauseAnalyzer_CompoundWhere_ReturnsFalse()
    {
        // Arrange
        var columns = new List<string> { "age" };
        var columnTypes = new List<DataType> { DataType.Integer };

        // Act
        bool parsed = WhereClauseAnalyzer.TryParseSimpleNumericWhere(
            "age > 30 AND age < 50", columns, columnTypes, out var metadata);

        // Assert
        Assert.False(parsed); // Compound WHERE not supported for SIMD yet
    }

    [Fact]
    public void WhereClauseAnalyzer_IsLikelySimdOptimizable_DetectsCorrectly()
    {
        Assert.True(WhereClauseAnalyzer.IsLikelySimdOptimizable("salary > 50000"));
        Assert.True(WhereClauseAnalyzer.IsLikelySimdOptimizable("age >= 25"));
        Assert.False(WhereClauseAnalyzer.IsLikelySimdOptimizable("name LIKE 'A%'"));
        Assert.False(WhereClauseAnalyzer.IsLikelySimdOptimizable("id IN (1, 2, 3)"));
        Assert.False(WhereClauseAnalyzer.IsLikelySimdOptimizable("age > 30 AND salary < 100000"));
    }

    #endregion
}
