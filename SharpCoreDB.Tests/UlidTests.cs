using SharpCoreDB;

namespace SharpCoreDB.Tests;

/// <summary>
/// Unit tests for the Ulid (Universally Unique Lexicographically Sortable Identifier) class.
/// Tests ULID generation, parsing, and timestamp extraction functionality.
/// </summary>
public class UlidTests
{
    [Fact]
    public void Ulid_NewUlid_GeneratesValidUlid()
    {
        // Act
        var ulid = Ulid.NewUlid();

        // Assert
        Assert.NotNull(ulid);
        Assert.NotNull(ulid.Value);
        Assert.NotEmpty(ulid.Value);
        Assert.Equal(26, ulid.Value.Length); // ULIDs are 26 characters
    }

    [Fact]
    public void Ulid_NewUlid_GeneratesUniqueValues()
    {
        // Act
        var ulid1 = Ulid.NewUlid();
        var ulid2 = Ulid.NewUlid();

        // Assert
        Assert.NotEqual(ulid1.Value, ulid2.Value);
    }

    [Fact]
    public void Ulid_Parse_ValidUlid_Success()
    {
        // Arrange
        var originalUlid = Ulid.NewUlid();

        // Act
        var parsedUlid = Ulid.Parse(originalUlid.Value);

        // Assert
        Assert.NotNull(parsedUlid);
        Assert.Equal(originalUlid.Value, parsedUlid.Value);
    }

    [Fact]
    public void Ulid_ToDateTime_ReturnsValidDateTime()
    {
        // Arrange
        var beforeGeneration = DateTime.UtcNow.AddSeconds(-1);
        var ulid = Ulid.NewUlid();
        var afterGeneration = DateTime.UtcNow.AddSeconds(1);

        // Act
        var parsedUlid = Ulid.Parse(ulid.Value);
        var timestamp = parsedUlid.ToDateTime();

        // Assert
        Assert.True(timestamp >= beforeGeneration);
        Assert.True(timestamp <= afterGeneration);
    }

    [Fact]
    public void Ulid_Value_IsUpperCase()
    {
        // Act
        var ulid = Ulid.NewUlid();

        // Assert
        Assert.Equal(ulid.Value, ulid.Value.ToUpper());
    }

    [Fact]
    public void Ulid_OrderedByTime_LexicographicallySortable()
    {
        // Arrange - Generate ULIDs with slight time delays
        var ulid1 = Ulid.NewUlid();
        Thread.Sleep(10); // Small delay to ensure different timestamps
        var ulid2 = Ulid.NewUlid();
        Thread.Sleep(10);
        var ulid3 = Ulid.NewUlid();

        // Act - Compare lexicographically
        var comparison1 = string.Compare(ulid1.Value, ulid2.Value, StringComparison.Ordinal);
        var comparison2 = string.Compare(ulid2.Value, ulid3.Value, StringComparison.Ordinal);

        // Assert - Later ULIDs should be lexicographically greater
        Assert.True(comparison1 < 0); // ulid1 < ulid2
        Assert.True(comparison2 < 0); // ulid2 < ulid3
    }

    [Fact]
    public void Ulid_MultipleGenerations_AllValid()
    {
        // Act
        var ulids = new List<Ulid>();
        for (int i = 0; i < 100; i++)
        {
            ulids.Add(Ulid.NewUlid());
        }

        // Assert
        Assert.Equal(100, ulids.Count);
        foreach (var ulid in ulids)
        {
            Assert.NotNull(ulid.Value);
            Assert.Equal(26, ulid.Value.Length);
        }
        
        // Verify all are unique
        var uniqueValues = ulids.Select(u => u.Value).Distinct().Count();
        Assert.Equal(100, uniqueValues);
    }

    [Fact]
    public void Ulid_ToString_ReturnsValue()
    {
        // Arrange
        var ulid = Ulid.NewUlid();

        // Act
        var stringValue = ulid.ToString();

        // Assert
        Assert.Equal(ulid.Value, stringValue);
    }
}
