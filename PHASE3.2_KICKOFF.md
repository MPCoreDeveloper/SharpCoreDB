# 🚀 Phase 3.2: Select Optimization - KICKOFF

**Date:** 2025-01-28  
**Status:** ✅ **ACTIVE - AGENT MODE**  
**Priority:** 🟡 **HIGH**  
**Target:** 4.1 ms → <1 ms (75% improvement)

---

## 🎯 Objective

**Optimize `SingleFileStorageProvider` select operations through metadata caching and read-ahead buffering.**

### Current Performance (Baseline):

```
SCDB_Single_Select:  4.1 ms
SCDB_Dir_Select:     910 µs (4.5x faster)
PageBased_Select:    1.1 ms (3.7x faster)

Problem: Single-file mode is 4.5x slower than directory mode
Target:  <1 ms (match or exceed directory mode)
```

---

## 🔍 Root Cause Analysis

### Issue #1: Block Metadata Lookup
**Current Implementation:**
```csharp
// Every block read requires registry lookup
var entry = _blockRegistry.GetEntry(blockName);
_fileStream.Position = (long)entry.Offset;
await _fileStream.ReadAsync(buffer, cancellationToken);
```

**Problem:**
- Registry lookup per block read
- No metadata caching
- Extra I/O for metadata

**Solution:** LRU metadata cache

---

### Issue #2: Sequential Scan Inefficiency
**Current Implementation:**
```csharp
// Read blocks one-by-one
foreach (var blockName in blockNames)
{
    var data = await ReadBlockAsync(blockName);
    // No prefetch for next block
}
```

**Problem:**
- Sequential I/O not optimized
- No read-ahead for next block
- Cache-cold reads

**Solution:** Read-ahead buffer

---

### Issue #3: Memory-Mapped File Overhead
**Current Implementation:**
```csharp
// Create accessor for each read
using var accessor = _memoryMappedFile.CreateViewAccessor(
    viewOffset, viewLength, MemoryMappedFileAccess.Read);
```

**Problem:**
- Accessor creation overhead
- No accessor pooling
- OS handle allocation per read

**Solution:** ViewAccessor pooling

---

## 🎯 Phase 3.2 Optimizations

### 1. ✅ Block Metadata Cache (LRU)

**Implementation:**
```csharp
/// <summary>
/// ✅ C# 14: LRU cache for block metadata using Lock class.
/// Reduces registry lookups by caching frequently accessed block entries.
/// </summary>
public sealed class BlockMetadataCache
{
    private readonly Dictionary<string, CacheEntry> _cache = [];
    private readonly LinkedList<string> _lru = new();
    private readonly Lock _cacheLock = new(); // C# 14
    private const int MAX_CACHE_SIZE = 1000;
    
    private sealed record CacheEntry(BlockEntry Entry, DateTime AccessTime);
    
    public bool TryGet(string blockName, out BlockEntry entry)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(blockName, out var cached))
            {
                // Move to front (MRU)
                _lru.Remove(blockName);
                _lru.AddFirst(blockName);
                
                // Update access time
                _cache[blockName] = cached with { AccessTime = DateTime.UtcNow };
                
                entry = cached.Entry;
                return true;
            }
            
            entry = default;
            return false;
        }
    }
    
    public void Add(string blockName, BlockEntry entry)
    {
        lock (_cacheLock)
        {
            if (_cache.Count >= MAX_CACHE_SIZE)
            {
                // Evict LRU
                var lru = _lru.Last!.Value;
                _cache.Remove(lru);
                _lru.RemoveLast();
            }
            
            _cache[blockName] = new CacheEntry(entry, DateTime.UtcNow);
            _lru.AddFirst(blockName);
        }
    }
    
    public (int Size, double HitRate) GetStatistics()
    {
        lock (_cacheLock)
        {
            // Calculate hit rate from access patterns
            return (_cache.Count, 0.0); // TODO: track hits/misses
        }
    }
}
```

**Expected Impact:** ~1-2ms improvement (25-50% reduction)

---

### 2. ✅ Read-Ahead Buffer

**Implementation:**
```csharp
/// <summary>
/// ✅ C# 14: Prefetch buffer using Channel for async prefetching.
/// Predicts sequential access patterns and prefetches next blocks.
/// </summary>
public sealed class ReadAheadBuffer
{
    private readonly int _bufferSize = 64 * 1024; // 64 KB
    private readonly Channel<PrefetchRequest> _prefetchQueue;
    private readonly Dictionary<string, byte[]> _buffer = [];
    private readonly Lock _bufferLock = new(); // C# 14
    private readonly Task _prefetchTask;
    private readonly CancellationTokenSource _cts = new();
    
    private sealed record PrefetchRequest(string BlockName, ulong Offset, int Length);
    
    public ReadAheadBuffer(SingleFileStorageProvider provider)
    {
        _prefetchQueue = Channel.CreateBounded<PrefetchRequest>(10);
        _prefetchTask = Task.Run(() => PrefetchWorkerAsync(provider), _cts.Token);
    }
    
    public void PrefetchAsync(string blockName, ulong offset, int length)
    {
        // Non-blocking hint to prefetch
        _prefetchQueue.Writer.TryWrite(new(blockName, offset, length));
    }
    
    public bool TryGetPrefetched(string blockName, out byte[] data)
    {
        lock (_bufferLock)
        {
            return _buffer.Remove(blockName, out data!);
        }
    }
    
    private async Task PrefetchWorkerAsync(SingleFileStorageProvider provider)
    {
        await foreach (var request in _prefetchQueue.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                // Read block into buffer
                var data = await provider.ReadBlockInternalAsync(
                    request.BlockName, request.Offset, request.Length, _cts.Token);
                
                lock (_bufferLock)
                {
                    _buffer[request.BlockName] = data;
                }
            }
            catch
            {
                // Ignore prefetch errors
            }
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _prefetchTask.Wait(TimeSpan.FromSeconds(1));
        _cts.Dispose();
    }
}
```

**Expected Impact:** ~1-2ms improvement for sequential scans

---

### 3. ✅ ViewAccessor Pooling

**Implementation:**
```csharp
/// <summary>
/// ✅ C# 14: Pool of MemoryMappedViewAccessor for reuse.
/// Reduces OS handle allocation overhead.
/// </summary>
public sealed class ViewAccessorPool
{
    private readonly ConcurrentBag<MemoryMappedViewAccessor> _pool = [];
    private readonly MemoryMappedFile _mmf;
    private const int MAX_POOL_SIZE = 10;
    
    public ViewAccessorPool(MemoryMappedFile mmf)
    {
        _mmf = mmf;
    }
    
    public MemoryMappedViewAccessor Rent(long offset, long length)
    {
        if (_pool.TryTake(out var accessor))
        {
            // Reuse existing accessor if it fits
            if (accessor.Capacity >= length)
            {
                return accessor;
            }
            
            accessor.Dispose();
        }
        
        // Create new accessor
        return _mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
    }
    
    public void Return(MemoryMappedViewAccessor accessor)
    {
        if (_pool.Count < MAX_POOL_SIZE)
        {
            _pool.Add(accessor);
        }
        else
        {
            accessor.Dispose();
        }
    }
    
    public void Dispose()
    {
        while (_pool.TryTake(out var accessor))
        {
            accessor.Dispose();
        }
    }
}
```

**Expected Impact:** ~0.5ms improvement

---

## 📊 Expected Performance Impact

```
Current:                4.1 ms
After Metadata Cache:   ~2.5 ms (-1.6ms, 39%)
After Read-Ahead:       ~1.2 ms (-1.3ms, 52%)
After Accessor Pool:    ~0.8 ms (-0.4ms, 33%)

Target:                 <1 ms
Expected Result:        ~0.8 ms (80% improvement, 5x faster) 🚀
```

---

## 🔥 Modern C# 14 Features

1. **Lock Class** - Modern synchronization
2. **Channel<T>** - Async prefetching
3. **ConcurrentBag<T>** - Lock-free pooling
4. **Record Types** - Cache entries
5. **with Expression** - Update cache entries
6. **Collection Expressions** - `[]` for collections

---

## 📋 Implementation Checklist

- [ ] Create `BlockMetadataCache.cs`
- [ ] Create `ReadAheadBuffer.cs`
- [ ] Create `ViewAccessorPool.cs`
- [ ] Integrate cache in `SingleFileStorageProvider`
- [ ] Integrate read-ahead in `SingleFileStorageProvider`
- [ ] Integrate accessor pool in `SingleFileStorageProvider`
- [ ] Create `Phase3_2_SelectOptimizationTests.cs`
- [ ] Run benchmarks
- [ ] Validate <1ms target

---

## ✅ Success Criteria

- ✅ Select operations <1 ms
- ✅ Cache hit rate >90%
- ✅ All tests passing
- ✅ No memory leaks
- ✅ Backward compatible

---

**Status:** READY TO START 🚀  
**Next:** Implement BlockMetadataCache
