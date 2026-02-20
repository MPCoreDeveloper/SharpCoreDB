using SharpCoreDB.Analytics.OLAP;
using Xunit;

namespace SharpCoreDB.Analytics.Tests;

public class OlapPivotTests
{
    private sealed record Sale(string Region, string Product, decimal Amount);

    [Fact]
    public void ToPivotTable_WithTwoDimensions_ShouldReturnExpectedRowCount()
    {
        // Arrange
        var sales = new List<Sale>
        {
            new("North", "Electronics", 500m),
            new("North", "Food", 200m),
            new("South", "Electronics", 300m)
        };

        // Act
        var pivot = sales
            .AsOlapCube()
            .WithDimensions(s => s.Region, s => s.Product)
            .WithMeasure(group => group.Sum(s => s.Amount))
            .ToPivotTable();

        // Assert
        Assert.Equal(2, pivot.RowHeaders.Count);
    }

    [Fact]
    public void ToPivotTable_WithTwoDimensions_ShouldReturnExpectedColumnCount()
    {
        // Arrange
        var sales = new List<Sale>
        {
            new("North", "Electronics", 500m),
            new("North", "Food", 200m),
            new("South", "Electronics", 300m)
        };

        // Act
        var pivot = sales
            .AsOlapCube()
            .WithDimensions(s => s.Region, s => s.Product)
            .WithMeasure(group => group.Sum(s => s.Amount))
            .ToPivotTable();

        // Assert
        Assert.Equal(2, pivot.ColumnHeaders.Count);
    }

    [Fact]
    public void ToPivotTable_WithMeasure_ShouldReturnExpectedValue()
    {
        // Arrange
        var sales = new List<Sale>
        {
            new("North", "Electronics", 500m),
            new("North", "Food", 200m),
            new("South", "Electronics", 300m)
        };

        // Act
        var pivot = sales
            .AsOlapCube()
            .WithDimensions(s => s.Region, s => s.Product)
            .WithMeasure(group => group.Sum(s => s.Amount))
            .ToPivotTable();
        var value = pivot.GetValue("North", "Electronics");

        // Assert
        Assert.Equal(500m, (decimal?)value);
    }
}
