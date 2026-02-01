# SharpCoreDB - Prioritized Work Items

**Generated:** 2026-01-28  
**For:** Immediate action planning

---

## 🔴 CRITICAL - Do First (This Week)

### Priority 1: Complete SCDB Phase 1 (5% remaining)
**Estimated Time:** 8-10 hours (~1-2 days)  
**Blocking:** Phase 2-5 cannot start until this is done

#### Task 1.1: Database Integration
**Time:** ~4 hours  
**Files to Modify:**
- `src/SharpCoreDB/Database/Database.cs`
- `src/SharpCoreDB/Database/Database.Tables.cs`
- `src/SharpCoreDB/Database/Database.Metadata.cs`

**Changes Required:**
1. Add `IStorageProvider _storageProvider` field
2. Replace direct file I/O with provider calls
3. Update SaveMetadata() to use WriteBlockAsync()
4. Update Load() to use ReadBlockAsync()

**Acceptance Criteria:**
- [ ] Database class uses IStorageProvider abstraction
- [ ] SaveMetadata() writes via provider
- [ ] Load() reads via provider
- [ ] Builds without errors
- [ ] Existing tests still pass

---

#### Task 1.2: Write Comprehensive Tests
**Time:** ~4 hours  
**New Files to Create:**

```
tests/SharpCoreDB.Tests/Storage/
├── BlockRegistryTests.cs          (~1 hour)
│   ├── FlushAndLoad_RoundTrip
│   ├── ConcurrentAccess_ThreadSafe
│   └── Corruption_GracefulHandling
│
├── FreeSpaceManagerTests.cs       (~1 hour)
│   ├── AllocateAndFree_Persistence
│   ├── FragmentationTracking
│   └── ExtentAllocation
│
├── VacuumTests.cs                 (~1.5 hours)
│   ├── VacuumQuick_Checkpoint
│   ├── VacuumIncremental_Defragmentation
│   ├── VacuumFull_CompletePack
│   └── PerformanceMetrics
│
└── SingleFileStorageProviderTests.cs (~0.5 hour)
    ├── CreateNew_Initialize
    ├── OpenExisting_Load
    └── ConcurrentWrites_Isolation
```

**Test Template:**
```csharp
[Fact]
public async Task BlockRegistry_FlushAndLoad_RoundTrip()
{
    // Arrange
    using var provider = CreateTestProvider();
    var registry = new BlockRegistry(provider, 0);
    
    // Add 1000 test blocks
    for (int i = 0; i < 1000; i++)
    {
        registry.AddBlock($"block_{i}", (ulong)(i * 1024), 1024);
    }
    
    // Act - Flush to disk
    await registry.FlushAsync();
    
    // Create new registry and load
    var newRegistry = new BlockRegistry(provider, 0);
    
    // Assert - Verify all blocks present
    for (int i = 0; i < 1000; i++)
    {
        Assert.True(newRegistry.TryGetBlock($"block_{i}", out var entry));
        Assert.Equal((ulong)(i * 1024), entry.Offset);
        Assert.Equal(1024u, entry.Size);
    }
}
```

**Acceptance Criteria:**
- [ ] All 4 test files created
- [ ] Minimum 15 tests total
- [ ] All tests pass
- [ ] Code coverage >80% for Storage namespace

---

#### Task 1.3: Documentation Update
**Time:** ~1 hour  
**Files to Update:**

1. **docs/scdb/IMPLEMENTATION_STATUS.md**
   - Change Phase 1 from 95% to 100%
   - Update Testing column from 0% to 80%+
   - Add "Next Steps: Begin Phase 2"

2. **docs/scdb/PHASE1_IMPLEMENTATION.md**
   - Add "Phase 1 Complete" banner
   - Document Database integration
   - Add test results summary

3. **README.md**
   - Update SCDB status to "Phase 1 Complete"
   - Add link to unified roadmap

**Acceptance Criteria:**
- [ ] All documentation reflects Phase 1 completion
- [ ] Test coverage documented
- [ ] Next phase clearly indicated

---

### Summary - Critical Priority
```
Total Time: 8-10 hours
Dependencies: None
Blocks: Phases 2-5
Value: High (unblocks next 8 weeks of work)
Risk: Low (well-defined scope)
```

**Action:** Assign to senior developer, complete this week.

---

## 🟡 MEDIUM - Do Next (Next 2-4 Weeks)

### Priority 2: SCDB Phase 2 - FSM & Allocation
**Estimated Time:** 2 weeks  
**Depends On:** Phase 1 completion

**Deliverables:**
- Free space map implementation
- Extent tracking for large allocations
- Page allocator optimization
- Performance benchmarks

**Reference:** `docs/scdb/DESIGN_SUMMARY.md` - Implementation Roadmap

---

### Priority 3: SCDB Phase 3 - WAL & Recovery
**Estimated Time:** 2 weeks  
**Depends On:** Phase 2 completion

**Deliverables:**
- Complete WalManager implementation (currently 60%)
- Circular buffer management
- Crash recovery replay
- Checkpoint logic

**Current File:** `src/SharpCoreDB/Storage/Scdb/WalManager.cs`

---

### Priority 4: Query Routing Refactoring
**Estimated Time:** 1 week  
**Depends On:** None (can run parallel)

**Value:** Architectural improvement, reduce code duplication

**Phases:**
- Phase 1: Stabilization ✅ Done
- Phase 2: Refactoring 📋 Planned
- Phase 3: Testing 📋 Planned

**Reference:** `docs/architecture/QUERY_ROUTING_REFACTORING_PLAN.md`

---

## 🟢 LOW - Do Later (Future)

### Priority 5: Fix XML Documentation Warnings
**Estimated Time:** 2 hours  
**Value:** Cosmetic, improved IntelliSense

**Files to Fix:**
- `Table.PerformanceOptimizations.cs`
- `QueryCompiler.cs`
- `ObjectPool.cs`
- `SqlParser.PerformanceOptimizations.cs`
- `IndexedRowData.cs`
- `Database.PerformanceOptimizations.cs`

**Issue:** Malformed XML comments (whitespace, unmatched tags)

---

### Priority 6: Entity Framework Core Provider
**Estimated Time:** 2-3 weeks  
**Depends On:** SCDB Phase 1-3 completion

**Current Status:** Stub implementations only

**Files to Complete:**
- `SharpCoreDBSqlGenerationHelper.cs`
- `SharpCoreDBModificationCommandBatchFactory.cs`

**Value:** EF Core integration, broader adoption

---

### Priority 7: SCDB Phases 4-5
**Estimated Time:** 4 weeks total  
**Depends On:** Phase 3 completion

**Phase 4: Integration** (2 weeks)
- PageBased/Columnar integration
- Migration tool
- Cross-format compatibility

**Phase 5: Hardening** (2 weeks)
- Error handling improvements
- Corruption detection
- Production-ready documentation

---

## 📊 Work Breakdown Timeline

### Week 1 (Current)
- ✅ Complete SCDB Phase 1
- ✅ Write and run tests
- ✅ Update documentation

### Weeks 2-3
- 🔄 SCDB Phase 2 (FSM & Allocation)

### Weeks 4-5
- 🔄 SCDB Phase 3 (WAL & Recovery)

### Week 6
- 🔄 Query Routing Refactoring

### Weeks 7-8
- 🔄 SCDB Phase 4 (Integration)

### Weeks 9-10
- 🔄 SCDB Phase 5 (Hardening)

### Week 11+
- 🔄 EF Core Provider (if needed)
- 🔄 Polish items (XML warnings, etc.)

---

## 🎯 Key Decision Points

### Decision 1: Complete SCDB First?
**Recommendation:** YES  
**Reasoning:**
- SCDB is 95% done, finish it
- Phases 2-5 are already planned
- Provides foundation for future work

---

### Decision 2: Refactor Query Routing Now?
**Recommendation:** AFTER Phase 3  
**Reasoning:**
- Not blocking other work
- Can run parallel if resources available
- Low priority vs SCDB completion

---

### Decision 3: EF Core Priority?
**Recommendation:** WAIT  
**Reasoning:**
- No customer demand yet
- SCDB storage is higher value
- Can defer to v2.0

---

## 📋 Task Assignment Suggestions

### Senior Developer (Full-time)
- Week 1: SCDB Phase 1 completion
- Weeks 2-5: SCDB Phases 2-3
- Weeks 6-10: SCDB Phases 4-5

### Junior Developer (Part-time)
- Week 1: Help with testing
- Week 6: Query Routing Refactoring
- Week 11+: XML documentation fixes

### Outsource/Future
- EF Core Provider (2-3 weeks)
- Additional performance optimizations

---

## ✅ Acceptance Criteria (Phase 1)

Before moving to Phase 2, verify:

1. **Code Quality**
   - [ ] Build successful (0 errors)
   - [ ] All existing tests pass
   - [ ] New tests added and passing
   - [ ] Code coverage >80% for Storage namespace

2. **Functionality**
   - [ ] Database uses IStorageProvider abstraction
   - [ ] BlockRegistry persists and loads correctly
   - [ ] FreeSpaceManager persists and loads correctly
   - [ ] VACUUM operations work as expected

3. **Performance**
   - [ ] BlockRegistry flush <10ms
   - [ ] FSM flush <10ms
   - [ ] VACUUM Quick <20ms
   - [ ] VACUUM Incremental <200ms
   - [ ] VACUUM Full <15s/GB

4. **Documentation**
   - [ ] IMPLEMENTATION_STATUS.md updated
   - [ ] PHASE1_IMPLEMENTATION.md complete
   - [ ] README.md reflects status
   - [ ] API documentation complete

---

## 🚀 Getting Started (Today)

### Step 1: Review Unified Roadmap
Read: `docs/PROJECT_STATUS_UNIFIED.md`

### Step 2: Start Database Integration
File: `src/SharpCoreDB/Database/Database.cs`

### Step 3: Write First Test
File: `tests/SharpCoreDB.Tests/Storage/BlockRegistryTests.cs`

### Step 4: Iterate
Build → Test → Fix → Repeat

---

**Questions? See `docs/PROJECT_STATUS_UNIFIED.md` for context.**

**Ready to start? Begin with Task 1.1 (Database Integration).**
