// <copyright file="PageManager_FreeList_O1_Test.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Diagnostics;
using SharpCoreDB.Storage.Hybrid;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for O(1) free list performance in PageManager.
/// Validates that page allocation doesn't degrade with high page counts.
/// 
/// Expected Performance:
/// - 10K allocations: <100ms total (~10?s per allocation)
/// - 10K frees: <50ms total (~5?s per free)
/// - 10K re-allocations: <100ms total (proving O(1) reuse)
/// - No O(n) slowdown as page count increases
/// </summary>
public class PageManager_FreeList_O1_Test
{
    private readonly string testDir;

    public PageManager_FreeList_O1_Test()
    {
        testDir = Path.Combine(Path.GetTempPath(), $"pm_o1_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
    }

    [Fact]
    public void AllocatePage_10K_Pages_Should_Be_O1_Not_On()
    {
        // Arrange
        using var pm = new PageManager(testDir, tableId: 1);
        var allocatedPages = new List<PageManager.PageId>();
        
        // Act: Allocate 10K pages and measure each batch
        var batchSize = 1000;
        var batchTimes = new List<long>();
        
        for (int batch = 0; batch < 10; batch++)
        {
            var sw = Stopwatch.StartNew();
            
            for (int i = 0; i < batchSize; i++)
            {
                var pageId = pm.AllocatePage(tableId: 1, PageManager.PageType.Table);
                allocatedPages.Add(pageId);
            }
            
            sw.Stop();
            batchTimes.Add(sw.ElapsedMilliseconds);
        }
        
        // Assert: Each batch should take similar time (O(1) per allocation)
        // Allow 2x variance (batch 10 should be at most 2x batch 1)
        var firstBatchTime = batchTimes[0];
        var lastBatchTime = batchTimes[9];
        
        var slowdownRatio = (double)lastBatchTime / Math.Max(firstBatchTime, 1);
        
        // O(1) means ratio should be close to 1.0 (constant time)
        // O(n) would show ratio of ~10 (10x slower for 10x more pages)
        Assert.True(slowdownRatio < 2.0, 
            $"? PERFORMANCE DEGRADATION: Batch 10 ({lastBatchTime}ms) is {slowdownRatio:F2}x slower than Batch 1 ({firstBatchTime}ms). Expected <2x for O(1) allocation.");
        
        // Also verify total time is reasonable (<100ms for 10K allocations)
        var totalTime = batchTimes.Sum();
        Assert.True(totalTime < 100, 
            $"? TOTAL TIME EXCEEDED: {totalTime}ms for 10K allocations (expected <100ms for O(1))");
        
        // SUCCESS
        Console.WriteLine($"? O(1) ALLOCATION VERIFIED:");
        Console.WriteLine($"   Batch 1: {batchTimes[0]}ms");
        Console.WriteLine($"   Batch 5: {batchTimes[4]}ms");
        Console.WriteLine($"   Batch 10: {batchTimes[9]}ms");
        Console.WriteLine($"   Slowdown Ratio: {slowdownRatio:F2}x (expected <2x)");
        Console.WriteLine($"   Total Time: {totalTime}ms (expected <100ms)");
    }

    [Fact]
    public void FreePage_10K_Pages_Should_Be_O1()
    {
        // Arrange: Allocate 10K pages first
        using var pm = new PageManager(testDir, tableId: 1);
        var pages = new List<PageManager.PageId>();
        
        for (int i = 0; i < 10_000; i++)
        {
            pages.Add(pm.AllocatePage(tableId: 1, PageManager.PageType.Table));
        }
        
        // Act: Free all pages in batches
        var batchSize = 1000;
        var batchTimes = new List<long>();
        
        for (int batch = 0; batch < 10; batch++)
        {
            var sw = Stopwatch.StartNew();
            
            for (int i = 0; i < batchSize; i++)
            {
                pm.FreePage(pages[batch * batchSize + i]);
            }
            
            sw.Stop();
            batchTimes.Add(sw.ElapsedMilliseconds);
        }
        
        // Assert: Each batch should take similar time
        var firstBatchTime = batchTimes[0];
        var lastBatchTime = batchTimes[9];
        var slowdownRatio = (double)lastBatchTime / Math.Max(firstBatchTime, 1);
        
        Assert.True(slowdownRatio < 2.0, 
            $"? FREE DEGRADATION: Batch 10 ({lastBatchTime}ms) is {slowdownRatio:F2}x slower than Batch 1 ({firstBatchTime}ms)");
        
        var totalTime = batchTimes.Sum();
        Assert.True(totalTime < 50, 
            $"? FREE TIME EXCEEDED: {totalTime}ms for 10K frees (expected <50ms)");
        
        Console.WriteLine($"? O(1) FREE VERIFIED:");
        Console.WriteLine($"   Total Time: {totalTime}ms for 10K frees");
    }

    [Fact]
    public void ReallocatePage_Should_Reuse_Freed_Pages_O1()
    {
        // Arrange
        using var pm = new PageManager(testDir, tableId: 1);
        
        // Allocate and free 5K pages to populate free list
        var initialPages = new List<PageManager.PageId>();
        for (int i = 0; i < 5_000; i++)
        {
            initialPages.Add(pm.AllocatePage(tableId: 1, PageManager.PageType.Table));
        }
        
        foreach (var page in initialPages)
        {
            pm.FreePage(page);
        }
        
        // Act: Re-allocate 5K pages (should reuse freed pages)
        var sw = Stopwatch.StartNew();
        var reusedPages = new List<PageManager.PageId>();
        
        for (int i = 0; i < 5_000; i++)
        {
            reusedPages.Add(pm.AllocatePage(tableId: 1, PageManager.PageType.Table));
        }
        
        sw.Stop();
        
        // Assert: Should be fast (<50ms) and reuse freed page IDs
        Assert.True(sw.ElapsedMilliseconds < 50, 
            $"? REALLOCATION SLOW: {sw.ElapsedMilliseconds}ms for 5K reallocations (expected <50ms)");
        
        // Verify that reused pages are from the freed set
        var freedPageIds = initialPages.Select(p => p.Value).ToHashSet();
        var reusedPageIds = reusedPages.Select(p => p.Value).ToHashSet();
        var reuseCount = reusedPageIds.Intersect(freedPageIds).Count();
        
        Assert.True(reuseCount == 5_000, 
            $"? NOT REUSING: Only {reuseCount}/5000 pages were reused from free list");
        
        Console.WriteLine($"? O(1) REUSE VERIFIED:");
        Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms for 5K reallocations");
        Console.WriteLine($"   Reused: {reuseCount}/5000 freed pages");
    }

    [Fact]
    public void MixedWorkload_10K_AllocFree_Should_Not_Degrade()
    {
        // Arrange
        using var pm = new PageManager(testDir, tableId: 1);
        var activePagesCount = 0;
        var batchTimes = new List<long>();
        
        // Act: Alternating pattern - allocate 100, free 50, repeat 100 times
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var sw = Stopwatch.StartNew();
            var batch = new List<PageManager.PageId>();
            
            // Allocate 100 pages
            for (int i = 0; i < 100; i++)
            {
                batch.Add(pm.AllocatePage(tableId: 1, PageManager.PageType.Table));
            }
            activePagesCount += 100;
            
            // Free 50 pages (simulating deletion)
            for (int i = 0; i < 50; i++)
            {
                pm.FreePage(batch[i]);
            }
            activePagesCount -= 50;
            
            sw.Stop();
            batchTimes.Add(sw.ElapsedMilliseconds);
        }
        
        // Assert: Performance should remain constant (O(1) operations)
        var firstIterationTime = batchTimes.Take(10).Average();
        var lastIterationTime = batchTimes.Skip(90).Take(10).Average();
        var slowdownRatio = lastIterationTime / firstIterationTime;
        
        Assert.True(slowdownRatio < 1.5, 
            $"? MIXED WORKLOAD DEGRADATION: Last 10 iterations ({lastIterationTime:F2}ms avg) is {slowdownRatio:F2}x slower than first 10 ({firstIterationTime:F2}ms avg)");
        
        Console.WriteLine($"? MIXED WORKLOAD O(1) VERIFIED:");
        Console.WriteLine($"   First 10 iterations: {firstIterationTime:F2}ms avg");
        Console.WriteLine($"   Last 10 iterations: {lastIterationTime:F2}ms avg");
        Console.WriteLine($"   Slowdown Ratio: {slowdownRatio:F2}x (expected <1.5x)");
        Console.WriteLine($"   Final Active Pages: {activePagesCount}");
    }

    [Fact]
    public void HeaderPage_FreeListPointer_Should_Persist()
    {
        // Arrange: Create PM, allocate/free some pages
        var pageId1 = PageManager.PageId.Invalid;
        var pageId2 = PageManager.PageId.Invalid;
        
        using (var pm = new PageManager(testDir, tableId: 1))
        {
            pageId1 = pm.AllocatePage(tableId: 1, PageManager.PageType.Table);
            pageId2 = pm.AllocatePage(tableId: 1, PageManager.PageType.Table);
            
            pm.FreePage(pageId1);
            pm.FreePage(pageId2);
            
            pm.FlushDirtyPages();
        }
        
        // Act: Reopen PM and allocate - should reuse freed pages
        using (var pm = new PageManager(testDir, tableId: 1))
        {
            var reusedPage1 = pm.AllocatePage(tableId: 1, PageManager.PageType.Table);
            var reusedPage2 = pm.AllocatePage(tableId: 1, PageManager.PageType.Table);
            
            // Assert: Should reuse the freed pages (LIFO order)
            Assert.True(reusedPage1.Value == pageId2.Value || reusedPage1.Value == pageId1.Value,
                $"? NOT PERSISTED: Reused page {reusedPage1.Value} doesn't match freed pages {pageId1.Value} or {pageId2.Value}");
            
            Console.WriteLine($"? PERSISTENCE VERIFIED:");
            Console.WriteLine($"   Freed: {pageId1.Value}, {pageId2.Value}");
            Console.WriteLine($"   Reused: {reusedPage1.Value}, {reusedPage2.Value}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
        catch
        {
            // Cleanup best-effort
        }
    }
}
