using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Tests.DataProvider;

public sealed class SharpCoreDBConnectionAndParameterTests
{
    [Fact]
    public void ConnectionString_WhenSetNull_ShouldBecomeEmptyString()
    {
        // Arrange
        using var connection = new SharpCoreDBConnection();

        // Act
        connection.ConnectionString = null;

        // Assert
        Assert.Equal(string.Empty, connection.ConnectionString);
    }

    [Fact]
    public void ParameterName_WhenSetNull_ShouldKeepNullAssignmentSafe()
    {
        // Arrange
        var parameter = new SharpCoreDBParameter();

        // Act
        parameter.ParameterName = null;

        // Assert
        Assert.Null(parameter.ParameterName);
    }

    [Fact]
    public void SourceColumn_WhenSetNull_ShouldKeepNullAssignmentSafe()
    {
        // Arrange
        var parameter = new SharpCoreDBParameter();

        // Act
        parameter.SourceColumn = null;

        // Assert
        Assert.Null(parameter.SourceColumn);
    }
}
