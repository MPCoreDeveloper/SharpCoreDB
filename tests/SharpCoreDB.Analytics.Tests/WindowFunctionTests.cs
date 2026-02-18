using SharpCoreDB.Analytics.WindowFunctions;
using Xunit;

namespace SharpCoreDB.Analytics.Tests;

public class WindowFunctionTests
{
    [Fact]
    public void RowNumber_WithMultipleValues_ShouldAssignSequentialNumbers()
    {
        // Arrange
        var rowNum = new RowNumberFunction();
        
        // Act
        var result1 = rowNum.GetResult();
        rowNum.ProcessValue("val1");
        var result2 = rowNum.GetResult();
        rowNum.ProcessValue("val2");
        var result3 = rowNum.GetResult();
        
        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(3, result3);
    }
    
    [Fact]
    public void Rank_WithSequentialValues_ShouldProduceCorrectRanks()
    {
        // Arrange
        var rank = new RankFunction();
        
        // Act
        var result1 = rank.GetResult();
        rank.ProcessValue("val1");
        var result2 = rank.GetResult();
        rank.ProcessValue("val2");
        var result3 = rank.GetResult();
        
        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(3, result3);
    }
    
    [Fact]
    public void DenseRank_ShouldAssignConsecutiveRanks()
    {
        // Arrange
        var denseRank = new DenseRankFunction();
        
        // Act
        var result1 = denseRank.GetResult();
        denseRank.ProcessValue("val1");
        var result2 = denseRank.GetResult();
        denseRank.ProcessValue("val2");
        var result3 = denseRank.GetResult();
        
        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(3, result3);
    }
    
    [Fact]
    public void Lag_WithOffset_ShouldReturnPreviousValue()
    {
        // Arrange
        var lag = new LagFunction(offset: 1);
        
        // Act
        lag.ProcessValue("A");
        var result1 = lag.GetResult();  // null (no previous)
        lag.ProcessValue("B");
        var result2 = lag.GetResult();  // "A"
        lag.ProcessValue("C");
        var result3 = lag.GetResult();  // "B"
        
        // Assert
        Assert.Null(result1);
        Assert.Equal("A", result2);
        Assert.Equal("B", result3);
    }
    
    [Fact]
    public void Lead_WithOffset_ShouldReturnNextValue()
    {
        // Arrange
        var values = new[] { "A", "B", "C", "D" };
        var lead = new LeadFunction(offset: 1);
        
        // Pre-populate all values
        foreach (var value in values)
        {
            lead.ProcessValue(value);
        }
        
        // Act
        var result1 = lead.GetResult();  // "B"
        var result2 = lead.GetResult();  // "C"
        var result3 = lead.GetResult();  // "D"
        var result4 = lead.GetResult();  // null
        
        // Assert
        Assert.Equal("B", result1);
        Assert.Equal("C", result2);
        Assert.Equal("D", result3);
        Assert.Null(result4);
    }
    
    [Fact]
    public void FirstValue_ShouldReturnFirstProcessedValue()
    {
        // Arrange
        var firstValue = new FirstValueFunction();
        
        // Act
        firstValue.ProcessValue("A");
        firstValue.ProcessValue("B");
        firstValue.ProcessValue("C");
        
        // Assert
        Assert.Equal("A", firstValue.GetResult());
    }
    
    [Fact]
    public void LastValue_ShouldReturnLastProcessedValue()
    {
        // Arrange
        var lastValue = new LastValueFunction();
        
        // Act
        lastValue.ProcessValue("A");
        lastValue.ProcessValue("B");
        lastValue.ProcessValue("C");
        
        // Assert
        Assert.Equal("C", lastValue.GetResult());
    }
}

public class WindowFunctionFactoryTests
{
    [Fact]
    public void Factory_WithValidWindowFunction_ShouldCreateCorrectFunction()
    {
        // Act
        var rowNum = WindowFunctionFactory.CreateWindowFunction("ROW_NUMBER");
        var rank = WindowFunctionFactory.CreateWindowFunction("RANK");
        var denseRank = WindowFunctionFactory.CreateWindowFunction("DENSE_RANK");
        var lag = WindowFunctionFactory.CreateWindowFunction("LAG", 1);
        var lead = WindowFunctionFactory.CreateWindowFunction("LEAD", 1);
        
        // Assert
        Assert.Equal("ROW_NUMBER", rowNum.FunctionName);
        Assert.Equal("RANK", rank.FunctionName);
        Assert.Equal("DENSE_RANK", denseRank.FunctionName);
        Assert.Equal("LAG", lag.FunctionName);
        Assert.Equal("LEAD", lead.FunctionName);
    }
    
    [Fact]
    public void Factory_WithInvalidFunction_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            WindowFunctionFactory.CreateWindowFunction("INVALID"));
    }
}
