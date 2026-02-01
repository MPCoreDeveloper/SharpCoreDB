# SCDB Phase 2: Extent Allocator Design

**Created:** 2026-01-28  
**Status:** 📝 Design Phase

---

## 🎯 Goals

1. **Public API** for page/extent allocation
2. **Extent structure** for contiguous block tracking
3. **Allocation optimization** - O(log n) lookup
4. **Defragmentation** support

---

## 📐 API Design

### Public Methods to Add to FreeSpaceManager

```csharp
// Single page allocation
public ulong AllocatePage();

// Extent allocation (returns Extent struct)
public Extent AllocateExtent(int pageCount);

// Single page free
public void FreePage(ulong pageId);

// Extent free
public void FreeExtent(Extent extent);

// Statistics with more detail
public FsmStatistics GetStatistics();
```

---

## 📦 Data Structures

### Extent Structure

```csharp
/// <summary>
/// Represents a contiguous block of pages.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Extent
{
    public readonly ulong StartPage;
    public readonly int PageCount;
    
    public Extent(ulong startPage, int pageCount)
    {
        StartPage = startPage;
        PageCount = pageCount;
    }
    
    public ulong EndPage => StartPage + (ulong)PageCount - 1;
    public ulong SizeBytes(int pageSize) => (ulong)PageCount * (ulong)pageSize;
}
```

### FsmStatistics Structure

```csharp
/// <summary>
/// FSM statistics for monitoring and debugging.
/// </summary>
public readonly record struct FsmStatistics
{
    public required long TotalPages { get; init; }
    public required long FreePages { get; init; }
    public required long UsedPages { get; init; }
    public required long FreeSpace { get; init; }
    public required long LargestExtent { get; init; }
    public required int ExtentCount { get; init; }
    public required double FragmentationPercent { get; init; }
}
```

---

## 🚀 Implementation Plan

### Step 1: Add Public API Methods
- `AllocatePage()` - wrapper around `AllocatePages(1)`
- `FreePage()` - wrapper around `FreePages(offset, 1)`
- `AllocateExtent()` - returns Extent struct
- `FreeExtent()` - accepts Extent struct

### Step 2: Create Extent Structure
- Define in `src/SharpCoreDB/Storage/Scdb/Extent.cs`
- ReadOnly struct for immutability
- Helper properties (EndPage, SizeBytes)

### Step 3: Enhance Statistics
- Add TotalPages, UsedPages fields
- Calculate FragmentationPercent
- Return ExtentCount and LargestExtent

### Step 4: Optimize Allocation
- Use L2 extent list for fast contiguous lookup
- Sort extents by size for O(log n) search
- Implement best-fit allocation strategy

---

## 📊 Performance Targets

| Operation | Current | Target | Notes |
|-----------|---------|--------|-------|
| AllocatePage | O(n) scan | O(log n) | Use extent list |
| AllocateExtent | O(n) scan | O(log n) | Best-fit from sorted list |
| FreePage | O(1) | O(1) | No change needed |
| GetStatistics | O(n) | O(1) | Cache values |

---

## 🧪 Testing Plan

### Unit Tests
1. **Single Page Operations**
   - Allocate/free single page
   - Round-trip persistence

2. **Extent Operations**
   - Allocate extent of various sizes (1, 10, 100, 1000 pages)
   - Free extent
   - Verify contiguous allocation

3. **Fragmentation**
   - Create fragmentation pattern
   - Measure fragmentation percent
   - Verify extent coalescing

4. **Performance**
   - Benchmark allocation speed
   - Verify O(log n) complexity
   - Compare with Phase 1

### Integration Tests
1. **Database Integration**
   - Use FSM through IStorageProvider
   - Verify metadata persistence
   - Test with real workload

---

## 📝 Implementation Checklist

- [ ] Create `Extent.cs` structure
- [ ] Create `FsmStatistics.cs` structure
- [ ] Add `AllocatePage()` method
- [ ] Add `FreePage()` method
- [ ] Add `AllocateExtent()` method
- [ ] Add `FreeExtent()` method
- [ ] Enhance `GetStatistics()` method
- [ ] Optimize extent allocation (best-fit)
- [ ] Add fragmentation calculation
- [ ] Write unit tests
- [ ] Write benchmarks
- [ ] Update documentation

---

## 🎯 Success Criteria

- [x] Public API complete
- [ ] All tests passing
- [ ] Page allocation <1ms
- [ ] Extent allocation <1ms
- [ ] Fragmentation tracking accurate
- [ ] Documentation updated

---

**Status:** Ready for implementation  
**Next:** Step 1 - Create Extent structure
