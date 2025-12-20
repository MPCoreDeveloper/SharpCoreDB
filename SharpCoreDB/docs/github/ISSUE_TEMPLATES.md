# GitHub Issue Templates for Critical Optimizations

## Issue 1: Priority 1 - Fix UPDATE Performance (CRITICAL)

### Title
🔴 CRITICAL: Optimize UPDATE performance (5.3x slower than LiteDB)

### Labels
`priority: critical`, `performance`, `optimization`, `help wanted`

### Description

**Problem**

UPDATE operations are currently **5.3x slower than LiteDB** and **415x slower than SQLite**, making SharpCoreDB unsuitable for update-heavy transactional workloads.

**Current Performance** (10,000 records, 5,000 random updates):
- SQLite: **5.2 ms** ⚡
- LiteDB: **407 ms**
- **SharpCoreDB: 2,172 ms** ❌ (5.3x slower than LiteDB)

**Root Cause**

Each UPDATE currently opens its own transaction with individual:
1. Transaction begin/commit (5K times)
2. Index rebuild (5K times)
3. WAL flush to disk (5K times) ⚠️ **Main culprit**

This results in **5,000 disk syncs** for 5,000 updates.

**Proposed Solution**

Implement **batch update transactions** with deferred index updates:

```csharp
// Proposed API
storage.BeginTransaction();              // 1. Single transaction
table.DeferIndexUpdates(true);           // 2. Queue index updates

foreach (var update in updates) {
    table.Update(row, newValues);        // 3. Update data only
}

table.FlushDeferredIndexUpdates();       // 4. Rebuild indexes once
storage.Commit();                        // 5. Single WAL flush
```

**Expected Performance**

**Target**: <400ms (match/beat LiteDB)
**Expected**: ~200-300ms (7-10x faster than current)

**Breakdown**:
- Single transaction overhead: ~1ms (vs 400ms)
- Bulk index rebuild: ~100ms (vs 600ms)
- Single WAL flush: ~50ms (vs 1,100ms) ✅ **Major win**
- Data writes: ~72ms (unchanged)
- **Total: ~223ms (9.7x improvement)**

**Implementation Plan**

See detailed plan in [docs/optimization/OPTIMIZATION_ROADMAP.md](docs/optimization/OPTIMIZATION_ROADMAP.md#priority-1-fix-update-performance--critical)

**Key Changes**:
1. Add `Table.DeferIndexUpdates(bool)` method
2. Implement deferred index queue
3. Add `Table.FlushDeferredIndexUpdates()` for bulk rebuild
4. Integrate with `ExecuteBatchSQL()`

**Success Criteria**

- ✅ UPDATE 5K records in <400ms
- ✅ All indexes remain consistent
- ✅ Rollback works correctly
- ✅ Memory usage <100MB
- ✅ No data corruption

**Timeline**

**ETA**: 2-3 weeks  
**Effort**: Medium  
**Confidence**: High (90%)

**Related Benchmarks**

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release -- --filter *Update*
```

**Community Help Needed**

- Code review on PR
- Testing on different workloads
- Performance validation
- Documentation review

---

## Issue 2: Priority 2 - Improve SELECT Performance

### Title
🟡 Optimize SELECT full scan performance (2.2x slower than LiteDB)

### Labels
`priority: high`, `performance`, `optimization`, `enhancement`

### Description

**Problem**

SELECT full table scans are **2.2x slower than LiteDB**, impacting query-heavy applications.

**Current Performance** (10,000 records, full scan with WHERE):
- SQLite: **1.3 ms** ⚡
- LiteDB: **14.2 ms**
- **SharpCoreDB: 30.8 ms** ⚠️ (2.2x slower than LiteDB)

**Root Causes**

1. **Dictionary Materialization**: Every row creates `Dictionary<string, object>` (~5-8ms overhead)
2. **Type Conversion**: Deserialize → box → unbox per field (~3-5ms)
3. **No Skip/Take Optimization**: Always reads entire result set
4. **Linear Scan for WHERE**: No B-tree indexes for range queries

**Proposed Solutions**

### Approach 1: Structured Row API (Zero-Allocation)

```csharp
// Current (slow):
List<Dictionary<string, object>> rows = db.ExecuteQuery("SELECT * FROM users");

// Proposed (fast):
IEnumerable<StructRow> rows = db.ExecuteQueryStructured("SELECT * FROM users");

public struct StructRow
{
    private readonly byte[] _data;
    public T GetValue<T>(string column) => Deserialize<T>(_data, column);
}
```

**Savings**: ~6-8ms (reduce materialization)

### Approach 2: B-tree Indexes

```csharp
table.CreateBTreeIndex("age");  // Enable range queries
var users = db.ExecuteQuery("SELECT * FROM users WHERE age BETWEEN 25 AND 35");
// Uses B-tree range scan (O(log n)) instead of full scan (O(n))
```

**Savings**: ~5-10ms for indexed queries

### Approach 3: SIMD WHERE Clause Evaluation

```csharp
if (SimdHelper.IsSimdSupported)
{
    SimdHelper.FilterRows(data, whereClause);  // 4-8x faster filtering
}
```

**Savings**: ~3-5ms for numeric comparisons

**Expected Performance**

**Target**: <15ms (match LiteDB)
**Expected**: ~12-15ms (2x faster)

**Timeline**

**ETA**: 3-4 weeks  
**Effort**: Medium-High  
**Confidence**: Medium (70%)

---

## Issue 3: Priority 3 - Close INSERT Gap to SQLite

### Title
🟢 Optimize INSERT performance (3x slower than SQLite)

### Labels
`priority: medium`, `performance`, `optimization`, `enhancement`

### Description

**Problem**

Bulk INSERT operations are **3x slower than SQLite**, though still **1.5x faster than LiteDB**.

**Current Performance** (10,000 records):
- SQLite: **31 ms** ⚡
- **SharpCoreDB: 91 ms** ⚠️ (3x slower)
- LiteDB: **138 ms** (1.5x slower than SharpCoreDB ✅)

**Root Causes**

1. **GC Pressure**: 54MB allocated per 10K inserts
   - Dictionary allocations: ~30MB
   - Serialization buffers: ~20MB
   - Index structures: ~4MB

2. **WAL Overhead**: Less optimized than SQLite's 20+ year implementation

3. **Serialization Cost**: More CPU cycles than native binary

**Proposed Solutions**

### Approach 1: Optimize StreamingRowEncoder V2

```csharp
// Eliminate ALL Dictionary allocations
public void BulkInsert<T>(IEnumerable<T> rows) where T : struct
{
    using var encoder = new StreamingRowEncoder();
    foreach (var row in rows)
    {
        encoder.EncodeStruct(row);  // ✅ No Dictionary
    }
    storage.WriteBatch(encoder.GetBuffer());  // ✅ Single write
}
```

**Savings**: ~15ms (less GC)

### Approach 2: Adaptive WAL Batching

```csharp
var walConfig = new WalConfig
{
    BatchSize = AdaptiveBatchSize(),  // Tune to workload
    FlushStrategy = FlushStrategy.GroupCommit,
    CompressionEnabled = true  // Reduce I/O
};
```

**Savings**: ~10ms (fewer syncs)

### Approach 3: Memory-Mapped I/O

```csharp
using var mmf = MemoryMappedFile.CreateFromFile(dataFile);
// Zero-copy writes
```

**Savings**: ~6-16ms (OS-dependent)

**Expected Performance**

**Target**: 40-50ms (closer to SQLite)
**Expected**: ~45-50ms (1.8-2x faster)

**Timeline**

**ETA**: 4-6 weeks  
**Effort**: High  
**Confidence**: Medium (60%)

---

## Issue 4: Feature - B-tree Index Implementation

### Title
🔵 Implement B-tree indexes for range queries

### Labels
`feature`, `enhancement`, `indexes`, `help wanted`

### Description

**Goal**

Add B-tree index support to enable efficient range queries and ordered iteration.

**Current Limitations**

- Only hash indexes available (O(1) point lookup, O(n) range)
- No efficient ORDER BY (requires external sort)
- No range queries (BETWEEN, <, >, <=, >=)

**Proposed Implementation**

```csharp
// Create B-tree index
table.CreateBTreeIndex("age");

// Enable efficient queries
var results = db.ExecuteQuery(@"
    SELECT * FROM users 
    WHERE age BETWEEN 25 AND 35 
    ORDER BY age
");
// Uses B-tree range scan + ordered iteration (no sort needed)
```

**Benefits**

- Range queries: O(log n) instead of O(n)
- ORDER BY: No external sort required (5-10x faster)
- JOIN optimization: Index nested loop joins

**Expected Performance**

- Range queries: 5-10x faster
- Ordered queries: 5-10x faster
- Complex JOINs: 3-5x faster

**Timeline**

**ETA**: Q2 2026 (8-10 weeks)  
**Effort**: Very High  
**Confidence**: Medium (65%)

**References**

- B+ tree algorithm: https://en.wikipedia.org/wiki/B%2B_tree
- SQLite B-tree implementation: https://www.sqlite.org/btree.html

---

## Issue 5: Feature - Query Planner/Optimizer

### Title
🔵 Implement cost-based query optimizer

### Labels
`feature`, `enhancement`, `query-optimization`, `help wanted`

### Description

**Goal**

Add cost-based query optimization to automatically select optimal execution plans.

**Current Limitations**

- No query planner (executes queries naively)
- No automatic index selection
- Suboptimal JOIN order
- No predicate pushdown

**Proposed Implementation**

```csharp
// Query optimizer selects best plan
var results = db.ExecuteQuery(@"
    SELECT u.name, o.total
    FROM users u
    JOIN orders o ON u.id = o.user_id
    WHERE u.age > 25 AND o.total > 100
");

// Optimizer:
// 1. Selects best index (age vs user_id)
// 2. Determines join order (users first vs orders first)
// 3. Pushes predicates (filter before join)
// 4. Estimates costs based on statistics
```

**Benefits**

- Automatic index selection
- Optimal JOIN order (3-5x faster)
- Subquery decorrelation (2-4x faster)
- Complex query optimization (5-10x faster)

**Timeline**

**ETA**: Q3 2026 (10-12 weeks)  
**Effort**: Very High  
**Confidence**: Medium (60%)

---

## How to Use These Issues

1. **Copy template content**
2. **Create new GitHub issue**
3. **Paste template**
4. **Add appropriate labels**
5. **Link to milestone** (Q1 2026, Q2 2026, etc.)
6. **Assign to team member** (if applicable)
7. **Add to project board** (Optimization Roadmap)

## Issue Labels

- `priority: critical` - Blocks production use
- `priority: high` - Important for competitiveness
- `priority: medium` - Nice to have
- `performance` - Performance improvement
- `optimization` - Code optimization
- `feature` - New feature
- `enhancement` - Improvement to existing feature
- `help wanted` - Community contributions welcome
- `good first issue` - Entry point for new contributors

## Milestones

- **Q1 2026**: Beat LiteDB (Issues 1-3)
- **Q2 2026**: B-tree Indexes (Issue 4)
- **Q3 2026**: Query Optimizer (Issue 5)

---

**Last Updated**: December 2025
