# COLLATE Support Phase 6 Planning - Schema Migration & ALTER TABLE

**Date:** 2025-01-28  
**Status:** 🚀 PLANNED  
**Target Completion:** Phase 6 completion

---

## Executive Summary

Phase 6 extends collation support to **schema evolution and migration scenarios**. This phase enables:

- ✅ ALTER TABLE MODIFY COLUMN to change collation on existing columns
- ✅ Schema migration with data revalidation
- ✅ Collation-aware index rebuilding
- ✅ Safe schema transitions with validation
- ✅ Migration tools for bulk collation updates
- ✅ Backward compatibility with non-collation schemas

### Key Objectives

1. **ALTER TABLE Support** - Modify column collations without losing data
2. **Schema Migration** - Safe migration from binary to NoCase or other collations
3. **Index Management** - Rebuild indexes with new collation rules
4. **Validation Framework** - Ensure data integrity during migration
5. **Migration Tools** - Utility methods for common migration scenarios
6. **Zero-Downtime Updates** - Minimize impact on running applications

---

## What's Been Completed (Phases 1-5)

### Phases 1-4: Foundation & Infrastructure
- ✅ Schema support (CollationType enum, ColumnCollations)
- ✅ Parser integration (SQL DDL parsing with COLLATE clause)
- ✅ Storage layer (Collation persistence)
- ✅ Index integration (B-Tree and Hash indexes with collation)

### EF Core Integration
- ✅ Migrations support (COLLATE in DDL)
- ✅ Query translation (EF.Functions.Collate())
- ✅ StringComparison support

### Phase 5: Runtime Optimization
- ✅ WHERE clause filtering with collation
- ✅ DISTINCT deduplication
- ✅ GROUP BY grouping
- ✅ ORDER BY sorting
- ✅ LIKE pattern matching

---

## Phase 6 Scope: Schema Migration & ALTER TABLE

### 6.1 ALTER TABLE MODIFY COLUMN (Collation Change)

**Current Status:** Not implemented  
**What Needs Implementation:**

1. **SQL Support:**
   ```sql
   ALTER TABLE users MODIFY COLUMN email TEXT COLLATE NOCASE;
   ALTER TABLE orders MODIFY COLUMN status TEXT COLLATE RTRIM;
   ALTER TABLE products MODIFY COLUMN name TEXT COLLATE UNICODE_CASE_INSENSITIVE;
   ```

2. **Change Detection:**
   - Only reindex/revalidate if collation actually changes
   - Preserve data, update collation metadata
   - Update ColumnCollations list

3. **Workflow:**
   ```
   ALTER TABLE → Parse → Validate collation → 
   Update schema → Rebuild affected indexes → 
   Revalidate data → Success
   ```

### 6.2 Collation Change Validation

**Current Status:** Not implemented  
**What Needs Implementation:**

1. **Data Validation:**
   - Check for duplicate values that become duplicates under new collation
   - Example: Binary → NoCase: "alice" and "ALICE" are now duplicates
   - Verify UNIQUE constraints still hold

2. **Validation Rules:**
   - **Binary → NoCase:** May create duplicates
   - **NoCase → Binary:** No issues (already handled)
   - **Any → Any:** Check CHECK constraints
   - **Impact on PRIMARY KEY:** Reject if PK collation changes

3. **Validation Result:**
   - ✅ Safe: No conflicts detected
   - ⚠️ Warning: Duplicates exist, manual review needed
   - ❌ Error: UNIQUE/PK violation

### 6.3 Schema Migration Tools

**Current Status:** Not implemented  
**What Needs Implementation:**

1. **Migration Scenarios:**

   **A) Rename Column (Preserves Collation)**
   ```csharp
   table.RenameColumn("old_name", "new_name");
   // Preserves collation, updates indexes
   ```

   **B) Change Column Collation**
   ```csharp
   table.ChangeColumnCollation("email", CollationType.NoCase);
   // Validates, rebuilds indexes, updates metadata
   ```

   **C) Bulk Collation Update (All String Columns)**
   ```csharp
   table.UpdateAllStringColumnCollations(CollationType.NoCase);
   // Safe, with validation
   ```

   **D) Migration with Duplicate Resolution**
   ```csharp
   table.ChangeColumnCollationWithDedup("email", CollationType.NoCase, 
       keepStrategy: KeepDuplicateStrategy.KeepFirst); // or KeepLast
   ```

2. **Index Rebuilding:**
   - Automatic detection of affected indexes
   - Rebuild hash indexes with new collation
   - Update B-Tree primary key index if needed (REJECT if PK changes)
   - Preserve index state (loaded/stale)

3. **Migration Logging:**
   - Before/after metadata snapshots
   - Data affected (row counts, duplicates)
   - Index rebuild statistics
   - Validation results

### 6.4 Transaction & Rollback

**Current Status:** Not implemented  
**What Needs Implementation:**

1. **Transaction Support:**
   - ALTER TABLE within database transaction
   - Atomic: all-or-nothing semantics
   - Rollback on validation failure

2. **Checkpoint Strategy:**
   - Create backup before major schema change
   - Store old collation metadata
   - Ability to revert if needed

### 6.5 Backward Compatibility

**Current Status:** Already supported via Phase 1  
**What Remains:**

- Ensure non-collation tables unaffected
- Support mixed schema (some columns with collation, some without)
- Graceful defaults for legacy columns

---

## Implementation Tasks

### Task 6.1: ALTER TABLE Parser Enhancement
**File:** `src/SharpCoreDB/Services/SqlParser.DDL.cs`  
**Changes:**
- Parse `ALTER TABLE ... MODIFY COLUMN ... COLLATE ...` syntax
- Generate AST node with old and new collation
- Validation during parsing

**Example:**
```csharp
// SQL: ALTER TABLE users MODIFY COLUMN email TEXT COLLATE NOCASE
public class AlterTableModifyColumn
{
    public string TableName { get; set; }
    public string ColumnName { get; set; }
    public string DataType { get; set; }
    public string? NewCollation { get; set; }
    public CollationType? NewCollationType { get; set; }
}
```

### Task 6.2: Table Schema Modification Methods
**File:** `src/SharpCoreDB/DataStructures/Table.Migration.cs` (NEW)  
**Methods:**

```csharp
/// <summary>
/// Modifies a column's collation.
/// Validates that the change is safe, rebuilds indexes.
/// </summary>
public void ModifyColumnCollation(
    string columnName, 
    CollationType newCollation,
    bool validateData = true);

/// <summary>
/// Validates if collation change is safe.
/// Returns validation result with warnings/errors.
/// </summary>
public CollationChangeValidationResult ValidateCollationChange(
    string columnName,
    CollationType newCollation);

/// <summary>
/// Changes collation with duplicate resolution strategy.
/// For cases where new collation creates duplicates.
/// </summary>
public void ChangeColumnCollationWithDedup(
    string columnName,
    CollationType newCollation,
    KeepDuplicateStrategy keepStrategy = KeepDuplicateStrategy.KeepFirst);

/// <summary>
/// Updates all string columns to use the same collation.
/// </summary>
public void UpdateAllStringColumnCollations(CollationType newCollation);

/// <summary>
/// Renames a column while preserving collation.
/// </summary>
public void RenameColumn(string oldName, string newName);
```

### Task 6.3: Validation & Migration Utilities
**File:** `src/SharpCoreDB/Services/CollationMigrationValidator.cs` (NEW)  
**Purpose:** Central validation logic

```csharp
public class CollationMigrationValidator
{
    /// <summary>
    /// Validates if changing from old to new collation is safe.
    /// Returns conflicts, duplicates, and warnings.
    /// </summary>
    public static ValidationResult Validate(
        Table table,
        string columnName,
        CollationType oldCollation,
        CollationType newCollation);
}

public class ValidationResult
{
    public bool IsSafe { get; set; }
    public List<string> Errors { get; set; }
    public List<string> Warnings { get; set; }
    public int DuplicatesFound { get; set; }
    public int RowsAffected { get; set; }
}
```

### Task 6.4: Integration Tests
**File:** `tests/SharpCoreDB.Tests/CollationPhase6Tests.cs` (NEW)  
**Test Cases:**
1. ALTER TABLE change collation (valid)
2. ALTER TABLE create duplicates (invalid - detection)
3. ALTER TABLE with deduplication strategy
4. Rename column (preserves collation)
5. Bulk update string columns
6. Transaction rollback on validation failure
7. Index rebuild after collation change
8. Multiple columns with different collations

### Task 6.5: Performance Benchmarks
**File:** `tests/SharpCoreDB.Benchmarks/Phase6_SchemaMigrationBenchmark.cs` (NEW)  
**Scenarios:**
- Change collation on 10K row table
- Reindex after collation change
- Bulk column collation update
- Validation performance (duplicate detection)

---

## Success Criteria

✅ **Functional:**
- ALTER TABLE MODIFY COLUMN supports collation changes
- Validation detects conflicts and duplicates
- Safe deduplication strategies available
- Indexes rebuild with new collation
- Transactions roll back on errors

✅ **Performance:**
- Collation change: <100ms for 1K rows
- Validation: <50ms (pre-computed hashes)
- Index rebuild: O(n)

✅ **Testing:**
- 8+ integration tests with >95% code coverage
- Benchmarks demonstrate performance
- All existing tests still pass (no regression)

✅ **Documentation:**
- Phase 6 completion document
- Migration guide for users
- Examples of common scenarios

---

## Implementation Priority

| Task | Priority | Complexity | Depends On |
|------|----------|-----------|-----------|
| 6.1: Parser enhancement | HIGH | MEDIUM | Phase 2 parser |
| 6.2: Table methods | HIGH | MEDIUM | 6.1 |
| 6.3: Validation | HIGH | HIGH | Phase 5 query ops |
| 6.4: Tests | HIGH | MEDIUM | 6.2, 6.3 |
| 6.5: Benchmarks | MEDIUM | LOW | 6.4 |

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Data corruption | CRITICAL | Comprehensive validation, transactions |
| Performance degradation | HIGH | Benchmarks, optimization before merge |
| Index inconsistency | HIGH | Automatic rebuild, validation |
| Backward compatibility break | MEDIUM | Non-collation tables unaffected |
| Concurrent schema changes | MEDIUM | Write lock during ALTER TABLE |

---

## Related Phases

| Phase | Status | Relation |
|-------|--------|----------|
| Phase 1-4 | ✅ COMPLETE | Foundation |
| Phase 5 | ✅ COMPLETE | Query operations |
| **Phase 6** | 🚀 PLANNED | Schema evolution |
| Phase 7+ | TODO | SIMD optimization, JOIN support |

---

## Timeline Estimate

| Phase | Estimated Time |
|-------|---|
| Step 1: Planning | 0.5 hours |
| Step 2: Parser | 1.5 hours |
| Step 3: Table methods | 2 hours |
| Step 4: Validation | 1.5 hours |
| Step 5: Tests | 1.5 hours |
| Step 6: Benchmarks | 1 hour |
| Step 7: Documentation | 0.5 hours |
| Step 8: Build & validate | 0.5 hours |
| **Total** | **~9 hours** |

---

## Known Limitations & Future Work

### Phase 6 Scope
- ✅ ALTER TABLE MODIFY COLUMN for collation
- ✅ Validation and safety checks
- ✅ Index management
- ⏱️ Transactions and rollback

### Phase 7+ Opportunities
- JOIN operations with collation awareness
- Subquery collation handling
- COLLATE in HAVING clauses
- SIMD-accelerated collation comparisons
- Distributed schema migration
- Zero-downtime migrations (via versioning)

---

## Success Metrics

After Phase 6 completion:

1. **Functionality:** 100% of ALTER TABLE operations supported
2. **Reliability:** All validation scenarios covered (8+ tests)
3. **Performance:** Schema changes <100ms for reasonable datasets
4. **Safety:** 0 data corruption incidents (validation prevents)
5. **Compatibility:** No breaking changes to existing APIs

---

## Dependencies

- **Phase 1-5**: All complete ✅
- **Parser (Phase 2)**: Already supports DDL
- **Query operations (Phase 5)**: Used for validation
- **CollationComparator (Phase 5)**: Used for comparisons

---

## Next Steps After Phase 6

1. **Phase 7:** JOIN operations with collation awareness
2. **Phase 8:** SIMD-optimized collation comparisons
3. **Phase 9:** Distributed schema management
4. **Phase 10:** Zero-downtime migration strategies

---

