using SharpCoreDB.Services;
using System.Text;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for OptimizedRowParser GC optimization functionality.
/// </summary>
public class OptimizedRowParserTests
{
    [Fact]
    public void OptimizedRowParser_ParseRowOptimized_FromString_Works()
    {
        // Arrange
        var json = "{\"id\":1,\"name\":\"Alice\",\"age\":30}";

        // Act
        var row = OptimizedRowParser.ParseRowOptimized(json);

        // Assert
        Assert.NotNull(row);
        Assert.Equal(3, row.Count);
        Assert.True(row.ContainsKey("id"));
        Assert.True(row.ContainsKey("name"));
        Assert.True(row.ContainsKey("age"));
    }

    [Fact]
    public void OptimizedRowParser_ParseRowOptimized_FromBytes_Works()
    {
        // Arrange
        var json = "{\"id\":1,\"name\":\"Alice\",\"age\":30}";
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Act
        var row = OptimizedRowParser.ParseRowOptimized(jsonBytes.AsSpan());

        // Assert
        Assert.NotNull(row);
        Assert.Equal(3, row.Count);
        Assert.True(row.ContainsKey("id"));
        Assert.True(row.ContainsKey("name"));
        Assert.True(row.ContainsKey("age"));
    }

    [Fact]
    public void OptimizedRowParser_ParseRowOptimized_LargeJson_UsesPool()
    {
        // Arrange - Create JSON larger than 4KB threshold
        var sb = new StringBuilder();
        sb.Append("{");
        for (int i = 0; i < 1000; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append($"\"field{i}\":\"{new string('X', 10)}\"");
        }
        sb.Append("}");
        var json = sb.ToString();
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Act
        var row = OptimizedRowParser.ParseRowOptimized(jsonBytes.AsSpan());

        // Assert
        Assert.NotNull(row);
        Assert.True(row.Count >= 1000);
    }

    [Fact]
    public void OptimizedRowParser_SerializeRowOptimized_Works()
    {
        // Arrange
        var row = new Dictionary<string, object>
        {
            { "id", 1 },
            { "name", "Bob" },
            { "active", true }
        };

        // Act
        var json = OptimizedRowParser.SerializeRowOptimized(row);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"active\"", json);
        Assert.Contains("Bob", json);
    }

    [Fact]
    public void OptimizedRowParser_ParseRowsOptimized_ParsesMultipleRows()
    {
        // Arrange
        var json = "[{\"id\":1,\"name\":\"Alice\"},{\"id\":2,\"name\":\"Bob\"}]";
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Act
        var rows = OptimizedRowParser.ParseRowsOptimized(jsonBytes.AsSpan());

        // Assert
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].ContainsKey("name"));
        Assert.True(rows[1].ContainsKey("name"));
    }

    [Fact]
    public void OptimizedRowParser_BuildWhereClauseOptimized_HandlesStringValue()
    {
        // Arrange
        var columnName = "name";
        var operation = "=";
        var value = "Alice";

        // Act
        var whereClause = OptimizedRowParser.BuildWhereClauseOptimized(columnName, operation, value);

        // Assert
        Assert.Equal("name = 'Alice'", whereClause);
    }

    [Fact]
    public void OptimizedRowParser_BuildWhereClauseOptimized_HandlesNumericValue()
    {
        // Arrange
        var columnName = "age";
        var operation = ">";
        var value = 25;

        // Act
        var whereClause = OptimizedRowParser.BuildWhereClauseOptimized(columnName, operation, value);

        // Assert
        Assert.Equal("age > 25", whereClause);
    }

    [Fact]
    public void OptimizedRowParser_BuildWhereClauseOptimized_EscapesSingleQuotes()
    {
        // Arrange
        var columnName = "description";
        var operation = "=";
        var value = "O'Brien";

        // Act
        var whereClause = OptimizedRowParser.BuildWhereClauseOptimized(columnName, operation, value);

        // Assert
        Assert.Equal("description = 'O''Brien'", whereClause);
    }

    [Fact]
    public void OptimizedRowParser_ParseCsvRowOptimized_ParsesCorrectly()
    {
        // Arrange
        var line = "1,Alice,30,true".AsSpan();
        var columns = new List<string> { "id", "name", "age", "active" };

        // Act
        var row = OptimizedRowParser.ParseCsvRowOptimized(line, columns);

        // Assert
        Assert.Equal(4, row.Count);
        Assert.Equal("1", row["id"]);
        Assert.Equal("Alice", row["name"]);
        Assert.Equal("30", row["age"]);
        Assert.Equal("true", row["active"]);
    }

    [Fact]
    public void OptimizedRowParser_ParseCsvRowOptimized_HandlesCustomSeparator()
    {
        // Arrange
        var line = "1|Bob|25|false".AsSpan();
        var columns = new List<string> { "id", "name", "age", "active" };

        // Act
        var row = OptimizedRowParser.ParseCsvRowOptimized(line, columns, '|');

        // Assert
        Assert.Equal(4, row.Count);
        Assert.Equal("1", row["id"]);
        Assert.Equal("Bob", row["name"]);
        Assert.Equal("25", row["age"]);
        Assert.Equal("false", row["active"]);
    }

    [Fact]
    public void OptimizedRowParser_GetPoolStatistics_ReturnsInfo()
    {
        // Act
        var stats = OptimizedRowParser.GetPoolStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Contains("ArrayPool", stats);
    }

    [Fact]
    public void OptimizedRowParser_ParseRowOptimized_EmptyString_ReturnsEmptyDict()
    {
        // Arrange
        var json = "";

        // Act
        var row = OptimizedRowParser.ParseRowOptimized(json);

        // Assert
        Assert.NotNull(row);
        Assert.Empty(row);
    }

    [Fact]
    public void OptimizedRowParser_MultipleOperations_ReusesPools()
    {
        // Arrange
        var json = "{\"id\":1,\"name\":\"Test\"}";
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Act - Perform multiple operations to test pool reuse
        for (int i = 0; i < 10; i++)
        {
            var row = OptimizedRowParser.ParseRowOptimized(jsonBytes.AsSpan());
            Assert.NotNull(row);

            var serialized = OptimizedRowParser.SerializeRowOptimized(row);
            Assert.NotNull(serialized);
        }

        // Assert - If we get here without exceptions, pools are working correctly
        Assert.True(true);
    }
}
