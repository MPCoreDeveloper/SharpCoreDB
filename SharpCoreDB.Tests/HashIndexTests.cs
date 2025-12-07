using SharpCoreDB.DataStructures;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for HashIndex functionality.
/// </summary>
public class HashIndexTests
{
    [Fact]
    public void HashIndex_AddAndLookup_FindsRows()
    {
        // Arrange
        var index = new HashIndex("users", "id");
        var row1 = new Dictionary<string, object> { { "id", 1 }, { "name", "Alice" } };
        var row2 = new Dictionary<string, object> { { "id", 2 }, { "name", "Bob" } };
        var row3 = new Dictionary<string, object> { { "id", 1 }, { "name", "Alice2" } };

        // Act
        index.Add(row1, 0);
        index.Add(row2, 1);
        index.Add(row3, 2);

        var positions1 = index.LookupPositions(1);
        var positions2 = index.LookupPositions(2);
        var positions3 = index.LookupPositions(999);

        // Assert
        Assert.Equal(2, positions1.Count); // Two rows with id=1
        Assert.Single(positions2); // One row with id=2
        Assert.Empty(positions3); // No rows with id=999
    }

    [Fact]
    public void HashIndex_ContainsKey_ReturnsCorrectly()
    {
        // Arrange
        var index = new HashIndex("products", "category");
        var row1 = new Dictionary<string, object> { { "category", "Electronics" }, { "name", "Laptop" } };

        // Act
        index.Add(row1, 0);

        // Assert
        Assert.True(index.ContainsKey("Electronics"));
        Assert.False(index.ContainsKey("Books"));
        Assert.False(index.ContainsKey(null!));
    }

    [Fact]
    public void HashIndex_Remove_RemovesRow()
    {
        // Arrange
        var index = new HashIndex("users", "id");
        var row1 = new Dictionary<string, object> { { "id", 1 }, { "name", "Alice" } };
        var row2 = new Dictionary<string, object> { { "id", 1 }, { "name", "Alice2" } };

        // Act
        index.Add(row1, 0);
        index.Add(row2, 1);
        var beforeRemove = index.LookupPositions(1);

        index.Remove(row1);
        var afterRemove = index.LookupPositions(1);

        // Assert
        Assert.Equal(2, beforeRemove.Count);
        Assert.Single(afterRemove);
    }

    [Fact]
    public void HashIndex_Clear_RemovesAllData()
    {
        // Arrange
        var index = new HashIndex("users", "id");
        var row1 = new Dictionary<string, object> { { "id", 1 }, { "name", "Alice" } };
        var row2 = new Dictionary<string, object> { { "id", 2 }, { "name", "Bob" } };

        // Act
        index.Add(row1, 0);
        index.Add(row2, 1);
        Assert.Equal(2, index.Count);

        index.Clear();

        // Assert
        Assert.Equal(0, index.Count);
        Assert.Empty(index.LookupPositions(1));
        Assert.Empty(index.LookupPositions(2));
    }

    [Fact]
    public void HashIndex_Rebuild_RebuildsFromRows()
    {
        // Arrange
        var index = new HashIndex("time_entries", "project");
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "project", "Alpha" }, { "duration", 60 } },
            new() { { "project", "Beta" }, { "duration", 90 } },
            new() { { "project", "Alpha" }, { "duration", 30 } }
        };

        // Act
        index.Rebuild(rows);

        // Assert
        Assert.Equal(2, index.Count); // Two unique projects
        Assert.Equal(2, index.LookupPositions("Alpha").Count);
        Assert.Single(index.LookupPositions("Beta"));
    }

    [Fact]
    public void HashIndex_GetStatistics_ReturnsCorrectData()
    {
        // Arrange
        var index = new HashIndex("users", "role");
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "role", "admin" }, { "name", "Alice" } },
            new() { { "role", "admin" }, { "name", "Bob" } },
            new() { { "role", "user" }, { "name", "Charlie" } },
            new() { { "role", "user" }, { "name", "Dave" } },
            new() { { "role", "user" }, { "name", "Eve" } }
        };

        // Act
        foreach (var row in rows)
        {
            index.Add(row, 0); // Dummy position
        }

        var stats = index.GetStatistics();

        // Assert
        Assert.Equal(2, stats.UniqueKeys); // admin and user
        Assert.Equal(5, stats.TotalRows);
        Assert.Equal(2.5, stats.AvgRowsPerKey); // (2 + 3) / 2
    }

    [Fact]
    public void HashIndex_NullKeys_AreIgnored()
    {
        // Arrange
        var index = new HashIndex("users", "email");
        var row1 = new Dictionary<string, object> { { "email", (object)null }, { "name", "Alice" } };
        var row2 = new Dictionary<string, object> { { "name", "Bob" } }; // Missing email key

        // Act
        index.Add(row1, 0);
        index.Add(row2, 1);

        // Assert
        Assert.Equal(0, index.Count); // No keys added
        Assert.Empty(index.LookupPositions(null));
    }

    [Fact]
    public void HashIndex_LargeDataSet_PerformsWell()
    {
        // Arrange
        var index = new HashIndex("events", "userId");
        var rowCount = 10000;
        var uniqueUsers = 100;

        // Act - Add 10k rows with 100 unique user IDs
        for (int i = 0; i < rowCount; i++)
        {
            var row = new Dictionary<string, object>
            {
                { "userId", i % uniqueUsers },
                { "eventId", i },
                { "timestamp", DateTime.UtcNow }
            };
            index.Add(row, i);
        }

        var lookupResults = index.LookupPositions(42); // Look up a specific user

        // Assert
        Assert.Equal(uniqueUsers, index.Count); // 100 unique users
        Assert.Equal(rowCount / uniqueUsers, lookupResults.Count); // Each user has 100 events

        var stats = index.GetStatistics();
        Assert.Equal(uniqueUsers, stats.UniqueKeys);
        Assert.Equal(rowCount, stats.TotalRows);
    }

    [Fact]
    public void HashIndex_DifferentDataTypes_WorksCorrectly()
    {
        // Arrange
        var index = new HashIndex("products", "price");

        // Act & Assert - Integer keys
        var row1 = new Dictionary<string, object> { { "price", 100 }, { "name", "Item1" } };
        index.Add(row1, 0);
        Assert.Single(index.LookupPositions(100));

        // String keys
        index.Clear();
        var row2 = new Dictionary<string, object> { { "price", "expensive" }, { "name", "Item2" } };
        index.Add(row2, 0);
        Assert.Single(index.LookupPositions("expensive"));

        // DateTime keys
        index.Clear();
        var date = DateTime.Parse("2024-01-01");
        var row3 = new Dictionary<string, object> { { "price", date }, { "name", "Item3" } };
        index.Add(row3, 0);
        Assert.Single(index.LookupPositions(date));
    }
}
