namespace SharpCoreDB.Tests.DataStructures;

using System.Text;
using SharpCoreDB.DataStructures;

public sealed class UnsafeEqualityIndexTests
{
    [Fact]
    public void AddLookupRemoveClear_WithSameKey_ShouldBehaveCorrectly()
    {
        using var index = new UnsafeEqualityIndex();
        var key = Encoding.UTF8.GetBytes("alpha");

        index.Add(key, 10);
        index.Add(key, 20);

        Span<long> values = stackalloc long[8];
        var count = index.GetRowIdsForValue(key, values);

        Assert.Equal(2, count);
        Assert.Contains(10, values[..count].ToArray());
        Assert.Contains(20, values[..count].ToArray());

        var removed = index.Remove(key, 10);
        Assert.True(removed);

        count = index.GetRowIdsForValue(key, values);
        Assert.Equal(1, count);
        Assert.Equal(20, values[0]);

        index.Clear();
        count = index.GetRowIdsForValue(key, values);
        Assert.Equal(0, count);
    }
}
