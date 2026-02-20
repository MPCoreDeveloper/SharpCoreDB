using SharpCoreDB.Analytics.TimeSeries;
using Xunit;

namespace SharpCoreDB.Analytics.Tests;

public class TimeSeriesCumulativeTests
{
    [Fact]
    public void CumulativeSum_WithSequentialValues_ShouldReturnExpectedFinalValue()
    {
        // Arrange
        var values = new[] { 3d, 4d, 5d };

        // Act
        var results = values.CumulativeSum(v => v).ToList();

        // Assert
        Assert.Equal(12d, results[^1]);
    }

    [Fact]
    public void CumulativeAverage_WithSequentialValues_ShouldReturnExpectedFinalValue()
    {
        // Arrange
        var values = new[] { 2d, 4d, 8d };

        // Act
        var results = values.CumulativeAverage(v => v).ToList();

        // Assert
        Assert.Equal(14d / 3d, results[^1], 6);
    }
}
