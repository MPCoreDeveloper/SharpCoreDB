using SharpCoreDB.Analytics.Aggregation;

namespace SharpCoreDB.Analytics.Tests;

public sealed class BivariateAggregateTests
{
    [Fact]
    public void Correlation_PerfectPositive_ReturnsOne()
    {
        // Arrange
        var corr = new CorrelationAggregate();
        var pairs = new (double, double)[]
        {
            (1.0, 2.0),
            (2.0, 4.0),
            (3.0, 6.0),
            (4.0, 8.0),
            (5.0, 10.0)
        };
        
        // Act
        foreach (var pair in pairs)
        {
            corr.Aggregate(pair);
        }
        
        var result = (double?)corr.GetResult();
        
        // Assert - perfect positive correlation (y = 2x)
        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value, precision: 10);
    }
    
    [Fact]
    public void Correlation_PerfectNegative_ReturnsMinusOne()
    {
        // Arrange
        var corr = new CorrelationAggregate();
        var pairs = new (double, double)[]
        {
            (1.0, 10.0),
            (2.0, 8.0),
            (3.0, 6.0),
            (4.0, 4.0),
            (5.0, 2.0)
        };
        
        // Act
        foreach (var pair in pairs)
        {
            corr.Aggregate(pair);
        }
        
        var result = (double?)corr.GetResult();
        
        // Assert - perfect negative correlation
        Assert.NotNull(result);
        Assert.Equal(-1.0, result.Value, precision: 10);
    }
    
    [Fact]
    public void Correlation_NoCorrelation_ReturnsNearZero()
    {
        // Arrange
        var corr = new CorrelationAggregate();
        var pairs = new (double, double)[]
        {
            (1.0, 5.0),
            (2.0, 3.0),
            (3.0, 7.0),
            (4.0, 2.0),
            (5.0, 6.0)
        };
        
        // Act
        foreach (var pair in pairs)
        {
            corr.Aggregate(pair);
        }
        
        var result = (double?)corr.GetResult();
        
        // Assert - weak or no correlation
        Assert.NotNull(result);
        Assert.True(Math.Abs(result.Value) < 0.5, $"Expected weak correlation, got {result.Value}");
    }
    
    [Fact]
    public void Correlation_ArrayInput_WorksCorrectly()
    {
        // Arrange
        var corr = new CorrelationAggregate();
        var pairs = new double[][]
        {
            [1.0, 2.0],
            [2.0, 4.0],
            [3.0, 6.0]
        };
        
        // Act
        foreach (var pair in pairs)
        {
            corr.Aggregate(pair);
        }
        
        var result = (double?)corr.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value, precision: 10);
    }
    
    [Fact]
    public void Correlation_InsufficientData_ReturnsNull()
    {
        // Arrange
        var corr = new CorrelationAggregate();
        
        // Act
        corr.Aggregate((1.0, 2.0));
        var result = corr.GetResult();
        
        // Assert - need at least 2 pairs
        Assert.Null(result);
    }
    
    [Fact]
    public void Covariance_Population_CalculatesCorrectly()
    {
        // Arrange
        var covar = new CovarianceAggregate(isSample: false);
        var pairs = new (double, double)[]
        {
            (1.0, 2.0),
            (2.0, 4.0),
            (3.0, 6.0),
            (4.0, 8.0),
            (5.0, 10.0)
        };
        
        // Act
        foreach (var pair in pairs)
        {
            covar.Aggregate(pair);
        }
        
        var result = (double?)covar.GetResult();
        
        // Assert - population covariance
        Assert.NotNull(result);
        Assert.Equal(4.0, result.Value, precision: 1);
    }
    
    [Fact]
    public void Covariance_Sample_CalculatesCorrectly()
    {
        // Arrange
        var covar = new CovarianceAggregate(isSample: true);
        var pairs = new (double, double)[]
        {
            (1.0, 2.0),
            (2.0, 4.0),
            (3.0, 6.0),
            (4.0, 8.0),
            (5.0, 10.0)
        };
        
        // Act
        foreach (var pair in pairs)
        {
            covar.Aggregate(pair);
        }
        
        var result = (double?)covar.GetResult();
        
        // Assert - sample covariance (n-1 divisor)
        Assert.NotNull(result);
        Assert.Equal(5.0, result.Value, precision: 1);
    }
    
    [Fact]
    public void Covariance_Sample_SingleValue_ReturnsNull()
    {
        // Arrange
        var covar = new CovarianceAggregate(isSample: true);
        
        // Act
        covar.Aggregate((1.0, 2.0));
        var result = covar.GetResult();
        
        // Assert - sample covariance undefined for n=1
        Assert.Null(result);
    }
    
    [Fact]
    public void Covariance_WithNullValues_IgnoresNulls()
    {
        // Arrange
        var covar = new CovarianceAggregate(isSample: false);
        var pairs = new object?[]
        {
            (1.0, 2.0),
            null,
            (2.0, 4.0),
            null,
            (3.0, 6.0)
        };
        
        // Act
        foreach (var pair in pairs)
        {
            covar.Aggregate(pair);
        }
        
        var result = (double?)covar.GetResult();
        
        // Assert - covariance of [(1,2), (2,4), (3,6)] = 1.33 (population)
        Assert.NotNull(result);
        Assert.Equal(1.33, result.Value, precision: 2);
    }
    
    [Fact]
    public void BivariateAggregates_Reset_ClearsState()
    {
        // Arrange
        var corr = new CorrelationAggregate();
        corr.Aggregate((1.0, 2.0));
        corr.Aggregate((2.0, 4.0));
        
        // Act
        corr.Reset();
        var result = corr.GetResult();
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void Correlation_FunctionName_ReturnsCorrectName()
    {
        // Arrange
        var corr = new CorrelationAggregate();
        
        // Act & Assert
        Assert.Equal("CORR", corr.FunctionName);
    }
    
    [Fact]
    public void Covariance_FunctionName_ReturnsCorrectName()
    {
        // Arrange
        var sampleCovar = new CovarianceAggregate(isSample: true);
        var popCovar = new CovarianceAggregate(isSample: false);
        
        // Act & Assert
        Assert.Equal("COVAR_SAMP", sampleCovar.FunctionName);
        Assert.Equal("COVAR_POP", popCovar.FunctionName);
    }
}
