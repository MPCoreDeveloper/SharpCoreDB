# Auto-ROWID: Automatic ULID Primary Key

**Version:** 1.6.0  
**Status:** ✅ Production-Ready  
**Last Updated:** July 2025

---

## Overview

SharpCoreDB automatically injects a hidden `_rowid` column as the primary key when a table is created **without an explicit `PRIMARY KEY`** definition. This follows the [SQLite rowid pattern](https://www.sqlite.org/rowidtable.html) but uses **ULID** (Universally Unique Lexicographically Sortable Identifier) instead of a monotonic integer.

### Why ULID?

| Property | ULID | Integer Auto-Increment |
|----------|------|----------------------|
| **Globally Unique** | ✅ Timestamp + random | ❌ Requires counter coordination |
| **Lexicographically Sortable** | ✅ Time-ordered | ✅ Monotonic |
| **Conflict-Free** | ✅ No coordination needed | ❌ Conflicts in distributed scenarios |
| **B-Tree Friendly** | ✅ Compact, sortable | ✅ Sequential |
| **External Dependencies** | ✅ None (built-in) | ✅ None |

---

## How It Works

### Table Creation

When you create a table without a `PRIMARY KEY`:

```sql
CREATE TABLE logs (
    message TEXT,
    level INTEGER,
    timestamp DATETIME
)
```

SharpCoreDB automatically injects a hidden `_rowid` column:

```
Internal schema: _rowid (ULID, AUTO, PRIMARY KEY, NOT NULL), message (TEXT), level (INTEGER), timestamp (DATETIME)
```

When you create a table **with** an explicit `PRIMARY KEY`, no `_rowid` is injected:

```sql
CREATE TABLE users (
    id INTEGER PRIMARY KEY AUTO,
    name TEXT,
    email TEXT
)
-- No _rowid column is added; 'id' is the primary key
```

### Querying

#### `SELECT *` — `_rowid` is Hidden

```sql
SELECT * FROM logs
```

Returns:

| message | level | timestamp |
|---------|-------|-----------|
| "Server started" | 1 | 2025-07-01 12:00:00 |
| "Request received" | 2 | 2025-07-01 12:00:01 |

The `_rowid` column is **not included** in `SELECT *` results.

#### Explicit `SELECT _rowid` — `_rowid` is Visible

```sql
SELECT _rowid, message, level FROM logs
```

Returns:

| _rowid | message | level |
|--------|---------|-------|
| 01J5ABCDEF0001GHIJKL000001 | "Server started" | 1 |
| 01J5ABCDEF0001GHIJKL000002 | "Request received" | 2 |

You can also use `_rowid` in `WHERE` clauses:

```sql
SELECT * FROM logs WHERE _rowid = '01J5ABCDEF0001GHIJKL000001'
```

### INSERT Behavior

When inserting into a table with an internal `_rowid`, you do **not** need to specify it:

```sql
-- Both of these work correctly:
INSERT INTO logs VALUES ('Error occurred', 3, '2025-07-01 12:00:02')
INSERT INTO logs (message, level, timestamp) VALUES ('Warning', 2, '2025-07-01 12:00:03')
```

The `_rowid` is automatically generated using `Ulid.NewUlid()`.

### DELETE and UPDATE

The `_rowid` is used internally as the primary key for efficient DELETE and UPDATE operations. The B-Tree index on `_rowid` enables O(log n) lookups instead of full table scans:

```sql
-- Efficient: Uses _rowid B-Tree index internally
DELETE FROM logs WHERE level = 3

-- Also efficient: Direct _rowid lookup
DELETE FROM logs WHERE _rowid = '01J5ABCDEF0001GHIJKL000001'
```

---

## Architecture

### Storage Impact

- **Column**: `_rowid` is stored as the first column in the table schema
- **Type**: `DataType.Ulid` — 26 characters, Crockford Base32 encoded
- **Storage overhead**: ~31 bytes per row (1 null flag + 4 length prefix + 26 chars)
- **Index**: Automatic B-Tree index + hash index (same as explicit PKs)

### Property: `HasInternalRowId`

The `Table.HasInternalRowId` property (persisted in metadata) indicates whether a table has an auto-generated `_rowid`. This property controls:

1. **SELECT behavior**: `_rowid` is stripped from `Select()` results, available via `SelectIncludingRowId()`
2. **INSERT behavior**: SQL parser skips `_rowid` in user column mapping
3. **Metadata**: `ColumnInfo.IsHidden = true` for `_rowid` columns
4. **Persistence**: Saved and restored across database reopens

### Metadata Schema

The `HasInternalRowId` field is included in the table metadata JSON:

```json
{
  "Name": "logs",
  "Columns": ["_rowid", "message", "level", "timestamp"],
  "ColumnTypes": [9, 2, 0, 6],
  "PrimaryKeyIndex": 0,
  "HasInternalRowId": true,
  "IsAuto": [true, false, false, false],
  ...
}
```

### Backward Compatibility

- **Existing databases**: Tables created before this feature have `HasInternalRowId = false` (the default). No behavior change.
- **Existing tables with explicit PKs**: Unaffected. `HasInternalRowId` is only `true` for tables created without a PK.
- **Metadata format**: New field with default `false` — old versions can safely ignore it.

---

## API Reference

### Table Properties

```csharp
/// <summary>
/// Gets whether this table has an auto-generated internal _rowid column.
/// </summary>
public bool HasInternalRowId { get; set; }
```

### Select Methods

```csharp
// Standard: strips _rowid from results (default behavior)
List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt);

// Raw: includes _rowid in results (for explicit _rowid queries)
List<Dictionary<string, object>> SelectIncludingRowId(string? where, string? orderBy, bool asc, bool noEncrypt);
```

### Metadata Discovery

```csharp
// Default: returns only user-visible columns (excludes _rowid)
// Follows the SQLite PRAGMA table_info pattern.
IReadOnlyList<ColumnInfo> GetColumns(string tableName);

// Full: returns ALL columns including hidden _rowid (with IsHidden = true)
// Use this when you need to inspect the complete internal schema.
IReadOnlyList<ColumnInfo> GetColumnsIncludingHidden(string tableName);
```

### ColumnInfo

```csharp
/// <summary>
/// Whether this column is a hidden internal column (e.g., auto-generated _rowid).
/// </summary>
public bool IsHidden { get; init; }
```

### Constants

```csharp
/// <summary>
/// The name of the auto-generated internal row identifier column.
/// </summary>
public const string InternalRowIdColumnName = "_rowid";
```

---

## Performance Characteristics

| Operation | Without _rowid (old) | With _rowid (new) |
|-----------|---------------------|-------------------|
| **DELETE (columnar, no PK)** | Full storage scan O(n) | B-Tree lookup O(log n) ✅ |
| **UPDATE (columnar, no PK)** | Full scan for position | B-Tree lookup O(log n) ✅ |
| **INSERT** | Same | +1 ULID generation (~100ns) |
| **SELECT *** | Same | +1 dict.Remove per row (~5ns) |
| **Storage** | Same | +31 bytes per row |

The DELETE/UPDATE performance improvement far outweighs the minimal INSERT and SELECT overhead.

---

## Comparison with SQLite

| Feature | SQLite `rowid` | SharpCoreDB `_rowid` |
|---------|---------------|---------------------|
| **Type** | 64-bit integer | ULID (26-char string) |
| **Visibility** | Hidden in `SELECT *` | Hidden in `SELECT *` ✅ |
| **Explicit query** | `SELECT rowid, ...` | `SELECT _rowid, ...` ✅ |
| **Auto-generated** | Yes (monotonic) | Yes (timestamp + random) ✅ |
| **Distributed-safe** | No | Yes ✅ |
| **Tables with explicit PK** | rowid = PK alias | No _rowid injected ✅ |

---

## FAQ

**Q: Does `_rowid` affect my existing tables?**  
A: No. Only tables created **after** this feature without an explicit PRIMARY KEY get a `_rowid`. Existing tables are unaffected.

**Q: Can I use `_rowid` in WHERE/ORDER BY?**  
A: Yes. The `_rowid` is a real column that can be queried explicitly. It's only hidden from `SELECT *`.

**Q: What's the storage overhead?**  
A: ~31 bytes per row. For a table with 1 million rows, that's about 30 MB — a small price for efficient DELETE/UPDATE operations.

**Q: Can I disable auto-`_rowid`?**  
A: Yes — simply define an explicit PRIMARY KEY on your table, and no `_rowid` will be injected.

**Q: Is `_rowid` persisted across database restarts?**  
A: Yes. The `HasInternalRowId` flag and the `_rowid` column are fully persisted in metadata and data files.

**Q: Does `_rowid` work with `ExecuteBatchSQL` and `BulkInsertAsync`?**  
A: Yes. All insert paths (SQL parsing, batch SQL, prepared statements, direct `InsertBatch`, optimized `BulkInsertAsync`, and `InsertBatchFromBuffer`) correctly skip the internal `_rowid` column during value mapping and auto-generate it during row validation.

**Q: How does `GetColumns()` behave with `_rowid`?**  
A: `GetColumns()` (the `IMetadataProvider` interface method) follows the SQLite `PRAGMA table_info` pattern and **excludes** hidden `_rowid` columns. Use `GetColumnsIncludingHidden()` on the `Database` class to see all columns with `IsHidden = true` on internal ones.

---

## Implementation Details

### Insert Path Coverage

The `_rowid` auto-generation is handled consistently across **all** insert paths:

| Insert Path | Skip _rowid | Auto-Generate | Location |
|------------|-------------|---------------|----------|
| `ExecuteSQL("INSERT ...")` | ✅ SqlParser.DML.cs | Via `Table.Insert()` | `ExecuteInsert()` |
| `ExecuteBatchSQL(...)` | ✅ Database.Batch.cs | Via `Table.InsertBatch()` | `ParseInsertStatement()` / `GetOrCreatePreparedInsert()` |
| `BulkInsertAsync(...)` (< 5K rows) | N/A (dict API) | Via `Table.InsertBatch()` | `ValidateAndSerializeBatchOutsideLock()` |
| `BulkInsertAsync(...)` (≥ 5K rows) | N/A (dict API) | Via `InsertBatchFromBuffer()` → `InsertBatch()` | `ValidateAndSerializeBatchOutsideLock()` |
| `InsertBatch(rows)` direct | N/A (dict API) | ✅ Auto-generates when key missing or null | `ValidateAndSerializeBatchOutsideLock()` |
| `InsertBatchFromBuffer(...)` | N/A (binary API) | ✅ Decoder produces null → auto-gen triggers | `ValidateAndSerializeBatchOutsideLock()` |

### Internal Operations

DELETE and UPDATE use `SelectInternal()` (instead of public `Select()`) to preserve the `_rowid` column in intermediate results. This ensures the B-Tree PK index lookup works correctly when locating storage positions for row mutation.

### Schema Discovery

```
┌─────────────────────────────────────────────────┐
│          GetColumns("logs")                     │
│  → [message (TEXT), level (INTEGER)]            │
│  _rowid is EXCLUDED (SQLite PRAGMA pattern)     │
├─────────────────────────────────────────────────┤
│       GetColumnsIncludingHidden("logs")         │
│  → [_rowid (ULID, hidden), message (TEXT),      │
│     level (INTEGER)]                            │
│  _rowid INCLUDED with IsHidden = true           │
└─────────────────────────────────────────────────┘
```

### Auto-Generation Guard

The `ValidateAndSerializeBatchOutsideLock()` method (used by all batch paths) handles three scenarios for auto-generated columns:

1. **Key missing**: `!row.TryGetValue("_rowid", ...)` → auto-generate
2. **Key present with null/DBNull**: Common when rows pass through `StreamingRowEncoder` → `BinaryRowDecoder` → auto-generate
3. **Key present with valid value**: Use as-is (e.g., explicit `_rowid` in INSERT)

This defensive approach prevents "Column '_rowid' cannot be NULL" errors across all insert paths.
