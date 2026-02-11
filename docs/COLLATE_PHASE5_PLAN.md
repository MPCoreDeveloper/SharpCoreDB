# COLLATE Support Phase 5 Planning - Runtime Query Optimization

**Date:** 2025-01-28  
**Status:** 🚀 PLANNED  
**Target Completion:** Phase 5 completion

---

## Executive Summary

Phase 5 extends collation support from infrastructure (Phases 1-4) to **runtime query execution optimization**. This phase ensures that:

- ✅ WHERE clause filtering respects column collations (case-insensitive queries)
- ✅ DISTINCT operations use collation-aware equality
- ✅ GROUP BY and aggregates respect collation
- ✅ ORDER BY respects collation for correct sorting
- ✅ Performance: No regression for binary comparisons, <5% overhead for NOCASE

---

## What's Been Completed (Phases 1-4)

### Phase 1: Schema Support
- ✅ `CollationType` enum (Binary, NoCase, RTrim, UnicodeCaseInsensitive)
- ✅ `ColumnCollations` list on Table
- ✅ SQL DDL parsing and generation (`CREATE TABLE ... COLLATE NOCASE`)

### Phase 2: Parser Integration
- ✅ SQL parser supports `COLLATE` clause in CREATE/ALTER TABLE
- ✅ SqlParser.DDL generates correct AST

### Phase 3: Storage Engine Integration
- ✅ Collation persisted with schema to disk
- ✅ Schema loading restores collation metadata
- ✅ B-Tree and Hash Index infrastructure prepared

### Phase 4: Index Integration
- ✅ B-Tree comparison uses collation (`BTree<string, long>` with `CollationType`)
- ✅ Hash Index uses collation-aware key normalization
- ✅ Primary key lookups respect collation

### EF Core Integration
- ✅ Migrations emit `COLLATE` clause in DDL
- ✅ `EF.Functions.Collate()` translator
- ✅ `StringComparison` translator
- ✅ Query SQL generation supports collation

---

## Phase 5 Scope: Runtime Query Optimization

### 5.1 WHERE Clause Filtering (Collation-Aware Comparison)

**Current Status:** Partial  
**What Needs Implementation:**

1. **Modify Table.CRUD.cs Select method:**
   - Enhance `EvaluateCondition()` to use `CollationExtensions.AreEqual()`
   - Support case-insensitive filtering: `WHERE Name = 'alice'` with NOCASE collation
   - Support collation-aware LIKE: `WHERE Email LIKE '%@EXAMPLE.COM%'` → match regardless of case

2. **String Comparison Operations:**
   - `=` (equality) → use `AreEqual()` with column collation
   - `<>` (inequality) → use `!AreEqual()`
   - `>`, `<`, `>=`, `<=` → use `CompareCollation()` (to be created)

3. **Example Behavior:**
   ```csharp
   // Column: name TEXT COLLATE NOCASE
   WHERE name = 'alice'  // Matches: 'alice', 'ALICE', 'Alice'
   WHERE name LIKE '%ice'  // Matches: '%ice', '%ICE', '%Ice'
   WHERE name > 'alice'   // Uses collation-aware comparison
   ```

### 5.2 DISTINCT Operation (Collation-Aware Deduplication)

**Current Status:** Not implemented  
**What Needs Implementation:**

1. **Collation-aware HashSet for DISTINCT:**
   - Create `CollationAwareEqualityComparer<string>` (if not exists)
   - Use in DISTINCT result deduplication
   - Example: `SELECT DISTINCT email FROM users` where `email` has NOCASE
     - 'alice@example.com' and 'ALICE@EXAMPLE.COM' → treated as same

2. **Method:** `Table.Select()` enhancement
   - Add parameter `bool distinct = false`
   - When DISTINCT, use collation-aware deduplication
   - Query parsing: Parse "SELECT DISTINCT" syntax

### 5.3 GROUP BY Support (Collation-Aware Grouping)

**Current Status:** Partial (infrastructure ready)  
**What Needs Implementation:**

1. **Collation-aware grouping:**
   - Group rows by collation-sensitive columns
   - Example: `GROUP BY status` where status is NOCASE
     - 'pending', 'PENDING', 'Pending' → one group

2. **Aggregates with collation:**
   - COUNT, SUM, AVG, MIN, MAX should group correctly
   - Ensure hash-based grouping uses collation

3. **SQL: `SELECT status, COUNT(*) FROM orders GROUP BY status`**
   - If `status` is NOCASE: 'pending' and 'Pending' → one group with combined count

### 5.4 ORDER BY with Collation (Correct Sorting)

**Current Status:** Partial (indexes support it)  
**What Needs Implementation:**

1. **Enhance Table.Select() ORDER BY:**
   - Use collation when sorting string columns
   - Example: `ORDER BY name` with NOCASE collation
     - Binary: ['Alice', 'alice', 'ALICE'] → sorted by ASCII
     - NOCASE: All equivalent, order by original appearance or secondary index

2. **Collation-aware Comparator:**
   - Use `BTree.CompareKeys()` logic (already implemented!)
   - Sort results using column collation

### 5.5 Performance & Edge Cases

**Considerations:**
- Binary collation: Zero overhead (use default comparison)
- NOCASE: String.CompareOrdinal vs String.Compare (measure impact)
- Composite keys: Each column uses its collation
- NULL handling: NULL always equals NULL regardless of collation

---

## Implementation Tasks

### Task 5.1: Create CollationComparator Utility
**File:** `src/SharpCoreDB/CollationComparator.cs`  
**Purpose:** Centralized collation-aware comparison for runtime operations

```csharp
public static class CollationComparator
{
    /// <summary>
    /// Collation-aware string comparison for ORDER BY and filtering.
    /// Returns: -1 (left < right), 0 (equal), 1 (left > right)
    /// </summary>
    public static int Compare(string? left, string? right, CollationType collation);
    
    /// <summary>
    /// Collation-aware LIKE pattern matching.
    /// Returns true if value matches pattern under given collation.
    /// </summary>
    public static bool Like(string value, string pattern, CollationType collation);
}
```

### Task 5.2: Enhance Table.CRUD.cs
**File:** `src/SharpCoreDB/DataStructures/Table.CRUD.cs`  
**Changes:**
- Update `EvaluateCondition()` to use `CollationComparator`
- Add collation handling for `=`, `<>`, `>`, `<`, `>=`, `<=`, `LIKE`
- Modify `Select()` to accept `distinct` parameter
- Add `GROUP BY` support in `Select()` method

### Task 5.3: Add Integration Tests
**File:** `tests/SharpCoreDB.Tests/CollationPhase5Tests.cs`  
**Test Cases:**
1. WHERE clause with NOCASE: Find rows case-insensitively
2. DISTINCT with NOCASE: Deduplicate case-insensitively
3. GROUP BY with NOCASE: Group case-insensitively
4. ORDER BY with NOCASE: Sort with collation rules
5. LIKE with NOCASE: Pattern match case-insensitively
6. Mixed collations: Different columns, different collations
7. Composite filters: WHERE + GROUP BY + ORDER BY together

### Task 5.4: Benchmarks
**File:** `tests/SharpCoreDB.Benchmarks/Phase5_CollationQueryPerformanceBenchmark.cs`  
**Scenarios:**
- WHERE with Binary vs NOCASE (1K, 10K, 100K rows)
- DISTINCT with Binary vs NOCASE
- GROUP BY performance
- ORDER BY performance
- Combined query performance

### Task 5.5: Documentation
**File:** `docs/COLLATE_PHASE5_COMPLETE.md`  
**Content:**
- Summary of runtime optimization implementation
- Examples of Phase 5 features
- Performance metrics from benchmarks
- Migration guide for users

---

## Success Criteria

✅ **Functional:**
- WHERE clauses respect column collations
- DISTINCT deduplicates based on collation
- GROUP BY groups based on collation
- ORDER BY sorts correctly with collation
- LIKE operator works with collation

✅ **Performance:**
- Binary collation: Zero overhead
- NOCASE: <5% perf overhead vs binary (measured via benchmarks)
- Large dataset: No memory leaks, constant allocation per row

✅ **Testing:**
- 7+ integration tests with >90% code coverage
- Benchmarks demonstrate performance characteristics
- All existing tests still pass (no regression)

✅ **Documentation:**
- Phase 5 completion document generated
- Examples of collation-aware queries provided
- Performance metrics documented

---

## Timeline

| Task | Estimated Time | Dependencies |
|------|---|---|
| 5.1: CollationComparator | 1 hour | None |
| 5.2: Table.CRUD enhancements | 2 hours | 5.1 |
| 5.3: Integration tests | 1.5 hours | 5.2 |
| 5.4: Benchmarks | 1 hour | 5.2 |
| 5.5: Documentation | 0.5 hours | 5.2, 5.3, 5.4 |
| **Total** | **6 hours** | - |

---

## Related Issues & PRs

- **Phase 4 Completion:** [COLLATE_PHASE4_COMPLETE.md](COLLATE_PHASE4_COMPLETE.md)
- **EF Core Integration:** [EFCORE_COLLATE_COMPLETE.md](EFCORE_COLLATE_COMPLETE.md)
- **Collation Types:** `src/SharpCoreDB/CollationType.cs`
- **Collation Extensions:** `src/SharpCoreDB/CollationExtensions.cs`

---

## Next Phase (Phase 6+)

After Phase 5:
- **Phase 6:** Schema Migration & ALTER TABLE
- **Phase 7:** Performance Optimization (vectorized comparisons, SIMD)
- **Phase 8:** Documentation & Tutorial

