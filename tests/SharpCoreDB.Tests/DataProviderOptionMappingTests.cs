namespace SharpCoreDB.Tests;

using System.Data;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Functional;

public sealed class DataProviderOptionMappingTests
{
    [Fact]
    public void DataReader_WithOptionalProjection_ShouldReturnOptionSomeForNonNull()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = "alice@example.com"
            }
        };

        using var reader = new SharpCoreDBDataReader(rows, CommandBehavior.Default, useOptionalProjection: true);

        // Act
        Assert.True(reader.Read());
        var value = reader.GetValue(0);

        // Assert
        var option = Assert.IsType<Option<string>>(value);
        Assert.True(option.IsSome);
        Assert.False(option.IsNone);
    }

    [Fact]
    public void DataReader_WithOptionalProjection_ShouldReturnOptionNoneForNull()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = DBNull.Value
            }
        };

        using var reader = new SharpCoreDBDataReader(rows, CommandBehavior.Default, useOptionalProjection: true);

        // Act
        Assert.True(reader.Read());
        var value = reader.GetValue(0);

        // Assert
        var option = Assert.IsType<Option<object>>(value);
        Assert.True(option.IsNone);
        Assert.False(option.IsSome);
    }

    [Fact]
    public void DataReader_WithOptionalProjection_GetFieldTypeShouldBeOptionOfInferredType()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = 123
            }
        };

        using var reader = new SharpCoreDBDataReader(rows, CommandBehavior.Default, useOptionalProjection: true);

        // Act
        var fieldType = reader.GetFieldType(0);

        // Assert
        Assert.Equal(typeof(Option<int>), fieldType);
    }
}
