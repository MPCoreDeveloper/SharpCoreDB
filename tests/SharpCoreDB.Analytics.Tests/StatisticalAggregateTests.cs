using SharpCoreDB.Analytics.Aggregation;

namespace SharpCoreDB.Analytics.Tests;

public sealed class StatisticalAggregateTests
{
    [Fact]
    public void StandardDeviation_Population_CalculatesCorrectly()
    {
        // Arrange
        var stddev = new StandardDeviationAggregate(isSample: false);
        var values = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        
        // Act
        foreach (var value in values)
        {
            stddev.Aggregate(value);
        }
        
        var result = (double?)stddev.GetResult();
        
        // Assert
        Assert.NotNull(result);
        // Population stddev = 2.0
        Assert.Equal(2.0, result.Value, precision: 10);
    }
    
    [Fact]
    public void StandardDeviation_Sample_CalculatesCorrectly()
    {
        // Arrange
        var stddev = new StandardDeviationAggregate(isSample: true);
        var values = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        
        // Act
        foreach (var value in values)
        {
            stddev.Aggregate(value);
        }
        
        var result = (double?)stddev.GetResult();
        
        // Assert
        Assert.NotNull(result);
        // Sample stddev ≈ 2.138
        Assert.Equal(2.138, result.Value, precision: 2);
    }
    
    [Fact]
    public void StandardDeviation_WithNullValues_IgnoresNulls()
    {
        // Arrange
        var stddev = new StandardDeviationAggregate(isSample: false);
        var values = new object?[] { 1.0, null, 2.0, null, 3.0 };
        
        // Act
        foreach (var value in values)
        {
            stddev.Aggregate(value);
        }
        
        var result = (double?)stddev.GetResult();
        
        // Assert - stddev of [1, 2, 3] = 0.8165 (population)
        Assert.NotNull(result);
        Assert.Equal(0.8165, result.Value, precision: 3);
    }
    
    [Fact]
    public void StandardDeviation_Sample_SingleValue_ReturnsNull()
    {
        // Arrange
        var stddev = new StandardDeviationAggregate(isSample: true);
        
        // Act
        stddev.Aggregate(5.0);
        var result = stddev.GetResult();
        
        // Assert - sample stddev undefined for n=1
        Assert.Null(result);
    }
    
    [Fact]
    public void Variance_Population_CalculatesCorrectly()
    {
        // Arrange
        var variance = new VarianceAggregate(isSample: false);
        var values = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        
        // Act
        foreach (var value in values)
        {
            variance.Aggregate(value);
        }
        
        var result = (double?)variance.GetResult();
        
        // Assert
        Assert.NotNull(result);
        // Population variance = 4.0 (stddev² = 2.0² = 4.0)
        Assert.Equal(4.0, result.Value, precision: 10);
    }
    
    [Fact]
    public void Variance_Sample_CalculatesCorrectly()
    {
        // Arrange
        var variance = new VarianceAggregate(isSample: true);
        var values = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        
        // Act
        foreach (var value in values)
        {
            variance.Aggregate(value);
        }
        
        var result = (double?)variance.GetResult();
        
        // Assert
        Assert.NotNull(result);
        // Sample variance ≈ 4.571
        Assert.Equal(4.571, result.Value, precision: 2);
    }
    
    [Fact]
    public void Variance_Sample_SingleValue_ReturnsNull()
    {
        // Arrange
        var variance = new VarianceAggregate(isSample: true);
        
        // Act
        variance.Aggregate(10.0);
        var result = variance.GetResult();
        
        // Assert - sample variance undefined for n=1
        Assert.Null(result);
    }
    
    [Fact]
    public void Variance_WithNullValues_IgnoresNulls()
    {
        // Arrange
        var variance = new VarianceAggregate(isSample: false);
        var values = new object?[] { 10.0, null, 20.0, null, 30.0 };
        
        // Act
        foreach (var value in values)
        {
            variance.Aggregate(value);
        }
        
        var result = (double?)variance.GetResult();
        
        // Assert - variance of [10, 20, 30] = 66.67 (population)
        Assert.NotNull(result);
        Assert.Equal(66.67, result.Value, precision: 1);
    }
    
    [Fact]
    public void StatisticalAggregates_Reset_ClearsState()
    {
        // Arrange
        var stddev = new StandardDeviationAggregate(isSample: false);
        stddev.Aggregate(1.0);
        stddev.Aggregate(2.0);
        stddev.Aggregate(3.0);
        
        // Act
        stddev.Reset();
        var result = stddev.GetResult();
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void StandardDeviation_FunctionName_ReturnsCorrectName()
    {
        // Arrange & Act
        var sampleStdDev = new StandardDeviationAggregate(isSample: true);
        var popStdDev = new StandardDeviationAggregate(isSample: false);
        
        // Assert
        Assert.Equal("STDDEV_SAMP", sampleStdDev.FunctionName);
        Assert.Equal("STDDEV_POP", popStdDev.FunctionName);
    }
    
    [Fact]
    public void Variance_FunctionName_ReturnsCorrectName()
    {
        // Arrange & Act
        var sampleVar = new VarianceAggregate(isSample: true);
        var popVar = new VarianceAggregate(isSample: false);
        
        // Assert
        Assert.Equal("VAR_SAMP", sampleVar.FunctionName);
        Assert.Equal("VAR_POP", popVar.FunctionName);
    }
}
