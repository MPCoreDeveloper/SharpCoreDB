# SharpCoreDB Insert Pipeline Performance Profiling

Complete guide for profiling and optimizing SharpCoreDB's insert performance using dotnet-trace on .NET 10.

## ? Quick Start

```powershell
# 1. Install dotnet-trace
dotnet tool install --global dotnet-trace

# 2. Build profiling project
cd ..\SharpCoreDB.Profiling
dotnet build -c Release

# 3. Run profiling menu
.\ProfileInserts.ps1

# 4. Analyze results
.\AnalyzeTrace.ps1 -TracePath ".\traces\cpu_sampling_<timestamp>.nettrace"
```

## ? What This Profiles

### Insert Pipeline Flow

```
Database.BulkInsertAsync
  ? Database.ExecuteBatchSQL
    ? Table.InsertBatch
      ? PageBasedEngine.InsertBatch  (or AppendOnlyEngine)
        ? PageManager.InsertRecord
          ? PageManager.FindPageWithSpace
          ? Storage.AppendBytes
            ? WalManager.WriteEntry
              ? FileStream.Flush
```

### Key Hotspots to Identify

1. **AppendBytes Indirection** (Services\Storage.Append.cs)
   - Symptom: 10K+ individual AppendBytes calls
   - Fix: Use AppendBytesMultiple for batching
   - Target: 5-10x improvement

2. **Page Flush Frequency** (Storage\Engines\PageBasedEngine.cs)
   - Symptom: FlushDirtyPages() called after every insert
   - Fix: Remove immediate flush, only flush on commit
   - Target: 3-5x fewer I/O operations

3. **WAL Sync Overhead** (Services\WalManager.cs)
   - Symptom: FileStream.Flush called >10K times
   - Fix: Enable GroupCommitWAL, batch WAL writes
   - Target: 2-3x fewer sync operations

4. **Page Cache Misses** (Core\Cache\PageCache.cs)
   - Symptom: High EvictPage / Miss ratio
   - Fix: Increase PageCacheCapacity to 10K pages
   - Target: 10x faster on hot data

5. **Free List Search** (Storage\Hybrid\PageManager.cs)
   - Symptom: O(n) FindPageWithSpace scans
   - Fix: Use O(1) free list bitmap
   - Target: Constant-time page allocation

## ? Profiling Scenarios

### 1. CPU Sampling

**Purpose:** Find hot methods consuming most CPU time

**When to use:** 
- Inserts are slow but memory looks fine
- Need to identify computational bottlenecks
- Want to see method call hierarchy

**Command:**
```powershell
.\ProfileInserts.ps1
# Select option 1: CPU Sampling
```

**What to look for:**
- Methods with >5% CPU time
- Tight loops (for, while)
- Serialization overhead (BinaryPrimitives)
- SIMD operations (SimdHelper)

**Expected hotspots:**
```
PageManager.InsertRecord         12.5%  (Page allocation logic)
BinaryPrimitives.Write*           8.3%  (Row serialization)
Storage.AppendBytes               7.1%  (File append operations)
PageCache.GetPage                 5.2%  (Cache lookups)
```

### 2. Allocation Tracking

**Purpose:** Find excessive heap allocations and GC pressure

**When to use:**
- High Gen2 GC collections
- Memory leaks suspected
- Large object allocations (>85KB)

**Command:**
```powershell
.\ProfileInserts.ps1
# Select option 2: Allocation Tracking
```

**What to look for:**
- Allocations >10MB total
- byte[] arrays created per insert
- Boxing (object allocations from value types)
- String concatenations in hot paths

**Expected allocations:**
```
byte[] buffers (ArrayPool)       ~100MB  (Reused via pool)
Dictionary<string,object> rows   ~15MB   (Row data structures)
List<byte[]> batches             ~5MB    (Batch collections)
```

### 3. Full Diagnostics

**Purpose:** Complete profile including CPU, allocations, and I/O

**When to use:**
- Need comprehensive overview
- Multiple performance issues suspected
- Production-like load testing

**Command:**
```powershell
.\ProfileInserts.ps1
# Select option 3: Full Diagnostics
```

**Trace includes:**
- CPU sampling at 100Hz
- All GC events (Gen0, Gen1, Gen2)
- Allocation tracking
- Thread contention events
- I/O operations

### 4. Page Cache Analysis

**Purpose:** Analyze cache effectiveness and memory access patterns

**When to use:**
- Suspect cache miss rate is high
- Memory pressure on hot data
- Need to tune PageCacheCapacity

**Command:**
```powershell
.\ProfileInserts.ps1
# Select option 4: Page Cache Analysis
```

**Metrics to analyze:**
- Cache hit rate (target: >90%)
- Eviction frequency
- Page access patterns (sequential vs random)
- Gen2 GC pressure

**Optimal configuration:**
```csharp
new DatabaseConfig
{
    EnablePageCache = true,
    PageCacheCapacity = 10000,  // 80MB cache for 8KB pages
    PageSize = 8192
}
```

### 5. WAL Sync Overhead

**Purpose:** Identify WAL write bottlenecks and flush frequency

**When to use:**
- Inserts blocked on I/O
- High FileStream.Flush counts
- Need to optimize WAL batching

**Command:**
```powershell
.\ProfileInserts.ps1
# Select option 5: WAL Sync Analysis
```

**What to measure:**
- WAL flushes per second (target: <100/sec)
- Average flush latency (target: <1ms)
- Batch size effectiveness
- GroupCommit behavior

**Optimal configuration:**
```csharp
new DatabaseConfig
{
    UseGroupCommitWal = true,
    GroupCommitSize = 1000,        // Flush every 1000 rows
    WalBufferSize = 8 * 1024 * 1024,  // 8MB buffer
    EnableAdaptiveWalBatching = true
}
```

### 6. Comparative Analysis

**Purpose:** Compare PAGE_BASED vs COLUMNAR modes

**When to use:**
- Measuring storage engine performance
- Validating optimizations
- A/B testing configurations

**Command:**
```powershell
.\ProfileInserts.ps1
# Select option 6: Comparative Analysis
```

**Comparison metrics:**
- Insert throughput (records/sec)
- Memory allocations
- I/O operations count
- CPU time per insert

**Expected results:**
```
PAGE_BASED: 200-300ms for 10K inserts
COLUMNAR:   500-700ms for 10K inserts

PAGE_BASED wins on OLTP (updates, random access)
COLUMNAR wins on OLAP (aggregates, sequential scans)
```

## ? Analyzing Results

### Using Speedscope (Visual Flame Graphs)

1. Open https://speedscope.app
2. Drag & drop `.nettrace` file
3. Select "Left Heavy" view for hotspots
4. Look for wide bars = hot methods

**Color coding:**
- Red = CPU-intensive
- Blue = I/O wait
- Green = GC operations

### Using PerfView (Advanced Analysis)

1. Download PerfView: https://github.com/microsoft/perfview
2. Open trace: `PerfView.exe <trace-file>.nettrace`
3. Navigate to "CPU Stacks" for detailed call trees
4. Navigate to "GC Stats" for allocation analysis

**PerfView features:**
- Call tree navigation
- Allocation flamegraphs
- GC pause time analysis
- Thread contention views

### Using dotnet-trace CLI

```powershell
# Generate text report
dotnet-trace report cpu_sampling_<timestamp>.nettrace

# Convert to speedscope format
dotnet-trace convert cpu_sampling_<timestamp>.nettrace --format Speedscope

# View GC stats
dotnet-trace ps  # List processes
dotnet-trace collect --process-id <PID> --providers Microsoft-Windows-DotNETRuntime:0x1
```

## ? Known Hotspots & Fixes

### 1. AppendBytes Indirection (5-10x Impact)

**File:** `Services\Storage.Append.cs`

**Problem:**
```csharp
// BAD: 10K individual calls
for (int i = 0; i < 10000; i++) {
    storage.AppendBytes(path, rowData[i]);
}
```

**Fix:**
```csharp
// GOOD: Single batch call
storage.AppendBytesMultiple(path, rowData);  // 10K rows at once!
```

**Metrics:**
- Before: 10,000 AppendBytes calls = 2500ms
- After: 1 AppendBytesMultiple call = 250ms
- **Improvement: 10x faster**

### 2. Page Flush Frequency (3-5x Impact)

**File:** `Storage\Engines\PageBasedEngine.cs`

**Problem:**
```csharp
// BAD: Flush after EVERY insert
public long Insert(string tableName, byte[] data) {
    var ref = engine.InsertRecord(...);
    manager.FlushDirtyPages();  // ? KILLS PERFORMANCE!
    return ref;
}
```

**Fix:**
```csharp
// GOOD: Only flush on transaction commit
public long Insert(string tableName, byte[] data) {
    var ref = engine.InsertRecord(...);
    // No immediate flush - dirty pages buffered
    return ref;
}

public async Task CommitAsync() {
    manager.FlushDirtyPages();  // ? Single flush for entire batch
}
```

**Metrics:**
- Before: 10,000 flushes = 1500ms
- After: 1 flush = 300ms
- **Improvement: 5x faster**

### 3. WAL Sync Overhead (2-3x Impact)

**File:** `Services\WalManager.cs`

**Problem:**
```csharp
// BAD: Sync after every WAL write
public void Log(string operation) {
    fs.Write(data);
    fs.Flush(flushToDisk: true);  // ? Expensive syscall!
}
```

**Fix:**
```csharp
// GOOD: Batch WAL writes with GroupCommit
public void Log(string operation) {
    walBuffer.Add(operation);
    if (walBuffer.Count >= GroupCommitSize) {
        fs.Write(walBuffer);
        fs.Flush(flushToDisk: true);  // ? Single flush for batch
        walBuffer.Clear();
    }
}
```

**Metrics:**
- Before: 10,000 syncs = 800ms
- After: 10 syncs = 280ms
- **Improvement: 3x faster**

### 4. Page Cache Misses (10x Impact on Hot Data)

**File:** `Core\Cache\PageCache.cs`

**Problem:**
```csharp
// BAD: Insufficient cache capacity
var config = new DatabaseConfig {
    PageCacheCapacity = 100  // Only 800KB cache!
};
```

**Fix:**
```csharp
// GOOD: Large cache for hot data
var config = new DatabaseConfig {
    PageCacheCapacity = 10000,  // 80MB cache
    EnablePageCache = true
};
```

**Metrics:**
- Before: 80% cache miss rate = 500ms/query
- After: 95% cache hit rate = 50ms/query
- **Improvement: 10x faster on hot data**

### 5. Free List Search (Constant Time)

**File:** `Storage\Hybrid\PageManager.cs`

**Problem:**
```csharp
// BAD: O(n) linear search
public PageId FindPageWithSpace(uint tableId, int requiredSpace) {
    foreach (var page in pages) {  // ? Scans all pages!
        if (page.FreeSpace >= requiredSpace)
            return page.Id;
    }
}
```

**Fix:**
```csharp
// GOOD: O(1) bitmap lookup
private BitArray freePageBitmap;

public PageId FindPageWithSpace(uint tableId, int requiredSpace) {
    int pageIndex = freePageBitmap.FindFirstSet();  // ? Constant time!
    return new PageId((ulong)pageIndex);
}
```

**Metrics:**
- Before: O(n) search = 50ms for 10K pages
- After: O(1) lookup = 0.5ms
- **Improvement: 100x faster**

## ? Target Performance

### Baseline (Before Optimizations)

```
10K INSERT Performance:
  PAGE_BASED:  2776ms   (3.6 records/ms)  ? 32x slower than SQLite
  COLUMNAR:    1500ms   (6.7 records/ms)  ? 18x slower than SQLite
  SQLite:      42ms     (238 records/ms)  ? Baseline
```

### Target (After Optimizations)

```
10K INSERT Performance:
  PAGE_BASED:  200-300ms   (33-50 records/ms)  ? Within 20-30% of SQLite
  COLUMNAR:    500-700ms   (14-20 records/ms)  ? Acceptable for OLAP
  SQLite:      42ms        (238 records/ms)    ? Baseline
```

### Break-even Point

**Goal:** Match or exceed SQLite on workload-specific operations

- **OLTP (Updates, Random Access):** PAGE_BASED should win
- **OLAP (Aggregates, Scans):** COLUMNAR should win
- **Bulk Insert:** Within 20-30% of SQLite is acceptable

## ? Common Issues

### Issue 1: Trace Collection Hangs

**Symptom:** `dotnet-trace collect` never completes

**Cause:** Process not responding to profiler events

**Fix:**
```powershell
# Kill hung process
taskkill /F /PID <process-id>

# Restart with shorter duration
dotnet-trace collect --process-id <PID> --duration 00:00:30
```

### Issue 2: Trace File Too Large (>1GB)

**Symptom:** speedscope.app crashes loading trace

**Cause:** Too many events captured

**Fix:**
```powershell
# Use sampling instead of tracing
dotnet-trace collect --process-id <PID> --profile cpu-sampling

# Or limit duration
--duration 00:00:10
```

### Issue 3: No SharpCoreDB Methods in Trace

**Symptom:** Only System.* methods visible

**Cause:** Symbols not loaded or Release build stripped symbols

**Fix:**
```powershell
# Build with symbols
dotnet build -c Release /p:DebugType=portable

# Or use Debug build for profiling
dotnet build -c Debug
```

### Issue 4: High Gen2 GC Pressure

**Symptom:** Frequent Gen2 collections slow down inserts

**Cause:** Large object allocations or memory leaks

**Fix:**
```csharp
// Use ArrayPool to avoid LOH allocations
byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
try {
    // Use buffer
} finally {
    ArrayPool<byte>.Shared.Return(buffer);
}
```

## ? Advanced Profiling

### Custom Event Providers

Add custom EventSource to SharpCoreDB:

```csharp
[EventSource(Name = "SharpCoreDB-Performance")]
public sealed class SharpCoreDBEventSource : EventSource
{
    [Event(1, Level = EventLevel.Informational)]
    public void InsertStart(int recordCount) => WriteEvent(1, recordCount);
    
    [Event(2, Level = EventLevel.Informational)]
    public void InsertComplete(int recordCount, long durationMs) 
        => WriteEvent(2, recordCount, durationMs);
}
```

Collect custom events:

```powershell
dotnet-trace collect --process-id <PID> --providers SharpCoreDB-Performance
```

### PerfView Scenarios

**Scenario 1: Find Allocations >10MB**

1. Open PerfView
2. File > Open > `<trace>.nettrace`
3. Memory > GC Heap Allocations
4. Filter: `Size > 10MB`

**Scenario 2: Identify Lock Contention**

1. PerfView > Advanced > Thread Times
2. Look for "Blocked Time" spikes
3. Drill into call stacks

**Scenario 3: I/O Bottlenecks**

1. PerfView > Advanced > Disk I/O
2. Filter by process name
3. Identify slow I/O operations

## ? Results Interpretation

### Good Profile Characteristics

? CPU time distributed evenly across methods (<10% per method)
? Gen0/Gen1 GCs only, no Gen2
? <100 FileStream.Flush calls per 10K inserts
? Cache hit rate >90%
? Throughput >20 records/ms

### Bad Profile Characteristics

? Single method consuming >30% CPU
? Frequent Gen2 GC collections
? 10K+ individual storage calls
? Cache miss rate >50%
? Throughput <5 records/ms

## ? Next Steps

After profiling and identifying hotspots:

1. **Apply Fixes** documented above
2. **Re-profile** to measure improvement
3. **Benchmark** against baseline traces
4. **Document** performance gains
5. **Share** results with team

## ? Reference

- **dotnet-trace docs:** https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace
- **PerfView:** https://github.com/microsoft/perfview
- **Speedscope:** https://speedscope.app
- **EventSource guide:** https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource

---

**Maintainer:** MPCoreDeveloper  
**Last Updated:** 2025-01-16  
**Version:** 1.0
