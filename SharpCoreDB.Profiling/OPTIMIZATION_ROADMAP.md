# SharpCoreDB Insert Pipeline Optimization Roadmap

## ? Executive Summary

Based on code analysis of SharpCoreDB's insert pipeline, we've identified **5 critical hotspots** that cause insert performance to be **32x slower than SQLite**. This document provides a complete profiling and optimization strategy to achieve **20-30% of SQLite performance** (target: 200-300ms for 10K inserts).

---

## ? Current Performance Baseline

### Measured Performance (10K Inserts)

| Mode | Time | Throughput | vs SQLite | Status |
|------|------|------------|-----------|--------|
| **PAGE_BASED** | 2776ms | 3.6 rec/ms | **32x slower** | ? Needs optimization |
| **COLUMNAR** | 1500ms | 6.7 rec/ms | **18x slower** | ? Needs optimization |
| **SQLite** | 42ms | 238 rec/ms | Baseline | ? Target |

### Root Cause Analysis

1. ? **Transaction buffering works** - Storage.Append.cs correctly buffers writes
2. ? **10K individual AppendBytes calls** - Should use AppendBytesMultiple
3. ? **Immediate page flushes** - PageBasedEngine.Insert() flushes after EVERY insert
4. ? **Excessive WAL syncs** - 10K FileStream.Flush calls per batch
5. ? **Small page cache** - 100 pages = only 800KB cache

---

## ? Profiling Strategy

### Tools Required

```powershell
# Install dotnet-trace (once)
dotnet tool install --global dotnet-trace

# Build profiling project
cd SharpCoreDB.Profiling
dotnet build -c Release
```

### Quick Start Commands

```powershell
# 1. Run profiling menu
.\ProfileInserts.ps1

# 2. Select scenario (recommend #1: CPU Sampling first)

# 3. Follow on-screen instructions:
#    - App will show Process ID
#    - Press ENTER in app to start profiling
#    - dotnet-trace will collect data
#    - Press Ctrl+C to stop collection

# 4. Analyze results
.\AnalyzeTrace.ps1 -TracePath ".\traces\cpu_sampling_<timestamp>.nettrace"
```

### Recommended Profiling Sequence

1. **Day 1: Baseline Profiling**
   - Run CPU Sampling (scenario #1)
   - Run Allocation Tracking (scenario #2)
   - Document current hotspots

2. **Day 2: Apply Fix #1 (AppendBytesMultiple)**
   - Modify Table.InsertBatch to use AppendBytesMultiple
   - Re-run CPU Sampling
   - Measure improvement (expect 5-10x)

3. **Day 3: Apply Fix #2 (Page Flush)**
   - Remove FlushDirtyPages() from PageBasedEngine.Insert()
   - Re-run CPU Sampling
   - Measure improvement (expect additional 3-5x)

4. **Day 4: Apply Fix #3 (WAL Batching)**
   - Enable GroupCommitWAL in config
   - Re-run WAL Sync Analysis (scenario #5)
   - Measure improvement (expect additional 2-3x)

5. **Day 5: Apply Fix #4 (Page Cache)**
   - Increase PageCacheCapacity to 10000
   - Re-run Cache Analysis (scenario #4)
   - Measure improvement (expect 10x on hot data)

6. **Day 6: Comparative Validation**
   - Run Comparative Analysis (scenario #6)
   - Verify PAGE_BASED vs COLUMNAR performance
   - Document final results

---

## ? Critical Fixes (Priority Order)

### ?? Priority 1: AppendBytesMultiple Batching

**Impact:** 5-10x improvement  
**Difficulty:** Easy  
**Time:** 30 minutes

**File:** `DataStructures\Table.CRUD.cs`, line ~200

**Current Code:**
```csharp
public long[] InsertBatch(List<Dictionary<string, object>> rows)
{
    var positions = new long[rows.Count];
    for (int i = 0; i < rows.Count; i++)
    {
        var serialized = SerializeRow(rows[i]);
        positions[i] = storage.AppendBytes(path, serialized);  // ? 10K calls!
    }
    return positions;
}
```

**Fixed Code:**
```csharp
public long[] InsertBatch(List<Dictionary<string, object>> rows)
{
    // Serialize all rows first
    var serializedRows = new List<byte[]>(rows.Count);
    foreach (var row in rows)
    {
        serializedRows.Add(SerializeRow(row));
    }
    
    // ? Single batch call!
    var positions = storage.AppendBytesMultiple(path, serializedRows);
    return positions;
}
```

**Validation:**
```powershell
# Before fix
dotnet run --project SharpCoreDB.Profiling
# Select option 1, note AppendBytes count

# After fix
dotnet run --project SharpCoreDB.Profiling
# Select option 1, verify AppendBytesMultiple is called once
```

**Expected Results:**
- Before: 2776ms with 10K AppendBytes calls
- After: 500-700ms with 1 AppendBytesMultiple call
- **Improvement: 4-5x faster**

---

### ?? Priority 2: Remove Immediate Page Flush

**Impact:** 3-5x improvement  
**Difficulty:** Medium  
**Time:** 1 hour

**File:** `Storage\Engines\PageBasedEngine.cs`, line ~60

**Current Code:**
```csharp
public long Insert(string tableName, byte[] data)
{
    var pageId = manager.FindPageWithSpace(tableId, data.Length + 16);
    var recordId = manager.InsertRecord(pageId, data);
    
    // ? KILLS PERFORMANCE!
    manager.FlushDirtyPages();  
    
    return EncodeStorageReference(pageId.Value, recordId.SlotIndex);
}
```

**Fixed Code:**
```csharp
public long Insert(string tableName, byte[] data)
{
    var pageId = manager.FindPageWithSpace(tableId, data.Length + 16);
    var recordId = manager.InsertRecord(pageId, data);
    
    // ? NO IMMEDIATE FLUSH - pages buffered in memory
    // Flush happens on CommitAsync() only
    
    return EncodeStorageReference(pageId.Value, recordId.SlotIndex);
}

public async Task CommitAsync()
{
    lock (transactionLock)
    {
        // ? Single flush for entire transaction
        foreach (var manager in tableManagers.Values)
        {
            manager.FlushDirtyPages();
        }
        
        isInTransaction = false;
    }
    await Task.CompletedTask;
}
```

**Configuration:**
```csharp
var config = new DatabaseConfig
{
    EnablePageCache = true,          // ? Cache dirty pages
    PageCacheCapacity = 10000,       // ? 80MB cache
    UseGroupCommitWal = true,        // ? Batch WAL writes
    GroupCommitSize = 1000           // ? Flush every 1000 rows
};
```

**Validation:**
```powershell
# Profile WAL sync behavior
.\ProfileInserts.ps1
# Select option 5: WAL Sync Analysis

# Before fix: ~10K FlushDirtyPages calls
# After fix: ~10 FlushDirtyPages calls (one per batch)
```

**Expected Results:**
- Before: 500-700ms with 10K flushes
- After: 150-200ms with 10 flushes
- **Improvement: 3-4x faster**

---

### ?? Priority 3: Enable WAL Batching

**Impact:** 2-3x improvement  
**Difficulty:** Easy  
**Time:** 15 minutes

**File:** `DatabaseConfig` instantiation

**Current Config:**
```csharp
var config = new DatabaseConfig
{
    UseGroupCommitWal = false,  // ? Individual WAL writes
    GroupCommitSize = 100       // ? Too small
};
```

**Optimized Config:**
```csharp
var config = new DatabaseConfig
{
    UseGroupCommitWal = true,           // ? Batch WAL writes
    GroupCommitSize = 1000,             // ? Flush every 1000 rows
    WalBufferSize = 8 * 1024 * 1024,    // ? 8MB buffer
    EnableAdaptiveWalBatching = true    // ? Auto-tune batch size
};
```

**Validation:**
```powershell
# Check WAL sync count
.\ProfileInserts.ps1
# Select option 5: WAL Sync Analysis

# Before: ~10K FileStream.Flush calls
# After: ~10 FileStream.Flush calls
```

**Expected Results:**
- Before: 10K WAL syncs = 800ms
- After: 10 WAL syncs = 280ms
- **Improvement: 3x faster**

---

### ?? Priority 4: Increase Page Cache Capacity

**Impact:** 10x improvement on hot data  
**Difficulty:** Trivial  
**Time:** 5 minutes

**File:** `DatabaseConfig` instantiation

**Current Config:**
```csharp
var config = new DatabaseConfig
{
    EnablePageCache = true,
    PageCacheCapacity = 100  // ? Only 800KB cache!
};
```

**Optimized Config:**
```csharp
var config = new DatabaseConfig
{
    EnablePageCache = true,
    PageCacheCapacity = 10000,  // ? 80MB cache (8KB pages)
    PageSize = 8192
};
```

**Cache Size Calculation:**
```
10,000 pages × 8KB = 80MB cache
Enough for ~500K rows (assuming 160 bytes/row, 50 rows/page)
```

**Validation:**
```powershell
# Analyze cache behavior
.\ProfileInserts.ps1
# Select option 4: Page Cache Analysis

# Check cache hit rate:
# - Before: 20% hit rate (80% misses)
# - After: 95% hit rate (5% misses)
```

**Expected Results:**
- Cold data (first query): 500ms
- Hot data (cached): 50ms
- **Improvement: 10x faster on repeated queries**

---

### ?? Priority 5: Optimize Free List Search

**Impact:** 100x improvement on page allocation  
**Difficulty:** Hard  
**Time:** 4 hours

**File:** `Storage\Hybrid\PageManager.cs`, line ~150

**Current Code (O(n) search):**
```csharp
public PageId FindPageWithSpace(uint tableId, int requiredSpace)
{
    // ? Linear search through ALL pages!
    foreach (var page in pages)
    {
        if (page.TableId == tableId && page.FreeSpace >= requiredSpace)
            return page.Id;
    }
    
    // Allocate new page if none found
    return AllocateNewPage(tableId);
}
```

**Optimized Code (O(1) bitmap):**
```csharp
// Add field to PageManager
private Dictionary<uint, BitArray> freePageBitmaps = new();

public PageId FindPageWithSpace(uint tableId, int requiredSpace)
{
    // ? O(1) bitmap lookup!
    if (!freePageBitmaps.TryGetValue(tableId, out var bitmap))
    {
        bitmap = new BitArray(maxPages);
        freePageBitmaps[tableId] = bitmap;
    }
    
    // Find first page with free space
    int pageIndex = bitmap.FindFirstSet();
    
    if (pageIndex >= 0)
    {
        var page = pages[pageIndex];
        if (page.FreeSpace >= requiredSpace)
            return page.Id;
    }
    
    // Allocate new page if none suitable
    return AllocateNewPage(tableId);
}

// Update bitmap on page modification
private void UpdateFreeSpaceBitmap(uint tableId, int pageIndex, int freeSpace)
{
    var bitmap = freePageBitmaps[tableId];
    bitmap[pageIndex] = freeSpace > MIN_FREE_SPACE_THRESHOLD;
}
```

**Validation:**
```powershell
# Profile page allocation overhead
.\ProfileInserts.ps1
# Select option 1: CPU Sampling

# Look for FindPageWithSpace in flamegraph:
# - Before: 12% CPU time (slow linear search)
# - After: <1% CPU time (fast bitmap lookup)
```

**Expected Results:**
- Before: O(n) search = 50ms for 10K pages
- After: O(1) lookup = 0.5ms
- **Improvement: 100x faster**

---

## ? Performance Targets

### Final Goals (After All Fixes)

| Metric | Current | Target | Improvement |
|--------|---------|--------|-------------|
| **10K INSERT** | 2776ms | 200-300ms | **10x faster** |
| **Throughput** | 3.6 rec/ms | 33-50 rec/ms | **10x higher** |
| **vs SQLite** | 32x slower | 5-7x slower | **Within 20-30%** |
| **Memory** | 68.3 MB | 15-20 MB | **4x less** |

### Incremental Milestones

**Milestone 1: AppendBytesMultiple (Week 1)**
- Target: 500-700ms
- Improvement: 4-5x
- Status: ? In Progress

**Milestone 2: Remove Page Flush (Week 2)**
- Target: 150-200ms
- Improvement: 3-4x additional
- Status: ? Blocked by Milestone 1

**Milestone 3: WAL Batching + Cache (Week 3)**
- Target: 200-300ms
- Improvement: 2x additional
- Status: ? Blocked by Milestone 2

**Milestone 4: Free List Optimization (Week 4)**
- Target: <200ms
- Improvement: Eliminates CPU bottleneck
- Status: ? Blocked by Milestone 3

---

## ? Testing & Validation

### Automated Benchmark Suite

```powershell
# Run all benchmarks
cd SharpCoreDB.Benchmarks
dotnet run -c Release

# Compare results
Get-Content BenchmarkDotNet.Artifacts\results\*ComparisonBenchmark*.csv | 
    Select-String "PageBased_Insert|AppendOnly_Insert"
```

### Manual Profiling Checklist

- [ ] Baseline CPU profile captured
- [ ] Baseline allocation profile captured
- [ ] Fix #1 applied and re-profiled
- [ ] Fix #2 applied and re-profiled
- [ ] Fix #3 applied and re-profiled
- [ ] Fix #4 applied and re-profiled
- [ ] Fix #5 applied and re-profiled
- [ ] Final comparative analysis completed
- [ ] Performance regression tests passing

### Success Criteria

? **Must Have:**
- 10K inserts complete in <300ms (PAGE_BASED mode)
- Memory usage <20MB for 10K records
- Zero Gen2 GC collections during insert batch
- Cache hit rate >90% on hot data

? **Nice to Have:**
- Match SQLite on OLTP workloads (updates, random access)
- Beat SQLite on OLAP workloads (aggregates, scans with SIMD)
- Throughput >30 records/ms

---

## ? Monitoring & Observability

### Key Metrics to Track

```csharp
public class InsertPipelineMetrics
{
    public long TotalInserts { get; set; }
    public long AvgInsertTimeMicros { get; set; }
    public long AppendBytesCalls { get; set; }
    public long PageFlushCount { get; set; }
    public long WalSyncCount { get; set; }
    public double CacheHitRate { get; set; }
    public long Gen2GcCount { get; set; }
}
```

### Dashboarding (Future Work)

Consider integrating Application Insights or Prometheus:

```csharp
// Example: Export metrics to Prometheus
[PrometheusMetrics]
public class SharpCoreDBMetrics
{
    private static readonly Counter InsertCounter = Metrics.CreateCounter(
        "sharpcoredb_inserts_total", 
        "Total number of INSERT operations");
    
    private static readonly Histogram InsertDuration = Metrics.CreateHistogram(
        "sharpcoredb_insert_duration_ms",
        "INSERT operation duration in milliseconds");
}
```

---

## ? Rollout Plan

### Phase 1: Development (Weeks 1-4)

- Week 1: Apply Fix #1 (AppendBytesMultiple)
- Week 2: Apply Fix #2 (Page Flush)
- Week 3: Apply Fixes #3 & #4 (WAL + Cache)
- Week 4: Apply Fix #5 (Free List)

### Phase 2: Testing (Week 5)

- Unit tests for all modified code
- Integration tests for insert pipeline
- Performance regression suite
- Load testing with 100K-1M records

### Phase 3: Documentation (Week 6)

- Update architecture docs
- Add performance tuning guide
- Create troubleshooting runbook
- Write blog post on optimizations

### Phase 4: Release (Week 7)

- Merge to main branch
- Tag release (v2.0.0-alpha)
- Announce on GitHub
- Gather community feedback

---

## ? Risk Mitigation

### Potential Issues

**Issue 1: Transaction Semantics Change**

- **Risk:** Removing immediate flushes may break ACID guarantees
- **Mitigation:** Ensure CommitAsync() is called after every batch
- **Testing:** Add transaction rollback tests

**Issue 2: Memory Pressure from Large Cache**

- **Risk:** 80MB cache may cause OOM on low-memory systems
- **Mitigation:** Make PageCacheCapacity configurable, auto-tune based on available memory
- **Testing:** Test on 512MB, 1GB, 2GB RAM machines

**Issue 3: Regression on Small Batches**

- **Risk:** Optimizations may hurt performance for <100 row batches
- **Mitigation:** Add fast path for small batches, keep immediate flush for single inserts
- **Testing:** Benchmark 1, 10, 100, 1000, 10000 record batches

---

## ? Contact & Support

**Project Lead:** MPCoreDeveloper  
**GitHub:** https://github.com/MPCoreDeveloper/SharpCoreDB  
**Discord:** (TBD)  

**Profiling Questions?**  
Open an issue with the `performance` label and attach your trace files.

**Contributing?**  
See CONTRIBUTING.md for guidelines. PRs welcome!

---

**Last Updated:** 2025-01-16  
**Version:** 1.0  
**Status:** ?? In Progress
