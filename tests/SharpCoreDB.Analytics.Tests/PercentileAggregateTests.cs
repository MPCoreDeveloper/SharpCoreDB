using SharpCoreDB.Analytics.Aggregation;

namespace SharpCoreDB.Analytics.Tests;

public sealed class PercentileAggregateTests
{
    [Fact]
    public void Median_OddCount_ReturnsMiddleValue()
    {
        // Arrange
        var median = new MedianAggregate();
        var values = new[] { 1.0, 3.0, 5.0, 7.0, 9.0 };
        
        // Act
        foreach (var value in values)
        {
            median.Aggregate(value);
        }
        
        var result = (double?)median.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(5.0, result.Value);
    }
    
    [Fact]
    public void Median_EvenCount_ReturnsAverageOfMiddleValues()
    {
        // Arrange
        var median = new MedianAggregate();
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };
        
        // Act
        foreach (var value in values)
        {
            median.Aggregate(value);
        }
        
        var result = (double?)median.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(3.5, result.Value); // (3 + 4) / 2
    }
    
    [Fact]
    public void Median_SingleValue_ReturnsThatValue()
    {
        // Arrange
        var median = new MedianAggregate();
        
        // Act
        median.Aggregate(42.0);
        var result = (double?)median.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.Value);
    }
    
    [Fact]
    public void Median_WithNullValues_IgnoresNulls()
    {
        // Arrange
        var median = new MedianAggregate();
        var values = new object?[] { 1.0, null, 3.0, null, 5.0 };
        
        // Act
        foreach (var value in values)
        {
            median.Aggregate(value);
        }
        
        var result = (double?)median.GetResult();
        
        // Assert - median of [1, 3, 5] = 3
        Assert.NotNull(result);
        Assert.Equal(3.0, result.Value);
    }
    
    [Fact]
    public void Median_UnsortedData_SortsCorrectly()
    {
        // Arrange
        var median = new MedianAggregate();
        var values = new[] { 9.0, 1.0, 5.0, 3.0, 7.0 };
        
        // Act
        foreach (var value in values)
        {
            median.Aggregate(value);
        }
        
        var result = (double?)median.GetResult();
        
        // Assert - sorted: [1, 3, 5, 7, 9], median = 5
        Assert.NotNull(result);
        Assert.Equal(5.0, result.Value);
    }
    
    [Fact]
    public void Percentile_P50_EqualsMedian()
    {
        // Arrange
        var p50 = new PercentileAggregate(0.5);
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        
        // Act
        foreach (var value in values)
        {
            p50.Aggregate(value);
        }
        
        var result = (double?)p50.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(3.0, result.Value);
    }
    
    [Fact]
    public void Percentile_P95_CalculatesCorrectly()
    {
        // Arrange
        var p95 = new PercentileAggregate(0.95);
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };
        
        // Act
        foreach (var value in values)
        {
            p95.Aggregate(value);
        }
        
        var result = (double?)p95.GetResult();
        
        // Assert - P95 with 10 values: rank = 0.95 * 9 = 8.55
        // Interpolate between index 8 (value=9) and index 9 (value=10)
        Assert.NotNull(result);
        Assert.Equal(9.55, result.Value, precision: 2);
    }
    
    [Fact]
    public void Percentile_P99_CalculatesCorrectly()
    {
        // Arrange
        var p99 = new PercentileAggregate(0.99);
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };
        
        // Act
        foreach (var value in values)
        {
            p99.Aggregate(value);
        }
        
        var result = (double?)p99.GetResult();
        
        // Assert - P99 with 10 values: rank = 0.99 * 9 = 8.91
        Assert.NotNull(result);
        Assert.Equal(9.91, result.Value, precision: 2);
    }
    
    [Fact]
    public void Percentile_P0_ReturnsMinimum()
    {
        // Arrange
        var p0 = new PercentileAggregate(0.0);
        var values = new[] { 5.0, 3.0, 9.0, 1.0, 7.0 };
        
        // Act
        foreach (var value in values)
        {
            p0.Aggregate(value);
        }
        
        var result = (double?)p0.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value);
    }
    
    [Fact]
    public void Percentile_P100_ReturnsMaximum()
    {
        // Arrange
        var p100 = new PercentileAggregate(1.0);
        var values = new[] { 5.0, 3.0, 9.0, 1.0, 7.0 };
        
        // Act
        foreach (var value in values)
        {
            p100.Aggregate(value);
        }
        
        var result = (double?)p100.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(9.0, result.Value);
    }
    
    [Fact]
    public void Percentile_WithNullValues_IgnoresNulls()
    {
        // Arrange
        var p50 = new PercentileAggregate(0.5);
        var values = new object?[] { 1.0, null, 5.0, null, 9.0 };
        
        // Act
        foreach (var value in values)
        {
            p50.Aggregate(value);
        }
        
        var result = (double?)p50.GetResult();
        
        // Assert - median of [1, 5, 9] = 5
        Assert.NotNull(result);
        Assert.Equal(5.0, result.Value);
    }
    
    [Fact]
    public void PercentileAggregates_Reset_ClearsState()
    {
        // Arrange
        var median = new MedianAggregate();
        median.Aggregate(1.0);
        median.Aggregate(2.0);
        median.Aggregate(3.0);
        
        // Act
        median.Reset();
        var result = median.GetResult();
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void Percentile_FunctionName_FormatsCorrectly()
    {
        // Arrange & Act
        var p50 = new PercentileAggregate(0.5);
        var p95 = new PercentileAggregate(0.95);
        var p99 = new PercentileAggregate(0.99);
        
        // Assert
        Assert.Equal("PERCENTILE_50", p50.FunctionName);
        Assert.Equal("PERCENTILE_95", p95.FunctionName);
        Assert.Equal("PERCENTILE_99", p99.FunctionName);
    }
}
