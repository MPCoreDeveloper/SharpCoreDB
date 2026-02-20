using SharpCoreDB.Analytics.TimeSeries;
using Xunit;

namespace SharpCoreDB.Analytics.Tests;

public class TimeSeriesRollingTests
{
    [Fact]
    public void RollingSum_WithWindowSize3_ShouldReturnExpectedFinalValue()
    {
        // Arrange
        var values = new[] { 1d, 2d, 3d, 4d };

        // Act
        var results = values.RollingSum(v => v, 3).ToList();

        // Assert
        Assert.Equal(9d, results[^1]);
    }

    [Fact]
    public void RollingAverage_WithWindowSize2_ShouldReturnExpectedFinalValue()
    {
        // Arrange
        var values = new[] { 2d, 4d, 6d };

        // Act
        var results = values.RollingAverage(v => v, 2).ToList();

        // Assert
        Assert.Equal(5d, results[^1]);
    }
}
