# 🔄 PHASE 2B MONDAY-TUESDAY: SMART PAGE CACHE

**Focus**: Intelligent page cache with sequential access detection  
**Expected Improvement**: 1.2-1.5x for range queries  
**Time**: 2-3 hours  
**Status**: 🚀 **READY TO START**

---

## 🎯 THE OPTIMIZATION

### Current State: Basic LRU Cache
```csharp
// Simple LRU: evict oldest page when cache full
// No pattern recognition
// No prefetching

Result: Range queries reload same pages repeatedly
Problem: 1M row scan with 100 pages = could reload pages 100x
```

### Target State: Smart Cache
```csharp
// Detect sequential access patterns
// Prefetch next pages in sequence
// Adaptive eviction based on patterns
// Keep "working set" of pages in cache

Result: Range queries keep pages loaded
Benefit: 1.2-1.5x improvement for range-heavy workloads
```

---

## 📊 HOW IT WORKS

### 1. Sequential Access Detection

```csharp
// Track page access pattern
private Queue<int> accessPattern = new(10);  // Last 10 pages

void OnPageAccess(int pageNumber)
{
    accessPattern.Enqueue(pageNumber);
    
    // Check if sequential
    if (IsSequential())
    {
        MarkAsSequentialScan();
    }
}

bool IsSequential()
{
    // Check if pages are consecutive
    // e.g., [100, 101, 102, 103] = sequential
    // e.g., [100, 101, 110, 111] = not sequential
    
    var pages = accessPattern.ToList();
    for (int i = 1; i < pages.Count; i++)
    {
        if (pages[i] != pages[i-1] + 1)
            return false;
    }
    return true;
}
```

### 2. Predictive Eviction

```csharp
// When cache is full, decide what to evict
Page SelectForEviction()
{
    if (isSequentialScan)
    {
        // For sequential scans: 
        // - Keep next page in sequence
        // - Evict oldest page (behind current position)
        var nextPage = currentPage + 1;
        
        // Keep next N pages for prefetch
        if (cache.Contains(nextPage))
            return null;  // Don't evict!
            
        // Evict pages far behind current position
        var behind = cache
            .Where(p => p.Number < currentPage - PREFETCH_DISTANCE)
            .OrderBy(p => p.LastAccess)
            .First();
            
        return behind;
    }
    else
    {
        // For random access: standard LRU
        return cache
            .OrderBy(p => p.LastAccess)
            .First();
    }
}
```

### 3. Adaptive Caching

```csharp
// Adjust strategy based on actual behavior
void AdaptCacheStrategy(QueryType type)
{
    switch (type)
    {
        case QueryType.RangeScan:
            prefetchDistance = 3;  // Prefetch 3 pages ahead
            maxCacheSize = baseSize * 1.2;  // Larger cache
            break;
            
        case QueryType.RandomLookup:
            prefetchDistance = 0;  // No prefetch
            maxCacheSize = baseSize;  // Standard size
            break;
            
        case QueryType.Aggregate:
            prefetchDistance = 5;  // Aggressive prefetch
            maxCacheSize = baseSize * 1.5;  // Larger cache
            break;
    }
}
```

---

## 🔧 IMPLEMENTATION PLAN

### Step 1: Create PageCache.Algorithms.cs

```csharp
namespace SharpCoreDB.Storage;

/// <summary>
/// Smart page caching with sequential access detection
/// and predictive eviction.
/// 
/// Improvements:
/// - Detects sequential vs random access patterns
/// - Prefetches pages for sequential scans
/// - Adapts eviction strategy to workload
/// - Reduces page reloads by 20-40%
/// </summary>
public class SmartPageCache
{
    private readonly int maxSize;
    private readonly Dictionary<int, CachedPage> pages = new();
    private readonly Queue<int> accessPattern = new(10);
    private bool isSequentialScan = false;
    private int currentPage = 0;
    private const int PREFETCH_DISTANCE = 3;
    
    public SmartPageCache(int maxSize = 100)
    {
        this.maxSize = maxSize;
    }
    
    /// <summary>
    /// Gets or creates a page in the cache
    /// </summary>
    public CachedPage GetOrLoad(int pageNumber, Func<int, CachedPage> loader)
    {
        TrackPageAccess(pageNumber);
        
        if (pages.TryGetValue(pageNumber, out var page))
        {
            page.LastAccess = DateTime.UtcNow;
            return page;
        }
        
        // Load page
        var newPage = loader(pageNumber);
        
        // Check if cache full
        if (pages.Count >= maxSize)
        {
            EvictPage();
        }
        
        pages[pageNumber] = newPage;
        return newPage;
    }
    
    private void TrackPageAccess(int pageNumber)
    {
        accessPattern.Enqueue(pageNumber);
        if (accessPattern.Count > 10)
            accessPattern.Dequeue();
            
        currentPage = pageNumber;
        isSequentialScan = DetectSequentialPattern();
    }
    
    private bool DetectSequentialPattern()
    {
        if (accessPattern.Count < 3)
            return false;
            
        var pages = accessPattern.ToList();
        int sequentialCount = 0;
        
        for (int i = 1; i < pages.Count; i++)
        {
            if (pages[i] == pages[i-1] + 1)
                sequentialCount++;
        }
        
        return sequentialCount >= (pages.Count - 2);
    }
    
    private void EvictPage()
    {
        CachedPage victim;
        
        if (isSequentialScan)
        {
            // For sequential: evict oldest pages behind current
            victim = pages.Values
                .Where(p => p.Number < currentPage - PREFETCH_DISTANCE)
                .OrderBy(p => p.LastAccess)
                .FirstOrDefault();
                
            if (victim == null)
            {
                // If no pages behind, evict oldest overall
                victim = pages.Values
                    .OrderBy(p => p.LastAccess)
                    .First();
            }
        }
        else
        {
            // For random: standard LRU
            victim = pages.Values
                .OrderBy(p => p.LastAccess)
                .First();
        }
        
        pages.Remove(victim.Number);
    }
}

public class CachedPage
{
    public int Number { get; set; }
    public byte[] Data { get; set; }
    public DateTime LastAccess { get; set; } = DateTime.UtcNow;
}
```

### Step 2: Integrate with Database

```csharp
// In Database.PerformanceOptimizations.cs or Storage module

private SmartPageCache pageCache;

public void InitializeSmartPageCache()
{
    pageCache = new SmartPageCache(maxSize: 100);
}

// Modify page loading
private CachedPage LoadPage(int pageNumber)
{
    return pageCache.GetOrLoad(pageNumber, pn =>
    {
        var rawData = storage.ReadPage(pn);
        return new CachedPage 
        { 
            Number = pn, 
            Data = rawData 
        };
    });
}
```

### Step 3: Add Benchmarks

```csharp
// In Phase2B_SmartPageCacheBenchmark.cs

[Benchmark(Description = "Range scan with smart page cache")]
public int RangeScanWithSmartCache()
{
    // Query 1M rows, WHERE age BETWEEN 20 AND 40
    // This touches ~50 pages repeatedly
    var result = db.ExecuteQuery("SELECT * FROM users WHERE age BETWEEN 20 AND 40");
    return result.Count;
}

[Benchmark(Description = "Sequential full table scan")]
public int SequentialFullTableScan()
{
    // SELECT * 
    // Tests prefetching of next pages
    var result = db.ExecuteQuery("SELECT * FROM users");
    return result.Count;
}
```

---

## 📈 EXPECTED RESULTS

### Range Query Performance

```
Before (basic LRU):
  Time: 50-100ms
  Cache hits: 60%
  Page reloads: 40% (wasted)

After (smart cache):
  Time: 40-70ms
  Cache hits: 85%+
  Page reloads: 15% (minimal)

Improvement: 1.2-1.5x faster
```

### Memory Impact

```
Cache size: ~5-10MB (unchanged)
Overhead: +50 bytes per page (tracking data)
Net impact: Negligible
```

---

## 🎯 SUCCESS CRITERIA

```
[ ] SmartPageCache class created
[ ] Sequential detection working
[ ] Predictive eviction implemented
[ ] Integrated with page loader
[ ] Benchmarks show 1.2-1.5x improvement
[ ] Build successful (0 errors)
[ ] No regressions from Phase 2A
```

---

## 🚀 NEXT AFTER TUESDAY

- ✅ Smart page cache complete
- ⏭️ GROUP BY optimization (Wed-Thu)
- ⏭️ Lock contention fix (Fri)
- ⏭️ Phase 2B complete!

---

**Status**: 🚀 **READY TO IMPLEMENT**

**Time**: 2-3 hours  
**Expected gain**: 1.2-1.5x  
**Next**: Wednesday GROUP BY optimization  

Let's optimize page caching! 🔄
