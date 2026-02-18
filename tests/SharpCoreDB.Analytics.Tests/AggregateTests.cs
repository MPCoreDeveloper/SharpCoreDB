using SharpCoreDB.Analytics.Aggregation;
using Xunit;

namespace SharpCoreDB.Analytics.Tests;

public class SumAggregateTests
{
    [Fact]
    public void Sum_WithPositiveNumbers_ShouldCalculateCorrectly()
    {
        // Arrange
        var sum = new SumAggregate();
        
        // Act
        sum.Aggregate(10);
        sum.Aggregate(20);
        sum.Aggregate(30);
        
        // Assert
        Assert.Equal(60m, sum.GetResult());
    }
    
    [Fact]
    public void Sum_WithNullValues_ShouldIgnoreNulls()
    {
        // Arrange
        var sum = new SumAggregate();
        
        // Act
        sum.Aggregate(10);
        sum.Aggregate(null);
        sum.Aggregate(20);
        
        // Assert
        Assert.Equal(30m, sum.GetResult());
    }
    
    [Fact]
    public void Sum_WithEmptyAggregate_ShouldReturnNull()
    {
        // Arrange
        var sum = new SumAggregate();
        
        // Act & Assert
        Assert.Null(sum.GetResult());
    }
    
    [Fact]
    public void Sum_AfterReset_ShouldStartOver()
    {
        // Arrange
        var sum = new SumAggregate();
        sum.Aggregate(50);
        
        // Act
        sum.Reset();
        sum.Aggregate(10);
        sum.Aggregate(20);
        
        // Assert
        Assert.Equal(30m, sum.GetResult());
    }
}

public class CountAggregateTests
{
    [Fact]
    public void Count_WithMultipleValues_ShouldReturnCorrectCount()
    {
        // Arrange
        var count = new CountAggregate();
        
        // Act
        count.Aggregate(10);
        count.Aggregate(20);
        count.Aggregate(30);
        
        // Assert
        Assert.Equal(3L, count.GetResult());
    }
    
    [Fact]
    public void Count_WithNullValues_ShouldIgnoreNulls()
    {
        // Arrange
        var count = new CountAggregate();
        
        // Act
        count.Aggregate(10);
        count.Aggregate(null);
        count.Aggregate(20);
        count.Aggregate(null);
        
        // Assert
        Assert.Equal(2L, count.GetResult());
    }
    
    [Fact]
    public void Count_WithEmptyAggregate_ShouldReturnZero()
    {
        // Arrange
        var count = new CountAggregate();
        
        // Act & Assert
        Assert.Equal(0L, count.GetResult());
    }
}

public class AverageAggregateTests
{
    [Fact]
    public void Average_WithMultipleValues_ShouldCalculateCorrectly()
    {
        // Arrange
        var avg = new AverageAggregate();
        
        // Act
        avg.Aggregate(10);
        avg.Aggregate(20);
        avg.Aggregate(30);
        
        // Assert
        Assert.Equal(20m, avg.GetResult());
    }
    
    [Fact]
    public void Average_WithEmptyAggregate_ShouldReturnNull()
    {
        // Arrange
        var avg = new AverageAggregate();
        
        // Act & Assert
        Assert.Null(avg.GetResult());
    }
}

public class MinMaxAggregateTests
{
    [Fact]
    public void Min_WithMultipleValues_ShouldReturnSmallest()
    {
        // Arrange
        var min = new MinAggregate();
        
        // Act
        min.Aggregate(30);
        min.Aggregate(10);
        min.Aggregate(20);
        
        // Assert
        Assert.Equal(10m, min.GetResult());
    }
    
    [Fact]
    public void Max_WithMultipleValues_ShouldReturnLargest()
    {
        // Arrange
        var max = new MaxAggregate();
        
        // Act
        max.Aggregate(30);
        max.Aggregate(10);
        max.Aggregate(50);
        max.Aggregate(20);
        
        // Assert
        Assert.Equal(50m, max.GetResult());
    }
}

public class AggregateFactoryTests
{
    [Fact]
    public void Factory_WithValidFunctionName_ShouldCreateCorrectAggregate()
    {
        // Act
        var sum = AggregateFactory.CreateAggregate("SUM");
        var count = AggregateFactory.CreateAggregate("COUNT");
        var avg = AggregateFactory.CreateAggregate("AVERAGE");
        
        // Assert
        Assert.NotNull(sum);
        Assert.NotNull(count);
        Assert.NotNull(avg);
        Assert.Equal("SUM", sum.FunctionName);
        Assert.Equal("COUNT", count.FunctionName);
        Assert.Equal("AVERAGE", avg.FunctionName);
    }
    
    [Fact]
    public void Factory_WithInvalidFunctionName_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            AggregateFactory.CreateAggregate("INVALID"));
    }
}
