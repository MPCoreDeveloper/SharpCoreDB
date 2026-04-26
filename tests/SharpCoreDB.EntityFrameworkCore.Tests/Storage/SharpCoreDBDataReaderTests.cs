namespace SharpCoreDB.EntityFrameworkCore.Tests.Storage;

using SharpCoreDB.EntityFrameworkCore.Storage;

public sealed class SharpCoreDBDataReaderTests
{
    [Fact]
    public void Constructor_WithQualifiedAndQuotedKeys_ShouldNormalizeColumnsAndReturnValues()
    {
        // Arrange
        List<Dictionary<string, object>> results =
        [
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["\"w\".\"Id\""] = 42,
                ["\"w\".\"PayloadJson\""] = "{\"name\":\"test\"}",
            },
        ];

        using var reader = new SharpCoreDBDataReader(results);

        // Act
        _ = reader.Read();
        var idOrdinal = reader.GetOrdinal("Id");
        var payloadOrdinal = reader.GetOrdinal("PayloadJson");
        var id = reader.GetInt32(idOrdinal);
        var payload = reader.GetString(payloadOrdinal);

        // Assert
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal(42, id);
        Assert.Equal("{\"name\":\"test\"}", payload);
    }

    [Fact]
    public void Constructor_WithDuplicateQualifiedColumns_ShouldDeduplicateFieldNames()
    {
        // Arrange
        List<Dictionary<string, object>> results =
        [
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = 7,
                ["w.Id"] = 7,
                ["Products.Id"] = 7,
                ["Name"] = "Widget",
                ["w.Name"] = "Widget",
            },
        ];

        using var reader = new SharpCoreDBDataReader(results);

        // Act
        _ = reader.Read();

        // Assert
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal(7, reader.GetInt32(reader.GetOrdinal("Id")));
        Assert.Equal("Widget", reader.GetString(reader.GetOrdinal("Name")));
    }

    [Fact]
    public void GetOrdinal_WithQuotedQualifiedName_ShouldResolveToNormalizedColumn()
    {
        // Arrange
        List<Dictionary<string, object>> results =
        [
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["\"w\".\"Id\""] = 1,
            },
        ];

        using var reader = new SharpCoreDBDataReader(results);

        // Act
        var ordinal = reader.GetOrdinal("\"w\".\"Id\"");

        // Assert
        Assert.Equal(0, ordinal);
    }
}
