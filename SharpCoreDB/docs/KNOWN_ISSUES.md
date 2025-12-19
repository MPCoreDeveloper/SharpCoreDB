# SharpCoreDB - Known Issues & Missing Features

**Last Updated**: 2025-12-18  
**Status**: ⚠️ INCOMPLETE - Several critical features not fully implemented

---

## Critical Issues

### 1. ❌ PageBased Full Table Scan Not Implemented
**File**: `DataStructures\Table.CRUD.cs` (SelectInternal method)  
**Impact**: SELECT queries with WHERE clauses return EMPTY results on PageBased tables  
**Status**: Partially implemented but incomplete

**Current Code:**
```csharp
else // PageBased
{
    // PageBased: Full table scan not yet implemented
    // Would require iterating all pages - future enhancement
    // For now, return empty results
}
```

**What Works:**
- ✅ Primary key lookups (`WHERE id = 5`)
- ✅ INSERT into PageBased tables
- ❌ Full table scans (`SELECT * FROM table`)
- ❌ WHERE clauses on non-PK columns (`WHERE age > 30`)
- ❌ UPDATE (depends on SELECT)
- ❌ DELETE (depends on SELECT)

**Workaround:** Use Columnar storage instead of PageBased for now

---

### 2. ❌ Benchmark Results: All NA (Not Available)
**File**: `..\SharpCoreDB.Benchmarks\PageBasedStorageBenchmark.cs`  
**Impact**: Cannot measure PageBased storage performance  
**Root Cause**: Benchmarks depend on full table scan (see issue #1)

**Benchmark Output:**
```
| Method                      | Mean | Error | Ratio |
|---------------------------- |-----:|------:|------:|
| Baseline_Delete_20K         |   NA |    NA |     ? |
| Optimized_Delete_20K        |   NA |    NA |     ? |
| Baseline_Select_FullScan    |   NA |    NA |     ? |
| Optimized_Select_FullScan   |   NA |    NA |     ? |
```

**Why:** All benchmark methods call SELECT with WHERE clauses, which returns empty results on PageBased storage.

---

### 3. ✅ FIXED: GroupCommitWAL Single-Threaded Hang
**File**: `Services\GroupCommitWAL.Batching.cs`  
**Impact**: Hang at last record when using GroupCommitWAL with sequential inserts  
**Status**: ✅ FIXED with immediate flush optimization

**Fix Applied:**
```csharp
// Detect low-concurrency scenario
if (batch.Count == 1 && commitQueue.Reader.Count == 0)
{
    break;  // Flush immediately instead of waiting
}
```

---

### 4. ✅ FIXED: FindPageWithSpace Off-By-One Error
**File**: `Storage\PageManager.cs`  
**Impact**: Crash when allocating pages  
**Status**: ✅ FIXED

**Fix Applied:**
```csharp
// BEFORE (bug):
for (ulong i = 1; i <= (ulong)totalPages; i++)

// AFTER (fix):
for (ulong i = 1; i < (ulong)totalPages; i++)
```

---

## What Actually Works

### ✅ Fully Functional Features
1. **Columnar Storage**
   - ✅ INSERT, SELECT, UPDATE, DELETE
   - ✅ Full table scans
   - ✅ Hash indexes
   - ✅ B+ tree primary key index

2. **PageBased Storage** (Partial)
   - ✅ INSERT (single and batch)
   - ✅ Primary key lookups
   - ✅ Page allocation and management
   - ✅ LRU page cache
   - ❌ Full table scan
   - ❌ Non-PK WHERE clauses

3. **GroupCommitWAL**
   - ✅ Batch commits
   - ✅ Immediate flush for low concurrency
   - ✅ Adaptive batch sizing
   - ✅ Single-threaded inserts (after fix)

4. **Database Core**
   - ✅ SQL parser (INSERT, CREATE TABLE, etc.)
   - ✅ Transaction support
   - ✅ Crash recovery
   - ✅ Encryption (AES-256-GCM)

---

## Recommended Actions

### For Production Use:
1. **Use Columnar storage** until PageBased full scan is implemented
2. **Disable GroupCommitWAL** for single-threaded workloads (or use recent fix)
3. **Avoid PageBased benchmarks** until SELECT is complete

### For Development:
1. **Implement PageBased full table scan** (priority #1)
   - Add `GetAllTablePages(tableId)` to PageManager
   - Add `GetAllRecordsInPage(pageId)` to PageManager
   - Update `SelectInternal` to iterate pages

2. **Complete UPDATE/DELETE for PageBased**
   - Both depend on SELECT working first

3. **Re-run benchmarks** after SELECT is fixed

---

## Bug Fixes Applied This Session

1. ✅ **FindPageWithSpace off-by-one** (PageManager.cs)
2. ✅ **GroupCommitWAL immediate flush** (GroupCommitWAL.Batching.cs)
3. ✅ **GroupCommitWAL timeout fix** (Task.WhenAny instead of CancellationToken)
4. ✅ **CREATE TABLE STORAGE = PAGE_BASED** added to benchmark
5. ✅ **Diagnostic logging removed** (production-ready)

---

## Files Modified

| File | Status | Description |
|------|--------|-------------|
| `Storage\PageManager.cs` | ✅ Fixed | Off-by-one bug in FindPageWithSpace |
| `Services\GroupCommitWAL.Batching.cs` | ✅ Fixed | Immediate flush + timeout fix |
| `Core\Database.Core.cs` | ✅ Fixed | Load() before WAL initialization |
| `..\SharpCoreDB.Benchmarks\PageBasedStorageBenchmark.cs` | ⚠️ Incomplete | Benchmarks fail due to missing SELECT |
| `DataStructures\Table.CRUD.cs` | ⚠️ Incomplete | PageBased SELECT not implemented |

---

## Next Steps

**If you want working benchmarks:**
1. Implement PageBased full table scan (30-60 min work)
2. OR switch benchmark to Columnar storage (5 min work)
3. OR create INSERT-only benchmark (10 min work)

**If you want complete PageBased storage:**
1. Implement full scan (requires GetAllTablePages + iteration)
2. Test with unit tests
3. Run benchmarks to validate performance

---

**Current State:** Database is ~80% complete. Core functionality works, but PageBased storage is missing full table scan feature which blocks benchmarks and most queries.
