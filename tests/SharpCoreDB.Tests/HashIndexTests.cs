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

        index.Remove(row1, 0);
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
        var row1 = new Dictionary<string, object> { { "email", (object?)null! }, { "name", "Alice" } };
        var row2 = new Dictionary<string, object> { { "name", "Bob" } }; // Missing email key

        // Act
        index.Add(row1, 0);
        index.Add(row2, 1);

        // Assert
        Assert.Equal(0, index.Count); // No keys added
        Assert.Empty(index.LookupPositions(null!));
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

    [Fact]
    public void AddBatchKeys_EqualSizedBatches_DoNotRebuildTheKeyMapPerBatch()
    {
        // §5.1 (2026-10-10, session 80): `AddBatchKeys` used to greet every batch with a capacity hint of
        // `Count + keys.Length`. Because that hint is computed from the *growing* count and re-runs on each call,
        // it makes the map rebuild its entry/bucket pair on every batch — ten 10.000-key batches meant ten
        // grow-and-rehash passes, ~12,6 MB of garbage per index per 100.000 rows, and O(total keys) of copying
        // per call, i.e. quadratic across a batched INSERT loop. The hint is gone, so `Dictionary`'s own
        // amortised doubling applies and a batch that still fits the capacity an earlier growth created must not
        // reallocate at all.
        //
        // Measured on the 100.000-row INSERT arm (`artifacts/s80-nohint-{1,2}.txt` against the pre-change
        // `artifacts/s80-hint-before-{1,2}.txt`): `hash-index` 3.530.483 B per call instead of 4.611.244,
        // 1.330 B/row instead of 1.438, and 191-194 K ops/s instead of 181-188 K in three interleaved pairs.
        //
        // After four 2.000-key batches, the growth that carried the third batch (~10.949 entries) already covers
        // the fourth, so the fourth allocates its keys and nothing else; with the hint every one of the four paid
        // a full grow-and-rehash, which made the last two calls allocate about the same amount (measured
        // pre-change: 372.416 B for the third against 411.784 B for the fourth — see
        // `artifacts/s80-hashindex-hint-red.txt`).
        using var index = new HashIndex("t", "k", CollationType.Binary);
        const int batchSize = 2000;
        var allocations = new long[4];

        for (int batch = 0; batch < 4; batch++)
        {
            var keys = new object?[batchSize];
            var positions = new long[batchSize];
            int start = batch * batchSize;
            for (int i = 0; i < batchSize; i++)
            {
                keys[i] = $"k{start + i}"; // distinct keys — the INSERT arm's shape
                positions[i] = start + i;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            index.AddBatchKeys(keys, positions);
            allocations[batch] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(4 * batchSize, index.Count);
        Assert.True(
            allocations[3] < allocations[2] * 3 / 4,
            $"the fourth batch should fit the headroom an earlier growth created, but it allocated "
            + $"{allocations[3]} B against the third batch's {allocations[2]} B (all four: "
            + $"{string.Join(", ", allocations)} B)");
    }

    [Fact]
    public void AddBatchKeys_RepeatedBatches_KeepEveryKeyQueryable()
    {
        // Behaviour pin for the capacity-policy change above: what the index answers must not move.
        using var index = new HashIndex("t", "k", CollationType.Binary);
        var keys = new object?[500];
        var positions = new long[500];
        for (int i = 0; i < 500; i++)
        {
            keys[i] = $"k{i}";
            positions[i] = i;
        }

        for (int batch = 0; batch < 6; batch++)
        {
            for (int i = 0; i < 500; i++)
            {
                keys[i] = $"b{batch}-k{i}";
                positions[i] = (batch * 500) + i;
            }

            index.AddBatchKeys(keys, positions);
        }

        Assert.Equal(3000, index.Count);
        Assert.Equal(new long[] { 1234L }, index.LookupPositions("b2-k234"));
        Assert.Equal(new long[] { 2999L }, index.LookupPositions("b5-k499"));
        Assert.Empty(index.LookupPositions("b6-k0"));
    }
}
