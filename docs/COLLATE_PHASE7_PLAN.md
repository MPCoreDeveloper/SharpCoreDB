# COLLATE Support Phase 7 Planning - JOIN Operations with Collation Awareness

**Date:** 2025-01-28  
**Status:** 🚀 PLANNED  
**Target Completion:** Phase 7 completion  
**Priority:** High (Performance-Critical)

---

## Executive Summary

Phase 7 extends collation support to **JOIN operations**, enabling correct and efficient multi-table queries with case-insensitive or custom collations. This phase ensures JOIN keys respect column collations, preventing incorrect matches and result set corruption.

### Key Objectives

1. **Collation-Aware JOIN Key Comparison** - Respect column collations when matching rows
2. **Hash JOIN Optimization** - Normalize hash keys using collation rules
3. **Nested Loop JOIN Support** - Use collation-aware comparisons in loops
4. **Merge JOIN Support** - Sort inputs using collation-aware ordering
5. **Collation Resolution** - Determine JOIN collation from columns or explicit COLLATE
6. **Performance Optimization** - Maintain high JOIN performance with collations
7. **Comprehensive Testing** - Cover all JOIN types and collation combinations

---

## What's Been Completed (Phases 1-6)

### Phases 1-4: Foundation & Infrastructure
- ✅ Schema support (`CollationType` enum, `ColumnCollations`)
- ✅ Parser integration (SQL DDL parsing with COLLATE clause)
- ✅ Storage layer (Collation persistence)
- ✅ Index integration (B-Tree and Hash indexes with collation)

### EF Core Integration
- ✅ Migrations support (COLLATE in DDL)
- ✅ Query translation (`EF.Functions.Collate()`)
- ✅ StringComparison support

### Phase 5: Runtime Optimization
- ✅ WHERE clause filtering with collation
- ✅ DISTINCT deduplication
- ✅ GROUP BY grouping
- ✅ ORDER BY sorting
- ✅ LIKE pattern matching

### Phase 6: Schema Migration
- ✅ ALTER TABLE MODIFY COLUMN for collation changes
- ✅ Validation framework
- ✅ Deduplication strategies
- ✅ Migration planning utilities

---

## Problem Statement

### Current JOIN Behavior (WITHOUT Collation Awareness)

```sql
-- Table: users (email TEXT COLLATE NOCASE)
INSERT INTO users VALUES (1, 'alice@example.com');

-- Table: orders (user_email TEXT COLLATE BINARY)  -- ⚠️ Different collation!
INSERT INTO orders VALUES (1, 'ALICE@EXAMPLE.COM', 100.00);

-- Query
SELECT u.*, o.* FROM users u
JOIN orders o ON u.email = o.user_email;

-- ❌ PROBLEM: No rows returned! 
-- Binary comparison: 'alice@example.com' != 'ALICE@EXAMPLE.COM'
```

### Expected Behavior (WITH Collation Awareness)

```sql
-- Same tables, same query
SELECT u.*, o.* FROM users u
JOIN orders o ON u.email = o.user_email;

-- ✅ CORRECT: Resolves collation conflict
-- Option 1: Use left table collation (NOCASE)
-- Option 2: Use explicit COLLATE in JOIN condition
-- Result: Match found, returns 1 row
```

---

## Collation Resolution Rules for JOINs

When joining two tables on string columns, the collation to use must be determined:

### Rule 1: Explicit COLLATE (Highest Priority)
```sql
SELECT * FROM t1
JOIN t2 ON t1.col COLLATE NOCASE = t2.col;
-- Use: NOCASE (explicit override)
```

### Rule 2: Both Columns Same Collation
```sql
-- t1.name: NOCASE, t2.name: NOCASE
SELECT * FROM t1 JOIN t2 ON t1.name = t2.name;
-- Use: NOCASE (no conflict)
```

### Rule 3: Left Column Collation (Default)
```sql
-- t1.name: NOCASE, t2.name: BINARY
SELECT * FROM t1 JOIN t2 ON t1.name = t2.name;
-- Use: NOCASE (left table wins)
-- ⚠️ Log warning: "Collation mismatch in JOIN"
```

### Rule 4: Collation Mismatch Error (Strict Mode - Future)
```sql
-- With PRAGMA strict_collation_checking=ON
SELECT * FROM t1 JOIN t2 ON t1.name = t2.name;
-- ❌ Error: "Cannot join NOCASE column with BINARY column without explicit COLLATE"
```

---

## JOIN Strategy Analysis

SharpCoreDB uses three JOIN strategies:

### 1. Hash JOIN (Most Common - Optimized)
**Current Implementation:**
- Build phase: Create `Dictionary<object, List<row>>` from right table
- Probe phase: Lookup left table keys in dictionary
- **Problem:** Uses default `object.Equals()` → case-sensitive for strings

**Solution:**
- Use `CollationAwareEqualityComparer` from Phase 5
- Normalize hash keys using `CollationComparator.GetHashCode()`
- Apply collation during both build and probe phases

**Example:**
```csharp
// Build hash table with collation
var collation = ResolveJoinCollation(leftColumn, rightColumn);
var comparer = new CollationAwareEqualityComparer(collation);
var hashTable = new Dictionary<string, List<Dictionary<string, object>>>(comparer);

// Build phase
foreach (var rightRow in rightTable)
{
    var key = rightRow[rightColumn]?.ToString() ?? "";
    if (!hashTable.ContainsKey(key))
        hashTable[key] = new List<Dictionary<string, object>>();
    hashTable[key].Add(rightRow);
}

// Probe phase
foreach (var leftRow in leftTable)
{
    var key = leftRow[leftColumn]?.ToString() ?? "";
    if (hashTable.TryGetValue(key, out var matches))
    {
        foreach (var rightRow in matches)
            yield return MergeRows(leftRow, rightRow);
    }
}
```

### 2. Nested Loop JOIN (Small Tables)
**Current Implementation:**
- Outer loop over left table
- Inner loop over right table
- Evaluate JOIN condition for each pair

**Solution:**
- Use `CollationComparator.Equals()` for string comparisons
- Apply collation resolution rules
- Optimize with early exit when possible

**Example:**
```csharp
var collation = ResolveJoinCollation(leftColumn, rightColumn);

foreach (var leftRow in leftTable)
{
    var leftValue = leftRow[leftColumn]?.ToString();
    
    foreach (var rightRow in rightTable)
    {
        var rightValue = rightRow[rightColumn]?.ToString();
        
        if (leftValue != null && rightValue != null &&
            CollationComparator.Equals(leftValue, rightValue, collation))
        {
            yield return MergeRows(leftRow, rightRow);
        }
    }
}
```

### 3. Merge JOIN (Sorted Inputs)
**Current Implementation:**
- Sort both tables by JOIN key
- Merge sorted streams
- **Problem:** Sorting doesn't respect collation

**Solution:**
- Sort inputs using `CollationComparator.Compare()`
- Merge with collation-aware comparison
- Maintain sorted order invariant

**Example:**
```csharp
var collation = ResolveJoinCollation(leftColumn, rightColumn);

// Sort with collation
var sortedLeft = leftTable
    .OrderBy(r => r[leftColumn]?.ToString(), 
        CollationComparator.GetComparer(collation))
    .ToList();

var sortedRight = rightTable
    .OrderBy(r => r[rightColumn]?.ToString(),
        CollationComparator.GetComparer(collation))
    .ToList();

// Merge with collation-aware comparison
// (Similar to standard merge algorithm but uses CollationComparator.Compare())
```

---

## Implementation Tasks

### Task 1: Add Collation Resolution Utility

**File:** `src/SharpCoreDB/CollationComparator.cs` (extend existing)

```csharp
/// <summary>
/// Resolves the collation to use for JOIN operations.
/// </summary>
/// <param name="leftCollation">Left column collation.</param>
/// <param name="rightCollation">Right column collation.</param>
/// <param name="explicitCollation">Explicit COLLATE override (if any).</param>
/// <param name="warningCallback">Callback for collation mismatch warnings.</param>
/// <returns>The resolved collation type.</returns>
public static CollationType ResolveJoinCollation(
    CollationType leftCollation,
    CollationType rightCollation,
    CollationType? explicitCollation = null,
    Action<string>? warningCallback = null)
{
    // Rule 1: Explicit override
    if (explicitCollation.HasValue)
        return explicitCollation.Value;

    // Rule 2: Same collation
    if (leftCollation == rightCollation)
        return leftCollation;

    // Rule 3: Mismatch - use left, warn
    warningCallback?.Invoke(
        $"JOIN collation mismatch: left={leftCollation}, right={rightCollation}. Using left column collation.");
    
    return leftCollation;
}

/// <summary>
/// Gets an IComparer&lt;string&gt; for the specified collation.
/// Used for sorting in MERGE JOIN and ORDER BY.
/// </summary>
public static IComparer<string> GetComparer(CollationType collation)
{
    return new CollationAwareComparer(collation);
}
```

### Task 2: Extend SqlParser.Helpers.cs

**File:** `src/SharpCoreDB/Services/SqlParser.Helpers.cs`

**Method:** `EvaluateJoinWhere()` (existing method - needs collation parameter)

**Changes:**
- Add `CollationType leftCollation` parameter
- Add `CollationType rightCollation` parameter
- Pass collations to `EvaluateOperator()`
- Resolve collation using `ResolveJoinCollation()`

**Signature Change:**
```csharp
// OLD
internal bool EvaluateJoinWhere(
    Dictionary<string, object> leftRow,
    Dictionary<string, object> rightRow,
    string whereClause)

// NEW
internal bool EvaluateJoinWhere(
    Dictionary<string, object> leftRow,
    Dictionary<string, object> rightRow,
    string whereClause,
    CollationType leftCollation,
    CollationType rightCollation,
    CollationType? explicitCollation = null)
```

### Task 3: Update Hash JOIN in SqlParser.DML.cs

**File:** `src/SharpCoreDB/Services/SqlParser.DML.cs`

**Method:** Look for hash-based JOIN implementation (likely in `ExecuteJoin()` or similar)

**Changes:**
1. Get column collations from left and right tables
2. Resolve JOIN collation
3. Create `CollationAwareEqualityComparer` with resolved collation
4. Pass comparer to `Dictionary<string, List<Dictionary<string, object>>>` constructor

**Example:**
```csharp
// Get collations
var leftColIdx = leftTable.Columns.IndexOf(leftColumn);
var rightColIdx = rightTable.Columns.IndexOf(rightColumn);

var leftCollation = leftColIdx >= 0 && leftColIdx < leftTable.ColumnCollations.Count
    ? leftTable.ColumnCollations[leftColIdx]
    : CollationType.Binary;

var rightCollation = rightColIdx >= 0 && rightColIdx < rightTable.ColumnCollations.Count
    ? rightTable.ColumnCollations[rightColIdx]
    : CollationType.Binary;

// Resolve collation
var collation = CollationComparator.ResolveJoinCollation(
    leftCollation, rightCollation, explicitCollation: null,
    warningCallback: msg => Console.WriteLine($"[JOIN WARNING] {msg}"));

// Create hash table with collation-aware comparer
var comparer = new CollationAwareEqualityComparer(collation);
var hashTable = new Dictionary<string, List<Dictionary<string, object>>>(comparer);
```

### Task 4: Update Nested Loop JOIN

**Same file:** `src/SharpCoreDB/Services/SqlParser.DML.cs`

**Changes:**
1. Resolve JOIN collation
2. Use `CollationComparator.Equals()` for JOIN condition evaluation
3. Pass collation through to `EvaluateJoinWhere()`

### Task 5: Update Merge JOIN (If Implemented)

**Same file:** `src/SharpCoreDB/Services/SqlParser.DML.cs`

**Changes:**
1. Resolve JOIN collation
2. Sort inputs using `CollationComparator.GetComparer()`
3. Use collation-aware comparison during merge phase

### Task 6: CompiledQueryExecutor Support

**File:** `src/SharpCoreDB/Services/CompiledQueryExecutor.cs`

**Method:** `ExecuteJoin()` or similar compiled JOIN execution

**Changes:**
1. Store collation metadata in compiled plan
2. Use collation-aware comparers in compiled JOIN logic
3. Maintain performance with cached comparers

---

## Test Plan

### Unit Tests (`CollationJoinTests.cs`)

```csharp
[Fact]
public void Join_BothColumnsNoCase_ShouldMatchCaseInsensitive()
{
    // users.email COLLATE NOCASE, orders.user_email COLLATE NOCASE
    // JOIN should match 'alice@example.com' with 'ALICE@EXAMPLE.COM'
}

[Fact]
public void Join_LeftNoCaseRightBinary_ShouldUseLeftCollation()
{
    // users.email COLLATE NOCASE, orders.user_email COLLATE BINARY
    // JOIN should use NOCASE (left wins), emit warning
}

[Fact]
public void Join_ExplicitCollateOverride_ShouldUseExplicitCollation()
{
    // SELECT * FROM t1 JOIN t2 ON t1.col COLLATE BINARY = t2.col
    // Should use BINARY regardless of column collations
}

[Fact]
public void Join_HashStrategy_WithNoCase_ShouldBuildCorrectHashTable()
{
    // Verify hash table normalization with NOCASE
}

[Fact]
public void Join_NestedLoop_WithRTrim_ShouldIgnoreTrailingSpaces()
{
    // Verify RTrim collation in nested loop JOIN
}

[Fact]
public void Join_MultipleColumns_DifferentCollations_ShouldResolveIndependently()
{
    // JOIN on (t1.a = t2.a AND t1.b = t2.b) with different collations per column
}
```

### Integration Tests (`CollationJoinIntegrationTests.cs`)

```sql
-- Test 1: INNER JOIN with NOCASE
CREATE TABLE users (id INT, email TEXT COLLATE NOCASE);
CREATE TABLE orders (id INT, user_email TEXT COLLATE NOCASE);
INSERT INTO users VALUES (1, 'alice@example.com');
INSERT INTO orders VALUES (1, 'ALICE@EXAMPLE.COM', 100.00);
SELECT * FROM users u JOIN orders o ON u.email = o.user_email;
-- Expected: 1 row

-- Test 2: LEFT JOIN with collation mismatch
CREATE TABLE t1 (id INT, name TEXT COLLATE NOCASE);
CREATE TABLE t2 (id INT, name TEXT COLLATE BINARY);
INSERT INTO t1 VALUES (1, 'Alice');
INSERT INTO t2 VALUES (1, 'alice');
SELECT * FROM t1 LEFT JOIN t2 ON t1.name = t2.name;
-- Expected: 1 row (uses t1 collation - NOCASE)

-- Test 3: CROSS JOIN (no collation needed)
SELECT * FROM t1 CROSS JOIN t2;
-- Expected: Cartesian product

-- Test 4: Self JOIN with same collation
CREATE TABLE employees (id INT, name TEXT COLLATE NOCASE, manager_id INT);
SELECT e.*, m.name AS manager_name 
FROM employees e 
JOIN employees m ON e.manager_id = m.id;
-- Expected: Correct self-join results
```

### Performance Benchmarks (`Phase7_JoinCollationBenchmark.cs`)

```csharp
[Benchmark]
public void HashJoin_10K_Rows_NoCase()
{
    // Measure hash JOIN with NOCASE collation
}

[Benchmark]
public void HashJoin_10K_Rows_Binary()
{
    // Baseline: hash JOIN with BINARY collation
}

[Benchmark]
public void NestedLoop_1K_Rows_NoCase()
{
    // Measure nested loop JOIN with NOCASE
}

[Benchmark]
public void MergeJoin_10K_Rows_NoCase()
{
    // Measure merge JOIN with NOCASE (if implemented)
}
```

---

## Performance Considerations

### Hash JOIN Optimization
- **Impact:** Minimal overhead (~5%) for NOCASE collation
- **Reason:** `OrdinalIgnoreCase` comparer is highly optimized
- **Mitigation:** Cache `CollationAwareEqualityComparer` instances

### Nested Loop JOIN
- **Impact:** Moderate overhead (~10-15%) for non-Binary collations
- **Reason:** Per-comparison function call overhead
- **Mitigation:** Use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on comparison helpers

### Merge JOIN
- **Impact:** Sorting overhead depends on collation
- **Reason:** Culture-aware comparisons are slower
- **Mitigation:** Use fast path for Binary and NoCase collations

### Query Compilation
- **Strategy:** Compile collation checks into query plan
- **Benefit:** Avoid runtime collation resolution overhead
- **Implementation:** Store resolved collation in `CompiledJoinNode`

---

## Backward Compatibility

✅ **Fully Backward Compatible**
- Existing JOINs use BINARY collation (default)
- No behavior change for non-collated tables
- New collation awareness is opt-in via column definitions

---

## Known Limitations

### Phase 7 Scope
- ✅ INNER JOIN, LEFT JOIN, RIGHT JOIN, FULL JOIN
- ✅ Hash JOIN strategy with collation
- ✅ Nested loop JOIN with collation
- ✅ Collation resolution rules
- ❌ MERGE JOIN (if not implemented yet)
- ❌ Subquery JOIN collation propagation (deferred to Phase 8)

### Future Enhancements (Phase 8+)
- SIMD-accelerated JOIN key comparison
- Parallel hash JOIN with collation partitioning
- Adaptive JOIN strategy based on collation (hash vs. sort-merge)
- Collation-aware join reordering hints

---

## Success Criteria

| Criterion | Target | Measurement |
|-----------|--------|-------------|
| Functional correctness | 100% | All test cases pass |
| Performance overhead (NOCASE) | <10% | Benchmark comparison |
| Backward compatibility | 100% | Existing tests pass |
| Warning detection | 100% | Mismatch warnings logged |
| Documentation | Complete | User guide + examples |

---

## Dependencies

### Required Before Phase 7
- ✅ Phase 5: `CollationComparator` utility
- ✅ Phase 5: `CollationAwareEqualityComparer`
- ✅ Phase 6: Schema metadata complete

### Enables After Phase 7
- Phase 8: Subquery collation handling
- Phase 9: SIMD-accelerated JOIN operations
- Phase 10: Multi-table collation optimization

---

## Timeline

| Task | Estimated Time | Priority |
|------|---------------|----------|
| Collation resolution utility | 2 hours | P0 |
| Hash JOIN implementation | 4 hours | P0 |
| Nested loop JOIN implementation | 3 hours | P0 |
| Merge JOIN implementation | 3 hours | P1 |
| CompiledQueryExecutor updates | 4 hours | P1 |
| Test suite | 4 hours | P0 |
| Performance benchmarks | 2 hours | P1 |
| Documentation | 2 hours | P0 |
| **Total** | **24 hours** | |

---

## Next Steps

1. **Create Task Branch:** `feature/collation-phase7-join`
2. **Implement Core Utilities:** `ResolveJoinCollation()` in `CollationComparator.cs`
3. **Update Hash JOIN:** Modify `SqlParser.DML.cs` JOIN execution
4. **Add Tests:** Create comprehensive test suite
5. **Benchmark:** Validate performance overhead
6. **Document:** Add examples to user guide
7. **Code Review:** Submit PR for review
8. **Merge:** Complete Phase 7

---

## References

- **Phase 5 Complete:** `COLLATE_PHASE5_COMPLETE.md`
- **Phase 6 Complete:** `COLLATE_PHASE6_COMPLETE.md`
- **CollationComparator:** `src/SharpCoreDB/CollationComparator.cs`
- **SqlParser.Helpers:** `src/SharpCoreDB/Services/SqlParser.Helpers.cs`
- **SqlParser.DML:** `src/SharpCoreDB/Services/SqlParser.DML.cs`

---

**Status:** 🚀 Ready to implement  
**Expected Completion:** Phase 7 completion  
**Next Phase:** Phase 8 - Subquery and nested query collation handling
