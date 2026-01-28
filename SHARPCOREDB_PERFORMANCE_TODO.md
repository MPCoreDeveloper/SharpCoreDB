# SharpCoreDB Performance Optimization TODO

**Datum:** 28 januari 2025  
**Benchmark Analyse:** StorageEngineComparisonBenchmark  
**Focus:** SCDB_Single_* performance improvements

---

## 📊 Executive Summary

### Huidige Performance Status

| Categorie | SCDB_Dir | SCDB_Single | Status | Prioriteit |
|-----------|----------|-------------|---------|-----------|
| **Analytics (SIMD)** | N/A | N/A | ✅ **UITSTEKEND** (640 ns) | - |
| **Insert** | ~12 ms | ~9.6 ms | ✅ **GOED** (competitief met SQLite) | LOW |
| **Select** | ~786 µs | ~4.1 ms | ⚠️ **MATIG** (5x trager dan Dir) | MEDIUM |
| **Update** | ~8.8 ms | **506 ms** | 🔴 **KRITIEK** (59x trager!) | **CRITICAL** |

### Key Findings

1. **🚨 KRITIEK**: SCDB_Single_Update is **59x langzamer** dan baseline (500+ ms vs 8.5 ms)
2. **⚠️ Probleem**: SCDB_Single_Select is **5x langzamer** dan SCDB_Dir (4.1 ms vs 786 µs)
3. **✅ Sterk**: Columnar SIMD analytics zijn **1000x sneller** dan SQLite
4. **✅ Sterk**: Encryption overhead is verwaarloosbaar (<5%)
5. **⚠️ Geheugen**: SCDB_Single alloceert ~8 MB bij updates vs 2.8 MB voor Dir

---

## 🔴 PRIORITY 1: CRITICAL ISSUES

### Issue #1: SCDB_Single_Update Performance (500+ ms)

**Huidige situatie:**
- Update operation neemt **506 ms** voor 500 records
- **59x langzamer** dan PageBased baseline
- 8.3 MB memory allocations (vs 2.9 MB voor Dir)

**Root Cause Analysis:**

#### 1.1 Waarschijnlijk Volledige File Rewrite
```csharp
// Huidige flow bij update in SingleFileStorageProvider.WriteBlockAsync:
// 1. Read block registry om block te vinden
// 2. Check of nieuwe data past in bestaande space
// 3. Als NIET past → free old space + allocate new space
// 4. Write data naar file
// 5. Flush file buffers (flushToDisk: true) ← EXPENSIVE!
// 6. Read back voor checksum verificatie ← EXTRA I/O!
// 7. Compute SHA256 checksum ← CPU intensive
// 8. Update block registry
// 9. Flush registry naar disk
```

**Specifieke Problemen:**

```csharp
// ⚠️ PROBLEEM 1: Registry flush bij ELKE update
// Locatie: src\SharpCoreDB\Storage\SingleFileStorageProvider.cs:363-365
await _blockRegistry.FlushAsync(cancellationToken).ConfigureAwait(false);

// ⚠️ PROBLEEM 2: Dubbele I/O voor checksum verificatie
// Locatie: src\SharpCoreDB\Storage\SingleFileStorageProvider.cs:351-355
_fileStream.Position = (long)offset;
await _fileStream.ReadExactlyAsync(verifyBuffer.AsMemory(0, data.Length), cancellationToken);
var checksumOnDisk = SHA256.HashData(verifyBuffer.AsSpan(0, data.Length));

// ⚠️ PROBLEEM 3: Synchronous flush blokkeert async flow
// Locatie: src\SharpCoreDB\Storage\SingleFileStorageProvider.cs:345
_fileStream.Flush(flushToDisk: true); // ← Blocks OS cache flush
```

#### 1.2 Inefficiënte Page Allocation Pattern

```csharp
// Locatie: src\SharpCoreDB\Storage\FreeSpaceManager.cs:53-73
public ulong AllocatePages(int count)
{
    lock (_allocationLock)  // ← Global lock per allocation
    {
        var startPage = FindContiguousFreePages(count);
        if (startPage == ulong.MaxValue)
        {
            // ⚠️ PROBLEEM: File extension per update batch!
            startPage = _totalPages;
            ExtendFile(count);  // ← Mogelijk resize van hele file
        }
    }
}
```

#### 1.3 Block Registry Overhead

```csharp
// Locatie: src\SharpCoreDB\Storage\BlockRegistry.cs:74-141
public async Task FlushAsync(CancellationToken cancellationToken = default)
{
    lock (_registryLock)
    {
        // ⚠️ PROBLEEM: Volledige registry serialize bij elke flush
        entriesSnapshot = _blocks.ToArray();  // ← Allocates
        
        // Calculate total size
        totalSize = headerSize + (entriesSnapshot.Length * entrySize);
        
        // ⚠️ PROBLEEM: Rent buffer, write ALL entries
        buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        
        // Write header + all entries
        foreach (var kvp in entriesSnapshot) { ... }
    }
    
    // ⚠️ PROBLEEM: Full disk flush
    fileStream.Flush(flushToDisk: true);
}
```

**💡 Oplossingen:**

1. **Batch Registry Flushes** (Impact: ~30-40% verbetering)
   ```csharp
   // Implementeer delayed flush met dirty tracking
   private bool _registryNeedsFlush;
   private DateTime _lastRegistryFlush;
   private const int REGISTRY_FLUSH_INTERVAL_MS = 100;
   
   public void AddOrUpdateBlock(string blockName, BlockEntry entry)
   {
       _blocks[blockName] = entry;
       _registryNeedsFlush = true;
       // DON'T flush immediately
   }
   
   public async Task FlushAsync(bool force = false)
   {
       if (!force && !_registryNeedsFlush) return;
       
       var timeSinceLastFlush = DateTime.UtcNow - _lastRegistryFlush;
       if (!force && timeSinceLastFlush.TotalMilliseconds < REGISTRY_FLUSH_INTERVAL_MS)
           return;
       
       // Flush only if needed
       await FlushRegistryToDisk();
   }
   ```

2. **Eliminate Read-Back Verification** (Impact: ~20% verbetering)
   ```csharp
   // Compute checksum BEFORE write, niet NA read-back
   public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, ...)
   {
       // Checksum van input data (in memory)
       var checksum = SHA256.HashData(data.Span);
       
       // Write to disk
       _fileStream.Position = (long)offset;
       await _fileStream.WriteAsync(data, cancellationToken);
       
       // ❌ REMOVE: Read-back verification
       // ✅ Trust OS write buffering + use checksum on READ instead
       
       // Update registry met pre-computed checksum
       entry = SetChecksum(entry, checksum);
       _blockRegistry.AddOrUpdateBlock(blockName, entry);
   }
   ```

3. **Async Flush with Write-Behind Cache** (Impact: ~40-50% verbetering)
   ```csharp
   // Implementeer write-behind cache voor batch updates
   private readonly Channel<WriteOperation> _writeQueue;
   private readonly Task _flushTask;
   
   public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, ...)
   {
       // Queue write operation
       await _writeQueue.Writer.WriteAsync(new WriteOperation
       {
           BlockName = blockName,
           Data = data,
           Checksum = SHA256.HashData(data.Span)
       });
       
       // Return immediately (async write-behind)
   }
   
   private async Task ProcessWriteQueueAsync(CancellationToken ct)
   {
       while (await _writeQueue.Reader.WaitToReadAsync(ct))
       {
           // Batch multiple writes
           var batch = new List<WriteOperation>();
           while (_writeQueue.Reader.TryRead(out var op) && batch.Count < 100)
           {
               batch.Add(op);
           }
           
           // Write batch to disk in single I/O operation
           await WriteBatchAsync(batch);
           
           // Flush registry ONCE per batch
           await _blockRegistry.FlushAsync();
       }
   }
   ```

4. **Pre-allocate File Space** (Impact: ~15-20% verbetering)
   ```csharp
   // Locatie: FreeSpaceManager.ExtendFile
   private void ExtendFile(int pageCount)
   {
       // ✅ Pre-allocate in grotere chunks (bijv. 1 MB)
       var minExtension = Math.Max(pageCount, 256); // 256 pages = 1MB @ 4KB
       
       var newSize = _totalPages + (ulong)minExtension;
       _fileStream.SetLength((long)(newSize * (ulong)_pageSize));
       _totalPages = newSize;
   }
   ```

**🎯 Verwachte Impact:**
- **Gecombineerd**: 60-70% snelheidsverbetering (506 ms → ~150-200 ms)
- **Target**: <100 ms (10x beter dan huidige 506 ms)

---

### Issue #2: SCDB_Single_Select Performance (4.1 ms)

**Huidige situatie:**
- Select operation neemt **4.1 ms** voor records waar age > 30
- **5x langzamer** dan SCDB_Dir (786 µs)
- 1.6 MB allocations

**Root Cause Analysis:**

#### 2.1 Memory-Mapped File Not Used Optimally

```csharp
// Locatie: src\SharpCoreDB\Storage\SingleFileStorageProvider.cs:191-219
public unsafe ReadOnlySpan<byte> GetReadSpan(string blockName)
{
    if (_memoryMappedFile != null)
    {
        try
        {
            using var accessor = _memoryMappedFile.CreateViewAccessor(...);
            // ⚠️ PROBLEEM: ViewAccessor per GetReadSpan call
            // Creates overhead voor elke row read
        }
        catch { /* Fall through */ }
    }
    
    // ⚠️ FALLBACK: Allocates byte array
    var buffer2 = new byte[(int)entry.Length];
    _fileStream.ReadExactly(buffer2);
    return buffer2;
}
```

#### 2.2 Block Registry Lookup Overhead

```csharp
// Elke SELECT query doet:
// 1. GetReadSpan → BlockRegistry.TryGetBlock
// 2. Hash lookup in ConcurrentDictionary
// 3. Voor ELKE row tijdens scan

// Bij 2000+ records = 2000+ dictionary lookups
```

**💡 Oplossingen:**

1. **Persistent ViewAccessor Pool** (Impact: ~40% verbetering)
   ```csharp
   // Cache ViewAccessors per table block
   private readonly ConcurrentDictionary<string, MemoryMappedViewAccessor> _viewAccessorCache;
   
   public unsafe ReadOnlySpan<byte> GetReadSpan(string blockName)
   {
       if (!_blockRegistry.TryGetBlock(blockName, out var entry))
           return ReadOnlySpan<byte>.Empty;
       
       // ✅ Reuse cached accessor
       if (!_viewAccessorCache.TryGetValue(blockName, out var accessor))
       {
           accessor = _memoryMappedFile.CreateViewAccessor(
               (long)entry.Offset, (long)entry.Length, 
               MemoryMappedFileAccess.Read);
           _viewAccessorCache[blockName] = accessor;
       }
       
       byte* ptr = null;
       accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
       return new ReadOnlySpan<byte>(ptr, (int)entry.Length);
   }
   ```

2. **Block Metadata Caching** (Impact: ~20% verbetering)
   ```csharp
   // Cache block metadata voor hot tables
   private readonly struct CachedBlockMetadata
   {
       public ulong Offset { get; init; }
       public ulong Length { get; init; }
       public long CachedAt { get; init; }
   }
   
   private readonly ConcurrentDictionary<string, CachedBlockMetadata> _metadataCache;
   
   public bool TryGetBlockFast(string blockName, out BlockEntry entry)
   {
       // Check L1 cache first
       if (_metadataCache.TryGetValue(blockName, out var cached))
       {
           entry = new BlockEntry { Offset = cached.Offset, Length = cached.Length };
           return true;
       }
       
       // Fallback to registry
       return _blockRegistry.TryGetBlock(blockName, out entry);
   }
   ```

3. **Read-Ahead for Sequential Scans** (Impact: ~30% verbetering)
   ```csharp
   // Detect sequential scan pattern en prefetch data
   private long _lastReadOffset = -1;
   private const int READAHEAD_THRESHOLD = 3;
   private int _sequentialReadCount = 0;
   
   public ReadOnlySpan<byte> GetReadSpan(string blockName)
   {
       var entry = GetBlockEntry(blockName);
       
       // Detect sequential pattern
       if (_lastReadOffset >= 0 && entry.Offset == _lastReadOffset + _lastReadLength)
       {
           _sequentialReadCount++;
           
           if (_sequentialReadCount >= READAHEAD_THRESHOLD)
           {
               // ✅ Trigger read-ahead (advise OS)
               PrefetchNextBlocks(entry.Offset + entry.Length, 128 * 1024); // 128 KB
           }
       }
       else
       {
           _sequentialReadCount = 0;
       }
       
       _lastReadOffset = (long)entry.Offset;
       return GetReadSpanInternal(entry);
   }
   ```

**🎯 Verwachte Impact:**
- **Gecombineerd**: 60-70% verbetering (4.1 ms → ~1.2-1.6 ms)
- **Target**: <1 ms (competitief met SCDB_Dir)

---

## 🟡 PRIORITY 2: MEDIUM ISSUES

### Issue #3: Memory Allocations Optimization

**Huidige situatie:**
- SCDB_Single_Update: 8.3 MB allocations (vs 2.9 MB voor Dir)
- Veel intermediate buffers en copies

**💡 Oplossingen:**

1. **ArrayPool Agressiever Gebruiken**
   ```csharp
   // Alle buffer allocations vervangen door ArrayPool
   // Locatie: BlockRegistry.FlushAsync, FreeSpaceManager.FlushAsync
   
   var buffer = ArrayPool<byte>.Shared.Rent(size);
   try
   {
       // Use buffer
   }
   finally
   {
       ArrayPool<byte>.Shared.Return(buffer);
   }
   ```

2. **Reduce Checksum Computation Frequency**
   ```csharp
   // Only compute checksums for critical operations
   // Skip for internal/temporary blocks
   
   public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, 
       bool verifyChecksum = false)  // ← Add flag
   {
       if (verifyChecksum)
       {
           var checksum = SHA256.HashData(data.Span);
           // ...
       }
       else
       {
           // Skip checksum for performance-critical updates
       }
   }
   ```

3. **Span-based API Everywhere**
   ```csharp
   // Replace byte[] parameters with ReadOnlySpan<byte>
   public async Task WriteBlockAsync(string blockName, ReadOnlySpan<byte> data, ...)
   {
       // Zero-copy implementation
   }
   ```

**🎯 Verwachte Impact:**
- 40-50% reductie in allocations
- 10-15% CPU reduction (fewer GC pauses)

---

### Issue #4: Concurrency & Lock Contention

**Huidige situatie:**
- Global `_ioGate` semaphore serialiseert ALLE I/O
- `_allocationLock` in FreeSpaceManager blokkeert parallelle allocaties

**💡 Oplossingen:**

1. **Lock-Free Block Registry**
   ```csharp
   // Gebruik ConcurrentDictionary zonder extra locking
   public void AddOrUpdateBlock(string blockName, BlockEntry entry)
   {
       _blocks.AddOrUpdate(blockName, entry, (k, old) => entry);
       Interlocked.Exchange(ref _isDirty, 1);
   }
   ```

2. **Striped Locking voor FSM**
   ```csharp
   // Partition free space map per region
   private const int STRIPE_COUNT = 16;
   private readonly Lock[] _regionLocks = new Lock[STRIPE_COUNT];
   
   public ulong AllocatePages(int count)
   {
       var stripe = GetStripeForAllocation(count);
       lock (_regionLocks[stripe])
       {
           // Allocate from specific region
       }
   }
   ```

3. **Async I/O Gate with Fairness**
   ```csharp
   // Replace SemaphoreSlim with more granular locking
   private readonly AsyncReaderWriterLock _ioLock = new();
   
   public async Task WriteBlockAsync(...)
   {
       await _ioLock.WriterLockAsync();  // Exclusive for writes
       try { ... } finally { _ioLock.WriterRelease(); }
   }
   
   public async Task<byte[]?> ReadBlockAsync(...)
   {
       await _ioLock.ReaderLockAsync();  // Shared for reads
       try { ... } finally { _ioLock.ReaderRelease(); }
   }
   ```

**🎯 Verwachte Impact:**
- 30-40% verbetering bij concurrent workloads
- Betere scalability op multi-core systems

---

## 🟢 PRIORITY 3: OPTIMIZATION OPPORTUNITIES

### Issue #5: Analytics Performance Behouden

**Huidige situatie:**
- ✅ Columnar SIMD Sum is **1000x sneller** dan SQLite (640 ns vs 664 µs)
- Dit is een **unique selling point**!

**💡 Acties:**

1. **Expand SIMD Operations**
   ```csharp
   // Add more SIMD-accelerated aggregations
   public T Max<T>(string columnName) where T : struct, INumber<T>
   {
       // Use Vector<T> for SIMD max finding
   }
   
   public T Min<T>(string columnName) where T : struct, INumber<T>
   {
       // SIMD min finding
   }
   
   public Dictionary<TKey, int> GroupCount<TKey>(string columnName)
   {
       // SIMD-accelerated group by
   }
   ```

2. **Columnar Index Compression**
   ```csharp
   // Implement lightweight compression for columnar storage
   // - Run-length encoding for repeated values
   // - Delta encoding for sequential IDs
   // - Dictionary encoding for low-cardinality strings
   ```

3. **Vectorized Filtering**
   ```csharp
   // Optimize WHERE clause evaluation with SIMD
   public IEnumerable<Dictionary<string, object>> SelectColumnar(string where)
   {
       // Parse where: "age > 30"
       var filterMask = EvaluateFilterVectorized("age", ">", 30);
       
       // Apply mask to select matching rows (SIMD accelerated)
       return ApplyFilterMask(filterMask);
   }
   ```

**🎯 Verwachte Impact:**
- Behoud van 1000x performance advantage
- Uitbreiding naar meer query types

---

### Issue #6: Comparison met SQLite/LiteDB

**Huidige situatie:**
- LiteDB heeft **zeer hoge allocations** (11 MB voor sum, 24 MB voor updates)
- SQLite is sneller bij updates (7 ms) maar 1000x langzamer bij analytics

**💡 Acties:**

1. **Benchmark More Realistic Workloads**
   - Mixed read/write workloads
   - Multi-table joins
   - Complex aggregations
   - Concurrent users

2. **Document Performance Characteristics**
   ```markdown
   ## SharpCoreDB Performance Profile
   
   **Best For:**
   - Analytics workloads (1000x faster than SQLite)
   - Read-heavy applications
   - Bulk inserts
   - Time-series data
   
   **Not Optimal For:**
   - High-frequency single-record updates (improve to <50 ms)
   - Random writes in single-file mode
   
   **Recommendations:**
   - Use SCDB_Dir for write-heavy OLTP
   - Use SCDB_Single for read-heavy analytics
   - Use Columnar storage for analytics
   ```

---

## 📋 Implementation Roadmap

### Phase 1: Critical Fixes (Week 1-2)
**Target: SCDB_Single_Update < 100 ms**

- [ ] Implement batch registry flushes
- [ ] Remove read-back verification
- [ ] Add write-behind cache with batching
- [ ] Pre-allocate file space in larger chunks
- [ ] Add benchmark to track progress

**Verwachte verbetering:** 506 ms → ~100-150 ms (70% improvement)

### Phase 2: Select Optimization (Week 3)
**Target: SCDB_Single_Select < 1 ms**

- [ ] Implement ViewAccessor pooling
- [ ] Add block metadata caching
- [ ] Implement read-ahead for sequential scans
- [ ] Optimize memory-mapped file usage

**Verwachte verbetering:** 4.1 ms → ~1.0 ms (75% improvement)

### Phase 3: Memory & Concurrency (Week 4)
**Target: 50% allocation reduction**

- [ ] Replace all allocations with ArrayPool
- [ ] Implement span-based APIs
- [ ] Add lock-free block registry
- [ ] Implement striped locking for FSM

**Verwachte verbetering:** 8 MB → ~4 MB allocations

### Phase 4: Advanced Optimizations (Week 5-6)
**Target: Match or exceed SQLite performance**

- [ ] Expand SIMD operations (max, min, group by)
- [ ] Implement columnar compression
- [ ] Add vectorized filtering
- [ ] Optimize concurrent workloads

**Verwachte verbetering:** Additional 20-30% across all operations

---

## 🧪 Testing Strategy

### Benchmark Checklist

```csharp
// Add to StorageEngineComparisonBenchmark.cs

[Benchmark]
[BenchmarkCategory("Update_Batch")]
public void SCDB_Single_BatchUpdate_500()
{
    // Test batched updates (should use write-behind)
}

[Benchmark]
[BenchmarkCategory("Update_Batch")]
public void SCDB_Single_BatchUpdate_5000()
{
    // Test larger batches
}

[Benchmark]
[BenchmarkCategory("Select_Sequential")]
public void SCDB_Single_SequentialScan()
{
    // Test read-ahead optimization
}

[Benchmark]
[BenchmarkCategory("Mixed_Workload")]
public void SCDB_Single_MixedReadWrite()
{
    // 70% reads, 30% writes
}
```

### Performance Targets

| Operation | Current | Phase 1 Target | Phase 4 Target |
|-----------|---------|----------------|----------------|
| Insert (1K records) | 9.6 ms | 9.6 ms | <8 ms |
| Select (2K records) | 4.1 ms | 2.0 ms | <1 ms |
| Update (500 records) | 506 ms | 100 ms | <50 ms |
| Analytics (SIMD sum) | 640 ns | 640 ns | <500 ns |
| Memory (update) | 8.3 MB | 5 MB | <3 MB |

---

## 🔍 Diagnostic Tools

### Add Performance Counters

```csharp
// Locatie: src\SharpCoreDB\Storage\SingleFileStorageProvider.cs

public class PerformanceMetrics
{
    public long TotalWrites { get; set; }
    public long TotalReads { get; set; }
    public long RegistryFlushes { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public long WriteLatencyMs { get; set; }
    public long ReadLatencyMs { get; set; }
    public long AllocatedBytes { get; set; }
}

public PerformanceMetrics GetMetrics()
{
    return new PerformanceMetrics
    {
        TotalWrites = Interlocked.Read(ref _totalWrites),
        // ... andere metrics
    };
}
```

### Add Debug Logging

```csharp
#if DEBUG
private void LogSlowOperation(string operation, long elapsedMs, string details)
{
    if (elapsedMs > 10) // Log operations > 10ms
    {
        Debug.WriteLine($"[PERF] {operation} took {elapsedMs}ms: {details}");
    }
}
#endif
```

---

## 📈 Expected Final Results

### After All Optimizations

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| **Update (500 records)** | 506 ms | <50 ms | **90%+** |
| **Select (2K records)** | 4.1 ms | <1 ms | **75%+** |
| **Memory (updates)** | 8.3 MB | <3 MB | **65%+** |
| **Insert** | 9.6 ms | <8 ms | **17%+** |
| **Analytics** | 640 ns | <500 ns | **22%+** |

### Competitive Position

```
Performance Comparison (500 record updates):

SQLite:        ████████ 7.0 ms
SCDB_Dir:      ████████▌ 8.8 ms
PageBased:     ████████▌ 8.6 ms
SCDB_Single:   ████████ <50 ms  ✅ TARGET (was 506 ms)
LiteDB:        ████████████████████ 41 ms

Analytics Comparison (SUM operation):

SCDB_Columnar: ▌ 640 ns   ✅ 1000x FASTER
SQLite:        ████████████████████ 664 µs
LiteDB:        ████████████████████████████ 14 ms
```

---

## 🎯 Success Criteria

### Must Have (Phase 1-2)
- ✅ SCDB_Single_Update < 100 ms (70% improvement)
- ✅ SCDB_Single_Select < 1.5 ms (60% improvement)
- ✅ Memory allocations < 5 MB

### Should Have (Phase 3)
- ✅ SCDB_Single_Update < 50 ms (90% improvement)
- ✅ SCDB_Single_Select < 1 ms (75% improvement)
- ✅ Memory allocations < 3 MB
- ✅ Competitive with SQLite for updates

### Nice to Have (Phase 4)
- ✅ SCDB_Single_Update < 30 ms (outperform SQLite)
- ✅ Analytics operations < 500 ns
- ✅ Zero-copy reads for 90%+ of operations
- ✅ Support concurrent writes without blocking reads

---

## 📚 References

### Related Files
- `src\SharpCoreDB\Storage\SingleFileStorageProvider.cs` - Primary optimization target
- `src\SharpCoreDB\Storage\BlockRegistry.cs` - Registry flush optimization
- `src\SharpCoreDB\Storage\FreeSpaceManager.cs` - Allocation optimization
- `tests\SharpCoreDB.Benchmarks\StorageEngineComparisonBenchmark.cs` - Benchmark harness

### Benchmark Command
```bash
cd tests\SharpCoreDB.Benchmarks
dotnet run -c Release --framework net10.0
```

### Performance Profiling
```bash
# CPU profiling
dotnet-trace collect -- dotnet run -c Release

# Memory profiling
dotnet-counters monitor --process-id <PID>

# Detailed profiling
dotnet-dump collect -p <PID>
```

---

**Last Updated:** 2025-01-28  
**Next Review:** Na implementatie van Phase 1 (target: 2 weken)
