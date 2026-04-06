# Storage Mode Guidance — v1.7.0

**Applies to:** SharpCoreDB v1.7.0+ (.NET 10 / C# 14)

---

## Overview

SharpCoreDB supports two primary table storage modes, specified via the `STORAGE` clause in `CREATE TABLE`:

```sql
-- Columnar (default): Optimized for append-heavy analytics workloads
CREATE TABLE logs (ts DATETIME, msg TEXT) STORAGE = COLUMNAR

-- Page-Based: Optimized for OLTP with frequent updates/deletes
CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT) STORAGE = PAGE_BASED
```

If omitted, the default is **Columnar** (unless overridden by `DatabaseConfig.StorageEngineType`).

---

## When to Use Each Mode

| Criteria | Columnar | Page-Based |
|---|---|---|
| Insert-heavy, append-only | ✅ Best | Good |
| Frequent UPDATE/DELETE | ⚠️ See below | ✅ Best |
| Analytics / full table scan | ✅ Best | Good |
| Point lookups by PK | Good | ✅ Best |
| Tables without PRIMARY KEY | ⚠️ See below | ✅ Works |

---

## Columnar Mode: DELETE/UPDATE Behavior

### With a PRIMARY KEY

DELETE and UPDATE work correctly. The PK B-tree index is used to locate storage positions for logical deletion (index removal). Physical space is reclaimed during compaction.

### Without a PRIMARY KEY (fixed in v1.7.0)

> **History:** Prior to v1.7.0, DELETE on Columnar tables without a PRIMARY KEY was a **silent no-op** — rows were not removed, no error was raised. This was a critical data integrity bug.

As of v1.7.0, Columnar DELETE without a PK falls back to a full storage scan (`engine.GetAllRecords()`) to locate matching rows. This works correctly but is **O(n)** for every delete operation.

### Recommendation

For tables that require DELETE or UPDATE operations, either:

1. **Define a PRIMARY KEY** — enables efficient index-based row location:
   ```sql
   CREATE TABLE snapshots (id TEXT PRIMARY KEY, stream_id TEXT, version LONG, data TEXT)
   ```

2. **Use `PAGE_BASED` storage** — supports efficient in-place delete without a PK:
   ```sql
   CREATE TABLE snapshots (stream_id TEXT, version LONG, data TEXT) STORAGE = PAGE_BASED
   ```

3. **Avoid delete-heavy patterns on Columnar tables without a PK** — the full-scan fallback works but is slow for large tables.

---

## Internal Implementation Notes

### Columnar Delete Flow (with PK)
1. `Select(where)` → finds matching rows via hash indexes
2. PK B-tree index → resolves storage positions
3. Remove from PK index + hash indexes (logical delete)
4. Physical space reclaimed during auto-compaction

### Columnar Delete Flow (without PK — fallback)
1. `engine.GetAllRecords()` → full storage scan
2. `DeserializeRowFromSpan()` + `EvaluateSimpleWhere()` → filter matches
3. Remove from hash indexes using storage position
4. Physical space reclaimed during auto-compaction

### Page-Based Delete Flow
1. `engine.GetAllRecords()` → full storage scan
2. `DeserializeRowFromSpan()` + `EvaluateSimpleWhere()` → filter matches
3. `engine.Delete()` → marks page slot as physically deleted
4. Freed pages tracked for reuse

---

## Future Consideration: Auto-Generated ROWID

A potential enhancement is to auto-generate an implicit `_rowid` column (similar to SQLite's `rowid`) for Columnar tables that lack an explicit PRIMARY KEY. This would:

- Enable efficient delete/update without user-defined PKs
- Maintain backward compatibility (hidden from `SELECT *`)
- Require careful handling in INSERT (auto-increment), schema serialization, and migration

This is tracked as a future improvement. For now, the full-scan fallback ensures correctness.
