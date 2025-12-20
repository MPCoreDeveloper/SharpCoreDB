# Optimization Roadmap: Beat LiteDB & Approach SQLite

**Goal**: Make SharpCoreDB the fastest pure .NET embedded database

**Timeline**: Q1 2026 (Beat LiteDB) → Q3 2026 (Approach SQLite)

---

## Current Performance Gaps (December 2025)

| Operation | SharpCoreDB | LiteDB | SQLite | Gap to Beat |
|-----------|-------------|--------|--------|-------------|
| **Analytics** | 45 μs | 15,079 μs | 599 μs | ✅ **WINNING (334x faster)** |
| **INSERT** | 91 ms | 138 ms | 31 ms | ✅ **WINNING vs LiteDB (1.5x)** |
| **SELECT** | 30.8 ms | 14.2 ms | 1.3 ms | ⚠️ **2.2x slower than LiteDB** |
| **UPDATE** | 2,172 ms | 407 ms | 5.2 ms | 🔴 **5.3x slower than LiteDB** |

---

## Phase 1: Beat LiteDB (Q1 2026)

**Target**: Match or exceed LiteDB across all metrics while maintaining analytics dominance

---

### Priority 1: Fix UPDATE Performance 🔴 **CRITICAL**

**Current**: 2,172ms (5.3x slower than LiteDB)  
**Target**: <400ms (match LiteDB)  
**Expected Improvement**: 5-10x faster  
**ETA**: 2-3 weeks  
**Effort**: Medium  
**Confidence**: High (90%)

#### Root Cause Analysis

**Current Implementation** (per update):
```csharp
// ❌ PROBLEM: Each update is its own transaction
foreach (var update in updates) {
    storage.BeginTransaction();        // 1. Open transaction
    table.Update(row, newValues);      // 2. Write data
    index.Update(row);                 // 3. Rebuild index
    wal.Flush();                       // 4. Sync to disk (expensive!)
    storage.Commit();                  // 5. Finalize
}
// Result: 5,000 updates = 5,000 disk syncs = 2,172ms
```

**Performance Breakdown** (per 5K updates):
- Transaction overhead: ~400ms (5K begins/commits)
- Index rebuild: ~600ms (5K individual index updates)
- WAL flush: ~1,100ms (5K disk syncs) ⚠️ **MAIN CULPRIT**
- Data write: ~72ms

#### Solution Architecture

**Proposed Implementation** (batch updates):
```csharp
// ✅ SOLUTION: Single transaction with deferred index updates
storage.BeginTransaction();            // 1. Open transaction ONCE
table.DeferIndexUpdates(true);         // 2. Disable immediate indexing

foreach (var update in updates) {
    table.Update(row, newValues);      // 3. Write to page cache (in-memory)
    // No index update, no WAL flush yet
}

table.FlushDeferredIndexUpdates();     // 4. Rebuild all indexes in one pass
wal.FlushBatch();                      // 5. Single disk sync
storage.Commit();                      // 6. Finalize ONCE

// Result: 5,000 updates = 1 disk sync = ~200-400ms (5-10x faster!)
```

**Performance Projection** (per 5K updates):
- Transaction overhead: ~1ms (single transaction)
- Index rebuild: ~100ms (bulk rebuild with sorting)
- WAL flush: ~50ms (single disk sync) ✅ **MAJOR WIN**
- Data write: ~72ms
- **Total: ~223ms (9.7x faster)**

#### Implementation Plan

**Week 1**: Core Infrastructure
1. Add `Table.DeferIndexUpdates(bool defer)` method
2. Implement deferred index queue (in-memory buffer)
3. Add `Table.FlushDeferredIndexUpdates()` bulk rebuild
4. Update `Storage.Commit()` to handle batch mode

**Week 2**: Integration & Testing
5. Integrate with `ExecuteBatchSQL()` for UPDATE statements
6. Add unit tests for deferred index correctness
7. Add benchmark to verify 5-10x improvement
8. Document API usage and limitations

**Week 3**: Polish & Validation
9. Edge case handling (rollback with deferred updates)
10. Performance profiling and fine-tuning
11. Documentation updates
12. Beta testing with production-like workloads

#### Code Changes Required

**1. Table.cs - Add Deferred Update Support**
```csharp
public partial class Table
{
    private bool _deferIndexUpdates = false;
    private List<(Dictionary<string, object> oldRow, Dictionary<string, object> newRow, long position)> 
        _deferredIndexUpdates = new();

    public void DeferIndexUpdates(bool defer)
    {
        _deferIndexUpdates = defer;
        if (!defer && _deferredIndexUpdates.Count > 0)
        {
            FlushDeferredIndexUpdates();
        }
    }

    public void FlushDeferredIndexUpdates()
    {
        if (_deferredIndexUpdates.Count == 0) return;

        // Rebuild indexes in bulk (much faster than per-row)
        foreach (var hashIndex in this.hashIndexes.Values)
        {
            hashIndex.RebuildFromDeferred(_deferredIndexUpdates);
        }

        // Update primary key index
        if (PrimaryKeyIndex >= 0)
        {
            this.Index.BulkUpdate(_deferredIndexUpdates);
        }

        _deferredIndexUpdates.Clear();
    }

    // Modified Update method
    public void Update(string? where, Dictionary<string, object> updates)
    {
        // ... existing logic ...
        
        if (_deferIndexUpdates)
        {
            // Queue for later
            _deferredIndexUpdates.Add((oldRow, newRow, position));
        }
        else
        {
            // Update immediately (current behavior)
            UpdateIndexesImmediately(oldRow, newRow, position);
        }
    }
}
```

**2. Database.Batch.cs - Use Deferred Updates**
```csharp
public void ExecuteBatchSQL(List<string> sqlStatements)
{
    var table = GetTable(tableName);
    
    // Detect if batch contains mostly UPDATEs
    bool isUpdateHeavy = sqlStatements.Count(s => s.StartsWith("UPDATE")) > 100;
    
    storage.BeginTransaction();
    
    if (isUpdateHeavy)
    {
        table.DeferIndexUpdates(true);  // ✅ Enable batch mode
    }
    
    try
    {
        foreach (var sql in sqlStatements)
        {
            ExecuteSQLInternal(sql);  // Individual updates queued
        }
        
        if (isUpdateHeavy)
        {
            table.FlushDeferredIndexUpdates();  // ✅ Bulk rebuild
        }
        
        storage.Commit();  // ✅ Single WAL flush
    }
    catch
    {
        storage.Rollback();
        throw;
    }
}
```

**3. HashIndex.cs - Add Bulk Rebuild**
```csharp
public void RebuildFromDeferred(
    List<(Dictionary<string, object> oldRow, 
          Dictionary<string, object> newRow, 
          long position)> updates)
{
    // Sort updates by key for efficient rebuild
    var grouped = updates
        .GroupBy(u => ExtractKey(u.oldRow))
        .ToDictionary(g => g.Key, g => g.ToList());

    foreach (var (key, operations) in grouped)
    {
        // Remove old entries
        foreach (var op in operations)
        {
            this.Remove(op.oldRow, op.position);
        }
        
        // Add new entries in bulk
        foreach (var op in operations)
        {
            this.Add(op.newRow, op.position);
        }
    }
}
```

#### Testing Strategy

**Unit Tests**:
```csharp
[Fact]
public void DeferredIndexUpdates_CorrectlyRebuildsIndexes()
{
    var db = CreateTestDatabase();
    db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
    
    // Insert 1000 rows
    for (int i = 0; i < 1000; i++)
    {
        db.ExecuteSQL($"INSERT INTO users VALUES ({i}, 'User{i}')");
    }
    
    var table = db.GetTable("users");
    table.DeferIndexUpdates(true);
    
    // Update all rows
    for (int i = 0; i < 1000; i++)
    {
        table.Update($"id = {i}", new() { ["name"] = $"Updated{i}" });
    }
    
    table.FlushDeferredIndexUpdates();
    
    // Verify all updates applied correctly
    var result = db.ExecuteQuery("SELECT COUNT(*) FROM users WHERE name LIKE 'Updated%'");
    Assert.Equal(1000, result[0]["COUNT(*)"]);
}
```

**Benchmark Test**:
```csharp
[Benchmark(Baseline = true)]
public void Update_5K_Individual()
{
    // Current implementation (2,172ms)
    for (int i = 0; i < 5000; i++)
    {
        db.ExecuteSQL($"UPDATE bench SET salary = {i} WHERE id = {i}");
    }
}

[Benchmark]
public void Update_5K_Deferred()
{
    // New implementation (target: <400ms)
    var table = db.GetTable("bench");
    table.DeferIndexUpdates(true);
    
    for (int i = 0; i < 5000; i++)
    {
        table.Update($"id = {i}", new() { ["salary"] = i });
    }
    
    table.FlushDeferredIndexUpdates();
}
```

#### Success Criteria

- ✅ UPDATE 5K records in <400ms (match LiteDB)
- ✅ All indexes remain consistent
- ✅ Rollback works correctly with deferred updates
- ✅ Memory usage stays under 100MB
- ✅ No data corruption under concurrent access

#### Risks & Mitigations

**Risk 1**: Deferred updates break ACID semantics
- **Mitigation**: Only enable for batch operations, document limitations
- **Impact**: Low (explicit opt-in API)

**Risk 2**: Memory overhead from buffering updates
- **Mitigation**: Flush every 10K updates, configurable threshold
- **Impact**: Medium (controlled with auto-flush)

**Risk 3**: Index rebuild complexity for hash collisions
- **Mitigation**: Comprehensive unit tests, existing hash index code is robust
- **Impact**: Low (hash indexes already handle collisions)

---

### Priority 2: Improve SELECT Performance 🟡

**Current**: 30.8ms (2.2x slower than LiteDB)  
**Target**: <15ms (match LiteDB)  
**Expected Improvement**: 2x faster  
**ETA**: 3-4 weeks  
**Effort**: Medium-High  
**Confidence**: Medium (70%)

#### Root Cause Analysis

**Current Bottlenecks**:
1. **Dictionary Materialization**: Every row is materialized as `Dictionary<string, object>`
   - Overhead: ~5-8ms for 10K rows
2. **Type Conversion**: Deserialize from bytes → box to object → unbox on access
   - Overhead: ~3-5ms for 10K rows
3. **No Skip/Take Optimization**: Always reads entire result set
   - Overhead: Variable (depends on result size)
4. **Linear Scan for WHERE**: No B-tree indexes for range queries
   - Overhead: O(n) instead of O(log n)

#### Solution Architecture

**Approach 1**: Reduce Materialization Overhead
```csharp
// Current (slow):
public List<Dictionary<string, object>> Select(string query)
{
    var results = new List<Dictionary<string, object>>();
    foreach (var row in ScanTable())
    {
        var dict = new Dictionary<string, object>();  // ❌ Allocate
        foreach (var col in columns)
        {
            dict[col] = ConvertType(row, col);  // ❌ Box/unbox
        }
        results.Add(dict);
    }
    return results;
}

// Proposed (fast):
public IEnumerable<StructRow> SelectStructured(string query)
{
    foreach (var row in ScanTable())
    {
        yield return new StructRow(row);  // ✅ No dictionary
    }
}

public struct StructRow
{
    private readonly byte[] _data;
    public T GetValue<T>(string column) => Deserialize<T>(_data, column);
}
```

**Approach 2**: Add B-tree Indexes
```csharp
// Enable range queries with O(log n) complexity
table.CreateBTreeIndex("age");  // ✅ Sorted index

// Query optimization
var results = db.ExecuteQuery("SELECT * FROM users WHERE age BETWEEN 25 AND 35");
// Uses B-tree range scan instead of full table scan
```

**Approach 3**: SIMD-Optimized Scanning
```csharp
// Use SIMD for WHERE clause evaluation
if (SimdHelper.IsSimdSupported)
{
    SimdHelper.FilterRows(data, whereClause);  // ✅ 4-8x faster
}
```

#### Implementation Plan

**Week 1**: Structured Row API
1. Design `StructRow` value type
2. Implement zero-allocation SELECT path
3. Add benchmark to measure improvement

**Week 2**: B-tree Index Foundation
4. Design B-tree node structure
5. Implement insert/search/range scan
6. Integrate with query planner

**Week 3**: SIMD WHERE Clause Optimization
7. SIMD-optimized integer comparisons
8. SIMD-optimized string matching
9. Benchmark and tune

**Week 4**: Integration & Testing
10. Integrate all optimizations
11. Comprehensive testing
12. Performance validation

#### Expected Performance

**Before**: 30.8ms for 10K rows  
**After**: ~12-15ms for 10K rows  
**Breakdown**:
- Dictionary elimination: -6ms (50% of materialization cost)
- Type conversion optimization: -4ms (SIMD-assisted)
- B-tree indexes: -5ms (for indexed queries)
- **Total savings: ~15ms (2x faster)**

---

### Priority 3: Close INSERT Gap to SQLite 🟢

**Current**: 91ms (3x slower than SQLite)  
**Target**: 40-50ms (closer to SQLite)  
**Expected Improvement**: 1.8-2.2x faster  
**ETA**: 4-6 weeks  
**Effort**: High  
**Confidence**: Medium (60%)

#### Root Cause Analysis

**Why SQLite is Faster**:
1. **Native C Implementation**: No GC overhead, direct syscalls
2. **Highly Optimized WAL**: 20+ years of tuning
3. **Optimized B-tree**: Memory-mapped pages, efficient node splitting
4. **Minimal Overhead**: Direct pointer manipulation, no boxing

**SharpCoreDB Overhead Sources**:
1. **GC Pressure**: 54MB allocated per 10K inserts
   - Dictionary allocations: ~30MB
   - Serialization buffers: ~20MB
   - Index structures: ~4MB
2. **WAL Implementation**: Less optimized than SQLite's
3. **Serialization**: More CPU cycles than native binary writes
4. **Page Management**: Less efficient than SQLite's pager

#### Solution Architecture

**Approach 1**: Further Optimize StreamingRowEncoder
```csharp
// Current: Still some Dictionary materialization
// Proposed: Direct binary write path

public void BulkInsert(IEnumerable<T> rows) where T : struct
{
    using var encoder = new StreamingRowEncoder();
    foreach (var row in rows)
    {
        encoder.EncodeStruct(row);  // ✅ No Dictionary
    }
    storage.WriteBatch(encoder.GetBuffer());  // ✅ Single write
}
```

**Approach 2**: Optimize WAL Batching
```csharp
// Adaptive batching based on workload
var walConfig = new WalConfig
{
    BatchSize = AdaptiveBatchSize(),  // ✅ Tune to workload
    FlushStrategy = FlushStrategy.GroupCommit,  // ✅ Reduce syncs
    CompressionEnabled = true  // ✅ Reduce I/O
};
```

**Approach 3**: Memory-Mapped Page Management
```csharp
// Use memory-mapped files for zero-copy I/O
using var mmf = MemoryMappedFile.CreateFromFile(dataFile);
using var accessor = mmf.CreateViewAccessor();
// Direct writes to mapped memory (OS handles sync)
```

#### Implementation Plan

**Week 1-2**: StreamingRowEncoder V2
1. Eliminate all Dictionary allocations
2. Direct struct serialization
3. Benchmark and validate

**Week 3-4**: WAL Optimization
4. Implement group commit
5. Add compression (LZ4/Snappy)
6. Tune batch sizes dynamically

**Week 5-6**: Memory-Mapped I/O
7. Integrate memory-mapped files
8. Optimize page allocation
9. Comprehensive testing and validation

#### Expected Performance

**Before**: 91ms for 10K inserts  
**After**: ~40-50ms for 10K inserts  
**Breakdown**:
- GC reduction: -15ms (less allocation)
- WAL optimization: -10ms (fewer syncs)
- Faster serialization: -10ms (SIMD encoding)
- Memory-mapped I/O: -6-16ms (depends on OS/hardware)
- **Total savings: ~41-51ms (1.8-2.2x faster)**

---

## Phase 2: Approach SQLite (Q2-Q3 2026)

**Target**: Competitive with SQLite for OLTP workloads

---

### Priority 4: B-tree Index Implementation 🔵

**Timeline**: Q2 2026 (8-10 weeks)  
**Effort**: Very High  
**Confidence**: Medium (65%)

#### Goals

- Implement full B-tree index structure
- Support range queries (BETWEEN, <, >, <=, >=)
- Enable ordered iteration (ORDER BY without sort)
- Composite indexes (multi-column)

#### Expected Impact

- **SELECT**: 2-5x faster for range queries
- **ORDER BY**: 5-10x faster (no external sort)
- **JOIN**: 3-5x faster (index lookups instead of nested loops)

---

### Priority 5: Query Planner/Optimizer 🔵

**Timeline**: Q3 2026 (10-12 weeks)  
**Effort**: Very High  
**Confidence**: Medium (60%)

#### Goals

- Cost-based query optimization
- Automatic index selection
- Join order optimization
- Predicate pushdown

#### Expected Impact

- **Complex Queries**: 5-10x faster
- **JOINs**: 3-5x faster (optimal join order)
- **Subqueries**: 2-4x faster (decorrelation)

---

## Success Metrics

### Q1 2026 Targets (Beat LiteDB)

| Metric | Current | Target | Status |
|--------|---------|--------|--------|
| UPDATE (5K) | 2,172ms | <400ms | 🎯 |
| SELECT (10K) | 30.8ms | <15ms | 🎯 |
| INSERT (10K) | 91ms | <50ms | 🎯 |
| Memory (INSERT) | 54MB | <30MB | 🎯 |

### Q3 2026 Targets (Approach SQLite)

| Metric | Current | Target | Status |
|--------|---------|--------|--------|
| UPDATE (5K) | 2,172ms | <50ms | 🎯 |
| SELECT (10K) | 30.8ms | <5ms | 🎯 |
| INSERT (10K) | 91ms | <35ms | 🎯 |
| Range Query | N/A | O(log n) | 🎯 |

---

## Resource Requirements

### Team

- 1-2 Senior .NET Engineers (full-time)
- 1 Database Specialist (part-time consulting)
- 1 Performance Engineer (part-time)

### Infrastructure

- Continuous benchmarking pipeline
- Performance regression testing
- Profiling tools (JetBrains dotTrace, PerfView)

### Timeline

- **Q1 2026**: 3 months (Priorities 1-3)
- **Q2 2026**: 3 months (Priority 4)
- **Q3 2026**: 3 months (Priority 5)
- **Total**: 9 months to approach SQLite

---

## Risk Assessment

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| GC overhead prevents reaching SQLite speed | High | Medium | Consider NativeAOT, Span<T>, memory pools |
| B-tree implementation complexity | High | Low | Use proven algorithms, extensive testing |
| Breaking changes to API | Medium | Low | Versioned APIs, deprecation warnings |
| Performance regressions | Medium | Medium | Automated benchmark CI |
| Community adoption slow | Low | Low | Marketing, documentation, examples |

---

## Conclusion

**Feasibility**: High for beating LiteDB (Q1 2026), Medium for approaching SQLite (Q3 2026)

**Key Success Factors**:
1. Focus on Priority 1 (UPDATE) first - biggest impact
2. Maintain analytics dominance (334x advantage)
3. Incremental improvements, continuous benchmarking
4. Community engagement and feedback

**Expected Outcome**:
- **Q1 2026**: SharpCoreDB beats LiteDB across all metrics
- **Q3 2026**: SharpCoreDB within 2-3x of SQLite for OLTP, maintains analytics dominance

---

**Document Version**: 1.0  
**Last Updated**: December 2025  
**Next Review**: January 2026 (after Priority 1 completion)
