namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;

public sealed class HashIndexUnsafeBackendTests
{
    [Fact]
    public void HashIndex_WithUnsafeBackend_ShouldAddAndLookupPositions()
    {
        using var index = new HashIndex("users", "id", CollationType.Binary, false, useUnsafeEqualityIndex: true);
        var row1 = new Dictionary<string, object> { ["id"] = 7, ["name"] = "A" };
        var row2 = new Dictionary<string, object> { ["id"] = 7, ["name"] = "B" };

        index.Add(row1, 100);
        index.Add(row2, 200);

        var positions = index.LookupPositions(7);

        Assert.Equal(2, positions.Count);
        Assert.Contains(100, positions);
        Assert.Contains(200, positions);
    }
}
