using SharpCoreDB.Analytics.Aggregation;

namespace SharpCoreDB.Analytics.Tests;

public sealed class FrequencyAggregateTests
{
    [Fact]
    public void Mode_SingleMode_ReturnsCorrectValue()
    {
        // Arrange
        var mode = new ModeAggregate();
        var values = new[] { 1.0, 2.0, 2.0, 2.0, 3.0, 4.0, 5.0 };
        
        // Act
        foreach (var value in values)
        {
            mode.Aggregate(value);
        }
        
        var result = (double?)mode.GetResult();
        
        // Assert - 2.0 appears 3 times (most frequent)
        Assert.NotNull(result);
        Assert.Equal(2.0, result.Value);
    }
    
    [Fact]
    public void Mode_AllValuesSame_ReturnsThatValue()
    {
        // Arrange
        var mode = new ModeAggregate();
        var values = new[] { 7.0, 7.0, 7.0, 7.0 };
        
        // Act
        foreach (var value in values)
        {
            mode.Aggregate(value);
        }
        
        var result = (double?)mode.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(7.0, result.Value);
    }
    
    [Fact]
    public void Mode_TiedValues_ReturnsFirstToReachMaxFrequency()
    {
        // Arrange
        var mode = new ModeAggregate();
        // Both 2.0 and 4.0 appear twice, but 2.0 reaches frequency=2 first
        var values = new[] { 2.0, 4.0, 2.0, 4.0 };
        
        // Act
        foreach (var value in values)
        {
            mode.Aggregate(value);
        }
        
        var result = (double?)mode.GetResult();
        
        // Assert - 2.0 should be returned (first to reach max frequency)
        Assert.NotNull(result);
        Assert.Equal(2.0, result.Value);
    }
    
    [Fact]
    public void Mode_WithNullValues_IgnoresNulls()
    {
        // Arrange
        var mode = new ModeAggregate();
        var values = new object?[] { 1.0, null, 3.0, 3.0, null, 3.0, 5.0 };
        
        // Act
        foreach (var value in values)
        {
            mode.Aggregate(value);
        }
        
        var result = (double?)mode.GetResult();
        
        // Assert - 3.0 appears 3 times (most frequent non-null)
        Assert.NotNull(result);
        Assert.Equal(3.0, result.Value);
    }
    
    [Fact]
    public void Mode_SingleValue_ReturnsThatValue()
    {
        // Arrange
        var mode = new ModeAggregate();
        
        // Act
        mode.Aggregate(42.0);
        var result = (double?)mode.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.Value);
    }
    
    [Fact]
    public void Mode_NoValues_ReturnsNull()
    {
        // Arrange
        var mode = new ModeAggregate();
        
        // Act
        var result = mode.GetResult();
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void Mode_Reset_ClearsState()
    {
        // Arrange
        var mode = new ModeAggregate();
        mode.Aggregate(1.0);
        mode.Aggregate(2.0);
        mode.Aggregate(2.0);
        
        // Act
        mode.Reset();
        var result = mode.GetResult();
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void Mode_FunctionName_ReturnsCorrectName()
    {
        // Arrange
        var mode = new ModeAggregate();
        
        // Act & Assert
        Assert.Equal("MODE", mode.FunctionName);
    }
}
