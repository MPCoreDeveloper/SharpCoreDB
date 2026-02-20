using SharpCoreDB.Analytics.TimeSeries;
using Xunit;

namespace SharpCoreDB.Analytics.Tests;

public class TimeSeriesBucketingTests
{
    private sealed record Metric(DateTime Timestamp, double Value);

    [Fact]
    public void BucketByDate_WithDayBucket_ShouldReturnExpectedGroupCount()
    {
        // Arrange
        var metrics = new List<Metric>
        {
            new(new DateTime(2025, 2, 1, 8, 0, 0, DateTimeKind.Utc), 10),
            new(new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc), 20),
            new(new DateTime(2025, 2, 2, 9, 0, 0, DateTimeKind.Utc), 30)
        };

        // Act
        var groups = metrics.BucketByDate(m => m.Timestamp, DateBucket.Day).ToList();

        // Assert
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void BucketByDate_WithDayBucket_ShouldReturnExpectedFirstKey()
    {
        // Arrange
        var metrics = new List<Metric>
        {
            new(new DateTime(2025, 2, 1, 8, 0, 0, DateTimeKind.Utc), 10),
            new(new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc), 20)
        };

        // Act
        var key = metrics.BucketByDate(m => m.Timestamp, DateBucket.Day).First().Key;

        // Assert
        Assert.Equal(new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), key);
    }
}
