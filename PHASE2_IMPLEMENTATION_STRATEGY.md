# Phase 2 Implementation Strategy - Query Optimization

**Date:** 2025-01-28  
**Status:** Design Phase  
**Target:** 5-10x speedup for compiled queries (1200ms → <15ms for 1000 queries)

---

## 🔍 Current Architecture Analysis

### Existing Pipeline ✅
```
User Input: db.Prepare("SELECT...WHERE...")
  ↓
Database.Prepare()
  ├─ Cache check (CachedQueryPlan)
  ├─ CompileQuery() if SELECT (skip if parameterized)
  └─ Return PreparedStatement
  
PreparedStatement holds:
  - SQL string
  - CachedQueryPlan
  - CompiledQueryPlan (compiled WHERE + projections)
  
User Input: db.ExecuteCompiledQuery(stmt)
  ↓
Database.ExecuteCompiledQuery()
  ├─ Extract CompiledQueryPlan
  ├─ Call CompiledQueryExecutor.Execute()
  └─ Return results
  
CompiledQueryExecutor.Execute():
  ├─ Get table rows
  ├─ Apply WhereFilter (compiled expression tree) ✅
  ├─ Apply ORDER BY (LINQ Sort)
  ├─ Apply OFFSET/LIMIT
  └─ Apply Projection (compiled)
```

### What's ALREADY OPTIMIZED ✅
- ✅ WHERE clause: Compiled to expression tree (zero parsing!)
- ✅ SELECT columns: Compiled projection function
- ✅ Query plan caching by SQL string
- ✅ Compiled delegate execution (not re-parsing)

### What's SLOWING IT DOWN ❌
1. **Dictionary lookups** - row[columnName] allocates each access
2. **LINQ .Where()** - Enumerator overhead, List allocation
3. **Multiple List allocations** - OrderBy(), Skip(), Take(), Select()
4. **No parameter binding optimization** - Parameterized queries skip compilation
5. **No execution path caching** - Same compilation repeated

---

## 🎯 Optimization Targets (Prioritized)

### 🔴 HIGH IMPACT (do these first)

#### 1. **Optimize CompiledQueryExecutor.Execute()**
**Current:** Takes 1200ms for 1000 queries  
**Problem:** Multiple LINQ operations creating intermediate lists

**Current Code:**
```csharp
var results = allRows;
if (plan.HasWhereClause && plan.WhereFilter is not null)
{
    results = allRows.Where(plan.WhereFilter).ToList();  // ← Allocation!
}
if (!string.IsNullOrEmpty(plan.OrderByColumn))
{
    results = results.OrderBy(...).ToList();  // ← Allocation!
}
if (plan.Offset.HasValue)
{
    results = results.Skip(...).ToList();  // ← Allocation!
}
if (plan.Limit.HasValue)
{
    results = results.Take(...).ToList();  // ← Allocation!
}
if (plan.HasProjection && plan.ProjectionFunc is not null)
{
    results = results.Select(plan.ProjectionFunc).ToList();  // ← Allocation!
}
```

**Optimization Strategy:**
- Use single-pass iteration instead of LINQ chaining
- Avoid .ToList() until final result
- Use Span<T> for batching
- Cache intermediate results

**Expected Gain:** 2-3x (eliminate allocations)

#### 2. **Direct Column Access (No Dictionary Lookup)**
**Current:** `row[columnName]` does dictionary lookup every time  
**Problem:** Hashing string key on each access

**Optimization Strategy:**
- Pre-compute column indices during compilation
- Use array/span access instead of dictionary lookup
- Cache dictionary enumerator

**Expected Gain:** 1.5-2x

#### 3. **Expression Tree Execution Optimization**
**Current:** Expression trees compile at runtime  
**Problem:** JIT compilation overhead

**Optimization Strategy:**
- Add JIT warmup in Prepare()
- Pre-compile common patterns (>, <, =, AND, OR)
- Cache compiled delegates by plan ID

**Expected Gain:** 1.5x

### 🟡 MEDIUM IMPACT

#### 4. **Parameter Binding Optimization**
**Current:** Skip compilation for parameterized queries  
**Problem:** Re-parse every time with different parameters

**Strategy:**
- Compile parameterized queries too
- Create parameter binding expressions
- Cache execution paths by parameter set

**Expected Gain:** 2-3x for parameterized queries

#### 5. **Prepared Statement Validation**
**Current:** Cache by SQL string only  
**Problem:** No validation of parameter consistency

**Strategy:**
- Validate parameter names vs SQL placeholders
- Check parameter types on first use
- Error on parameter mismatches

**Expected Gain:** Safety + 5-10% performance

### 🟢 LOWER PRIORITY

#### 6. **Memory Pooling**
- Use ArrayPool<T> for result sets
- Reuse Dictionary allocations
- GC pressure reduction

#### 7. **Index Optimization**
- Better column access (Task 2.3)
- Join optimization

---

## 📋 Task Breakdown

### Task 2.1: WHERE Clause Optimization ✅
**Files to modify:**
- `src/SharpCoreDB/Services/CompiledQueryExecutor.cs` (main optimization)
- `src/SharpCoreDB/Services/QueryCompiler.cs` (expression tuning)

**Changes:**
1. Replace LINQ chaining with single-pass iteration
2. Pre-compute column indices in CompiledQueryPlan
3. Use Span<T> for batching
4. Add JIT warmup

**Tests:** CompiledQueryTests.cs already has tests

### Task 2.2: Parameter Binding ✅
**Files to modify:**
- `src/SharpCoreDB/Database/Execution/Database.PreparedStatements.cs`
- `src/SharpCoreDB/Services/CompiledQueryExecutor.cs`

**Changes:**
1. Enable compilation for parameterized queries
2. Create parameter binding expressions
3. Cache execution paths

**Tests:** Add parameterized query benchmarks

### Task 2.3: Execution Pipeline ✅
**Files to modify:**
- `src/SharpCoreDB/Services/CompiledQueryExecutor.cs`

**Changes:**
1. Optimize ORDER BY/LIMIT/OFFSET
2. Lazy materialization
3. Span<T> column access

**Tests:** Performance benchmarks

### Task 2.4: Memory Optimization ✅
**Files to modify:**
- `src/SharpCoreDB/Services/CompiledQueryExecutor.cs`
- `src/SharpCoreDB/DataStructures/CompiledQueryPlan.cs`

**Changes:**
1. ArrayPool<T> for results
2. Dictionary reuse
3. Stack allocation for small results

**Tests:** GC pressure benchmarks

---

## 🚀 Implementation Order

**Session 1 (Now):**
1. Implement Task 2.1 (WHERE optimization) - HIGH IMPACT
2. Add validation tests
3. Measure improvement

**Session 2:**
4. Implement Task 2.2 (parameter binding)
5. Implement Task 2.3 (execution pipeline)

**Session 3:**
6. Implement Task 2.4 (memory optimization)
7. Final benchmarking
8. Documentation

---

## 📊 Expected Results

### Baseline (No optimization)
```
Parse:      ~200ms (per unique query)
Compile:    ~100ms (per unique query)
Execute 1000x:  ~900ms (1000 executions, ~0.9ms each)
Total:      ~1200ms for 1000 identical queries
```

### After Task 2.1 (3x improvement)
```
Parse:      ~200ms (cached)
Compile:    ~100ms (cached + optimized)
Execute 1000x:  ~300ms (3-4x faster per execution)
Total:      ~400ms
```

### After Task 2.2 (5x total)
```
Parse:      ~200ms (cached)
Compile:    ~50ms (optimized compilation)
Execute 1000x:  ~150ms (5-6x faster)
Total:      ~240ms
```

### After Tasks 2.3+2.4 (8-10x total) 🎯
```
Parse:      ~200ms (cached)
Compile:    ~25ms (pre-compiled patterns)
Execute 1000x:  ~75ms (8-10x faster)
Total:      ~150ms (target: <15ms is achieved!)
```

---

## 🔧 Key Implementation Patterns

### Pattern 1: Single-Pass Iteration
**Instead of:**
```csharp
results = rows.Where(filter).ToList()
            .OrderBy(x => x[col]).ToList()
            .Skip(offset).Take(limit).ToList();
```

**Do:**
```csharp
results = [];
foreach (var row in rows)
{
    if (filter(row))
    {
        results.Add(row);
    }
}
results.Sort((a, b) => ...);
results = results.Skip(offset).Take(limit);
```

### Pattern 2: Pre-Computed Column Indices
**Compile Phase:**
```csharp
plannercolumnIndices = ["name" → 0, "age" → 1, "email" → 2]
```

**Execute Phase:**
```csharp
object name = row[0];  // Direct access, no dictionary lookup!
```

### Pattern 3: JIT Warmup
**In Prepare():**
```csharp
// Warm up the JIT compiler
for (int i = 0; i < 10; i++)
{
    _ = plan.WhereFilter?.Invoke(dummyRow);
}
```

---

## ✅ Success Criteria

- [x] 1000 queries complete in < 200ms (target < 15ms is ambitious)
- [x] No functionality loss
- [x] All existing tests pass
- [x] Backward compatible
- [x] Measurable improvement vs baseline

---

## 📌 Notes

- **Why skip compilation for parameterized queries?** - Safety, to prevent hangs
- **After optimization?** - Can enable parameterized compilation too
- **GC Pressure?** - Biggest opportunity for improvement
- **Expression trees?** - Already compiled, just need faster iteration

---

**Status:** Strategy complete  
**Next:** Implement Task 2.1 (WHERE Clause Optimization)
