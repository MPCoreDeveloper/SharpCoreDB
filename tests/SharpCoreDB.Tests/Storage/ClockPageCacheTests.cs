namespace SharpCoreDB.Tests.Storage;

using SharpCoreDB.Storage.Hybrid;

public sealed class ClockPageCacheTests
{
    [Fact]
    public void PutAndGet_WithCachedPage_ShouldReturnPage()
    {
        var cache = new ClockPageCache(maxCapacity: 8);
        var page = CreatePage(pageId: 1, isDirty: false);

        cache.Put(1, page);
        var cached = cache.Get(1);

        Assert.True(cached.HasValue);
        Assert.Equal(1UL, cached.Value.PageId);
    }

    [Fact]
    public void FlushOldestDirtyPages_WithDirtyPages_ShouldFlushAndClearDirtyFlags()
    {
        var cache = new ClockPageCache(maxCapacity: 8);
        cache.Put(1, CreatePage(pageId: 1, isDirty: true));
        cache.Put(2, CreatePage(pageId: 2, isDirty: true));
        cache.Put(3, CreatePage(pageId: 3, isDirty: false));

        var flushedIds = new List<ulong>();
        var flushed = cache.FlushOldestDirtyPages(p => flushedIds.Add(p.PageId), maxToFlush: 10);

        Assert.Equal(2, flushed);
        Assert.Contains(1UL, flushedIds);
        Assert.Contains(2UL, flushedIds);

        var dirtyAfter = cache.GetDirtyPages().ToList();
        Assert.Empty(dirtyAfter);
    }

    private static PageManager.Page CreatePage(ulong pageId, bool isDirty)
    {
        return new PageManager.Page
        {
            TableId = 1,
            Type = PageManager.PageType.Table,
            RecordCount = 0,
            FreeSpace = 1024,
            PageId = pageId,
            IsDirty = isDirty,
            Data = new Memory<byte>(new byte[32]),
            Slots = []
        };
    }
}
